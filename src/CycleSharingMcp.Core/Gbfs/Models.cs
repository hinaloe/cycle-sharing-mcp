namespace CycleSharingMcp.Core.Gbfs;

/// <summary>station_information の 1 ポート。ドコモにしかない項目・HELLO CYCLING にしかない項目は、もう一方では null。</summary>
public sealed record Port(
    PortId Id,
    string Name,
    double Lat,
    double Lon,
    int? Capacity,
    string? RegionId,
    string? Address,
    bool? IsChargingStation,
    string? ParkingType,
    string? RentalUrl)
{
    /// <summary><see cref="TextNormalizer.Normalize"/> 済みの名前（検索のたびに正規化しないよう解析時に作る）。</summary>
    public string SearchName { get; init; } = "";

    /// <summary><see cref="TextNormalizer.NormalizeAddress"/> 済みの住所。</summary>
    public string? SearchAddress { get; init; }
}

public sealed record StationStatus(
    string StationId,
    int? NumBikesAvailable,
    int? NumDocksAvailable,
    bool? IsInstalled,
    bool? IsRenting,
    bool? IsReturning,
    long? LastReported,
    IReadOnlyList<VehicleTypeCount> VehicleTypesAvailable,
    IReadOnlyList<VehicleDockCount> VehicleDocksAvailable);

public sealed record VehicleTypeCount(string VehicleTypeId, int Count);

/// <summary>GBFS では返却可能数が複数の車種 ID をまとめた単位で返る。</summary>
public sealed record VehicleDockCount(IReadOnlyList<string> VehicleTypeIds, int Count);

public sealed record SystemInformation(
    string? Name,
    string? ShortName,
    string? Operator,
    string? Url,
    string? TermsUrl,
    string? PrivacyUrl,
    string? LicenseUrl,
    string? IosStoreUri,
    string? AndroidStoreUri);

public sealed record VehicleType(
    string VehicleTypeId,
    string? Name,
    string? FormFactor,
    string? PropulsionType,
    int? MaxRangeMeters);
