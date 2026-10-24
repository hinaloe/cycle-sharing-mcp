using System.Globalization;
using System.Text.Json;

namespace CycleSharingMcp.Core.Gbfs;

/// <summary>GBFS 2.3 のフィードを解析する。欠けた項目は null（配列は空）にし、解析全体は落とさない。</summary>
public static class GbfsParser
{
    public static IReadOnlyList<Port> ParseStationInformation(ReadOnlySpan<byte> utf8Json, Operator op)
    {
        using var doc = Parse(utf8Json);
        var ports = new List<Port>();
        foreach (var s in Objects(Data(doc), "stations"))
        {
            var id = String(s, "station_id");
            // 座標のないポートは (0, 0) として近傍検索に紛れ込むので扱わない
            if (id is null || Double(s, "lat") is not { } lat || Double(s, "lon") is not { } lon) continue;
            var name = String(s, "name") ?? "";
            var address = String(s, "address");
            ports.Add(new Port(
                new PortId(op, id),
                name,
                lat,
                lon,
                // ドコモは GBFS 標準の capacity、HELLO CYCLING は標準外の vehicle_capacity（文字列）
                Int(s, "capacity") ?? IntFromString(s, "vehicle_capacity"),
                String(s, "region_id"),
                address,
                Bool(s, "is_charging_station"),
                String(s, "parking_type"),
                Object(s, "rental_uris") is { } uris ? String(uris, "web") : null)
            {
                SearchName = TextNormalizer.Normalize(name),
                SearchAddress = address is null ? null : TextNormalizer.NormalizeAddress(address),
            });
        }
        return ports;
    }

    public static IReadOnlyDictionary<string, StationStatus> ParseStationStatus(ReadOnlySpan<byte> utf8Json)
    {
        using var doc = Parse(utf8Json);
        var result = new Dictionary<string, StationStatus>();
        foreach (var s in Objects(Data(doc), "stations"))
        {
            var id = String(s, "station_id");
            if (id is null) continue;
            result[id] = new StationStatus(
                id,
                Int(s, "num_bikes_available"),
                Int(s, "num_docks_available"),
                Bool(s, "is_installed"),
                Bool(s, "is_renting"),
                Bool(s, "is_returning"),
                Long(s, "last_reported"),
                Objects(s, "vehicle_types_available")
                    .Select(v => (Id: String(v, "vehicle_type_id"), Count: Int(v, "count")))
                    .Where(v => v.Id is not null && v.Count is not null)
                    .Select(v => new VehicleTypeCount(v.Id!, v.Count!.Value))
                    .ToList(),
                Objects(s, "vehicle_docks_available")
                    .Where(d => Int(d, "count") is not null)
                    .Select(d => new VehicleDockCount(
                        Array(d, "vehicle_type_ids").Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList(),
                        Int(d, "count")!.Value))
                    .ToList());
        }
        return result;
    }

    public static SystemInformation ParseSystemInformation(ReadOnlySpan<byte> utf8Json)
    {
        using var doc = Parse(utf8Json);
        var d = Data(doc);
        var apps = Object(d, "rental_apps");
        return new SystemInformation(
            String(d, "name"),
            String(d, "short_name"),
            String(d, "operator"),
            String(d, "url"),
            String(d, "terms_url"),
            String(d, "privacy_url"),
            String(d, "license_url"),
            apps is { } a1 && Object(a1, "ios") is { } ios ? String(ios, "store_uri") : null,
            apps is { } a2 && Object(a2, "android") is { } android ? String(android, "store_uri") : null);
    }

    public static IReadOnlyList<VehicleType> ParseVehicleTypes(ReadOnlySpan<byte> utf8Json)
    {
        using var doc = Parse(utf8Json);
        return Objects(Data(doc), "vehicle_types")
            .Where(v => String(v, "vehicle_type_id") is not null)
            .Select(v => new VehicleType(
                String(v, "vehicle_type_id")!,
                String(v, "name"),
                String(v, "form_factor"),
                String(v, "propulsion_type"),
                Int(v, "max_range_meters")))
            .ToList();
    }

    private static JsonDocument Parse(ReadOnlySpan<byte> utf8Json)
    {
        var reader = new Utf8JsonReader(utf8Json);
        return JsonDocument.ParseValue(ref reader);
    }

    private static JsonElement Data(JsonDocument doc) =>
        Object(doc.RootElement, "data") ?? throw new JsonException("GBFS の data がありません。");

    private static JsonElement? Object(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Object ? v : null;

    private static IEnumerable<JsonElement> Array(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray()
            : [];

    // オブジェクトでない要素に TryGetProperty すると InvalidOperationException になるので、オブジェクトだけを返す
    private static IEnumerable<JsonElement> Objects(JsonElement e, string name) =>
        Array(e, name).Where(x => x.ValueKind == JsonValueKind.Object);

    private static string? String(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static double? Double(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

    private static int? Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;

    private static long? Long(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var l) ? l : null;

    private static int? IntFromString(JsonElement e, string name) =>
        int.TryParse(String(e, name), NumberStyles.None, CultureInfo.InvariantCulture, out var i) ? i : null;

    private static bool? Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;
}
