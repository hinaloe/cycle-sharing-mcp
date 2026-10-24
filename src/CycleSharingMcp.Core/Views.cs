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
    int? EditDistance = null,
    int? NumBikesAvailable = null,
    int? NumDocksAvailable = null)
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

/// <summary>1 ポートのリアルタイム状態。ID が不正・存在しないときは <see cref="Error"/> だけが入る。</summary>
public sealed record PortStatusView(
    string PortId,
    string? Name = null,
    int? NumBikesAvailable = null,
    int? NumDocksAvailable = null,
    bool? IsInstalled = null,
    bool? IsRenting = null,
    bool? IsReturning = null,
    string? LastReported = null,
    IReadOnlyList<VehicleAvailabilityView>? VehicleTypesAvailable = null,
    IReadOnlyList<DockAvailabilityView>? VehicleDocksAvailable = null,
    string? Error = null);

public sealed record VehicleAvailabilityView(string VehicleTypeId, string? Name, string? PropulsionType, int Count);

/// <summary>返却可能数。GBFS では複数の車種をまとめた単位で返るので、その単位のまま返す。</summary>
public sealed record DockAvailabilityView(IReadOnlyList<string> VehicleTypes, int Count);

public sealed record PortStatusResponse(IReadOnlyList<PortStatusView> Ports);

public sealed record VehicleTypeView(string VehicleTypeId, string? Name, string? FormFactor, string? PropulsionType, int? MaxRangeMeters);

public sealed record OperatorInfoView(
    string Operator,
    string? Name,
    string? ShortName,
    string? OperatorName,
    string? Url,
    string? TermsUrl,
    string? PrivacyUrl,
    string? IosApp,
    string? AndroidApp,
    int PortCount,
    IReadOnlyList<VehicleTypeView>? VehicleTypes,
    string Credit,
    string License,
    string? LicenseUrl);

public sealed record OperatorInfoResponse(IReadOnlyList<OperatorInfoView> Operators);
