using CycleSharingMcp.Core.Gbfs;

namespace CycleSharingMcp.Core;

public sealed class BikeshareService(IFeedSource feeds)
{
    public const int MaxResults = 40;

    public async Task<PortListResponse> SearchNearbyAsync(double latitude, double longitude, string? @operator, int limit, CancellationToken ct)
    {
        if (latitude is < -90 or > 90 || double.IsNaN(latitude))
            throw new BikeshareException("緯度（latitude）は -90〜90 で指定してください。緯度と経度を取り違えていませんか（日本付近は緯度 20〜46、経度 122〜154）。");
        if (longitude is < -180 or > 180 || double.IsNaN(longitude))
            throw new BikeshareException("経度（longitude）は -180〜180 で指定してください。");
        CheckLimit(limit);

        var ports = await GetPortsAsync(OperatorExtensions.ParseOptional(@operator), ct);
        var nearest = ports
            .Select(p => (Port: p, Distance: GeoMath.DistanceMeters(latitude, longitude, p.Lat, p.Lon)))
            .OrderBy(x => x.Distance)
            .Take(limit)
            .Select(x => PortView.From(x.Port, distanceM: (int)Math.Round(x.Distance)))
            .ToList();
        return new PortListResponse(nearest, nearest.Count == 0 ? "ポートがありません。" : null);
    }

    /// <summary>HELLO CYCLING 専用（ドコモ・バイクシェアのデータには住所がない）。</summary>
    public async Task<PortListResponse> SearchByAddressAsync(string address, CancellationToken ct)
    {
        var query = TextNormalizer.NormalizeAddress(address ?? "");
        if (query.Length == 0) throw new BikeshareException("address を指定してください。");

        var ports = await feeds.GetPortsAsync(Operator.HelloCycling, ct);
        var matches = ports.Where(p => p.SearchAddress?.Contains(query, StringComparison.Ordinal) == true).ToList();
        if (matches.Count > MaxResults)
            throw new BikeshareException($"住所「{address}」に一致するポートが {matches.Count} 件あります（上限 {MaxResults} 件）。番地など、もっと詳しい住所で絞り込んでください。");

        var views = matches
            .OrderBy(p => p.Address, StringComparer.Ordinal)
            .Select(p => PortView.From(p))
            .ToList();
        return new PortListResponse(views, views.Count == 0
            ? "一致するポートがありません。都道府県名や番地を省くなど、短い住所で試してください（漢数字と算用数字は区別されます）。"
            : null);
    }

    public async Task<PortListResponse> SearchByNameAsync(string query, string? @operator, int limit, CancellationToken ct)
    {
        var q = TextNormalizer.Normalize(query ?? "");
        if (q.Length == 0) throw new BikeshareException("query を指定してください。");
        CheckLimit(limit);

        var maxDistance = FuzzyMatcher.MaxDistance(q.Length);
        var ports = await GetPortsAsync(OperatorExtensions.ParseOptional(@operator), ct);
        var matches = ports
            .Select(p => (Port: p, Distance: FuzzyMatcher.SubstringEditDistance(q, p.SearchName)))
            .Where(x => x.Distance <= maxDistance)
            .OrderBy(x => x.Distance)
            .ThenBy(x => x.Port.SearchName.Length)
            .ThenBy(x => x.Port.Id.ToString(), StringComparer.Ordinal)
            .Take(limit)
            .Select(x => PortView.From(x.Port, editDistance: x.Distance))
            .ToList();
        return new PortListResponse(matches, matches.Count == 0
            ? "一致するポートがありません。名前の一部だけ（例: 駅名）で試すか、search_ports_nearby で座標から探してください。"
            : null);
    }

