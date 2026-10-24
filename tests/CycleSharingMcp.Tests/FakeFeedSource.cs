using CycleSharingMcp.Core.Gbfs;

namespace CycleSharingMcp.Tests;

internal sealed class FakeFeedSource : IFeedSource
{
    public Dictionary<Operator, List<Port>> Ports { get; } = new() { [Operator.Docomo] = [], [Operator.HelloCycling] = [] };
    public Dictionary<Operator, Dictionary<string, StationStatus>> Statuses { get; } = new() { [Operator.Docomo] = [], [Operator.HelloCycling] = [] };
    public Dictionary<Operator, SystemInformation> SystemInformations { get; } = new();
    public Dictionary<Operator, List<VehicleType>> VehicleTypes { get; } = new() { [Operator.Docomo] = [], [Operator.HelloCycling] = [] };
    public Dictionary<(Operator, string Feed), int> Calls { get; } = new();

    /// <summary>Task 3 のフィクスチャを読み込んだもの。</summary>
    public static FakeFeedSource FromFixtures()
    {
        var fake = new FakeFeedSource();
        foreach (var (op, prefix) in new[] { (Operator.Docomo, "docomo"), (Operator.HelloCycling, "hellocycling") })
        {
            fake.Ports[op].AddRange(GbfsParser.ParseStationInformation(Fixtures.Read($"{prefix}_station_information.json"), op));
            foreach (var (id, s) in GbfsParser.ParseStationStatus(Fixtures.Read($"{prefix}_station_status.json"))) fake.Statuses[op][id] = s;
            fake.SystemInformations[op] = GbfsParser.ParseSystemInformation(Fixtures.Read($"{prefix}_system_information.json"));
        }
        fake.VehicleTypes[Operator.HelloCycling].AddRange(GbfsParser.ParseVehicleTypes(Fixtures.Read("hellocycling_vehicle_types.json")));
        return fake;
    }

    public static Port MakePort(Operator op, string id, string name, double lat, double lon, string? address = null) =>
        new(new PortId(op, id), name, lat, lon, null, null, address, null, null, null)
        {
            SearchName = TextNormalizer.Normalize(name),
            SearchAddress = address is null ? null : TextNormalizer.NormalizeAddress(address),
        };

    public Task<IReadOnlyList<Port>> GetPortsAsync(Operator op, CancellationToken ct)
    {
        Count(op, "station_information");
        return Task.FromResult<IReadOnlyList<Port>>(Ports[op]);
    }

    public Task<IReadOnlyDictionary<string, StationStatus>> GetStatusesAsync(Operator op, CancellationToken ct)
    {
        Count(op, "station_status");
        return Task.FromResult<IReadOnlyDictionary<string, StationStatus>>(Statuses[op]);
    }

    public Task<SystemInformation> GetSystemInformationAsync(Operator op, CancellationToken ct)
    {
        Count(op, "system_information");
        return Task.FromResult(SystemInformations[op]);
    }

    public Task<IReadOnlyList<VehicleType>> GetVehicleTypesAsync(Operator op, CancellationToken ct)
    {
        Count(op, "vehicle_types");
        return Task.FromResult<IReadOnlyList<VehicleType>>(VehicleTypes[op]);
    }

    private void Count(Operator op, string feed) => Calls[(op, feed)] = Calls.GetValueOrDefault((op, feed)) + 1;
}
