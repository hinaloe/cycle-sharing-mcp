using CycleSharingMcp.Core.Gbfs;

namespace CycleSharingMcp.Core;

// ツールの応答。匿名型ではなく record にしておく（将来 JSON ソース生成に載せるため）。

public sealed record PortView(
    string PortId,
    string Operator,
    string Name,
    double Lat,
    double Lon,
    int? Capacity,
    string? RegionId,
    string? Address,
    bool? IsChargingStation,
    string? ParkingType,
    string? RentalUrl,
    int? DistanceM = null,
    int? EditDistance = null)
{
    public static PortView From(Port p, int? distanceM = null, int? editDistance = null) => new(
        p.Id.ToString(),
        p.Id.Operator.Key(),
        p.Name,
        p.Lat,
        p.Lon,
        p.Capacity,
        p.RegionId,
        p.Address,
        p.IsChargingStation,
        p.ParkingType,
        p.RentalUrl,
        distanceM,
        editDistance);
}

public sealed record PortListResponse(IReadOnlyList<PortView> Ports, string? Note);