    /// <summary>ポート ID からリアルタイム状態を引く。ID ごとのエラーはその要素の Error に入れ、他の要素は返す。</summary>
    public async Task<PortStatusResponse> GetPortStatusAsync(IReadOnlyList<string> portIds, CancellationToken ct)
    {
        if (portIds is null || portIds.Count is < 1 or > MaxResults)
            throw new BikeshareException($"port_ids は 1〜{MaxResults} 件で指定してください。");

        var parsed = portIds.Select(raw => (Raw: raw, Ok: PortId.TryParse(raw, out var id), Id: id)).ToList();
        var operators = parsed.Where(x => x.Ok).Select(x => x.Id.Operator).Distinct().ToList();

        var data = new Dictionary<Operator, OperatorStatusData>();
        foreach (var (op, d) in await Task.WhenAll(operators.Select(async op => (op, await LoadStatusDataAsync(op, ct)))))
            data[op] = d;

        var views = parsed.Select(x =>
        {
            if (!x.Ok) return new PortStatusView(x.Raw, Error: PortId.InvalidMessage(x.Raw));
            var d = data[x.Id.Operator];
            d.Names.TryGetValue(x.Id.StationId, out var name);
            if (!d.Statuses.TryGetValue(x.Id.StationId, out var s))
                return name is null
                    ? new PortStatusView(x.Id.ToString(), Error: "ポートが見つかりません。")
                    : new PortStatusView(x.Id.ToString(), name, Error: "このポートの状態が配信されていません。");

            var isHello = x.Id.Operator == Operator.HelloCycling;
            return new PortStatusView(
                x.Id.ToString(),
                name,
                s.NumBikesAvailable,
                s.NumDocksAvailable,
                s.IsInstalled,
                s.IsRenting,
                s.IsReturning,
                s.LastReported is { } t ? FormatJst(t) : null,
                isHello
                    ? s.VehicleTypesAvailable.Select(v =>
                    {
                        d.VehicleTypes.TryGetValue(v.VehicleTypeId, out var vt);
                        return new VehicleAvailabilityView(v.VehicleTypeId, vt?.Name, vt?.PropulsionType, v.Count);
                    }).ToList()
                    : null,
                isHello
                    ? s.VehicleDocksAvailable.Select(dock => new DockAvailabilityView(
                        dock.VehicleTypeIds.Select(id => d.VehicleTypes.TryGetValue(id, out var vt) && vt.Name is not null ? vt.Name : id).ToList(),
                        dock.Count)).ToList()
                    : null);
        }).ToList();
        return new PortStatusResponse(views);
    }

    public async Task<OperatorInfoResponse> GetOperatorInfoAsync(string? @operator, CancellationToken ct)
    {
        var ops = OperatorExtensions.ParseOptional(@operator) is { } single ? [single] : OperatorExtensions.All;
        var views = await Task.WhenAll(ops.Select(async op =>
        {
            var infoTask = feeds.GetSystemInformationAsync(op, ct);
            var portsTask = feeds.GetPortsAsync(op, ct);
            var typesTask = op == Operator.HelloCycling ? feeds.GetVehicleTypesAsync(op, ct) : null;
            var info = await infoTask;
            var ports = await portsTask;
            var types = typesTask is null ? null : await typesTask;
            return new OperatorInfoView(
                op.Key(),
                info.Name,
                info.ShortName,
                info.Operator,
                info.Url,
                info.TermsUrl,
                info.PrivacyUrl,
                info.IosStoreUri,
                info.AndroidStoreUri,
                ports.Count,
                types?.Select(t => new VehicleTypeView(t.VehicleTypeId, t.Name, t.FormFactor, t.PropulsionType, t.MaxRangeMeters)).ToList(),
                op.Credit(),
                op.License(),
                info.LicenseUrl);
        }));
        return new OperatorInfoResponse(views);
    }

    private sealed record OperatorStatusData(
        IReadOnlyDictionary<string, string> Names,
        IReadOnlyDictionary<string, StationStatus> Statuses,
        IReadOnlyDictionary<string, VehicleType> VehicleTypes);

    private async Task<OperatorStatusData> LoadStatusDataAsync(Operator op, CancellationToken ct)
    {
        var portsTask = feeds.GetPortsAsync(op, ct);
        var statusesTask = feeds.GetStatusesAsync(op, ct);
        var typesTask = op == Operator.HelloCycling ? feeds.GetVehicleTypesAsync(op, ct) : null;
        var ports = await portsTask;
        var statuses = await statusesTask;
        var types = typesTask is null ? [] : await typesTask;
        // 上流のフィードに ID の重複があっても落ちないよう、先に出たものを使う
        var names = new Dictionary<string, string>();
        foreach (var p in ports) names.TryAdd(p.Id.StationId, p.Name);
        var typesById = new Dictionary<string, VehicleType>();
        foreach (var t in types) typesById.TryAdd(t.VehicleTypeId, t);
        return new OperatorStatusData(names, statuses, typesById);
    }

    private static readonly TimeSpan Jst = TimeSpan.FromHours(9);

    // タイムゾーン DB に頼らず固定の +09:00 で表示する（日本に夏時間はない）。日本時間か UTC か迷わないよう時差を付ける
    private static string FormatJst(long unixSeconds) =>
        DateTimeOffset.FromUnixTimeSeconds(unixSeconds).ToOffset(Jst).ToString("yyyy-MM-dd'T'HH:mm:sszzz", System.Globalization.CultureInfo.InvariantCulture);

    private async Task<IReadOnlyList<Port>> GetPortsAsync(Operator? op, CancellationToken ct)
    {
        if (op is { } single) return await feeds.GetPortsAsync(single, ct);
        var all = await Task.WhenAll(OperatorExtensions.All.Select(o => feeds.GetPortsAsync(o, ct)));
        return all.SelectMany(x => x).ToList();
    }

    private static void CheckLimit(int limit)
    {
        if (limit is < 1 or > MaxResults)
            throw new BikeshareException($"limit は 1〜{MaxResults} で指定してください。");
    }
}
