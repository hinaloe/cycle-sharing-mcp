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
        var query = TextNormalizer.Normalize(address ?? "");
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
