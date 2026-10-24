using CycleSharingMcp.Core.Gbfs;

namespace CycleSharingMcp.Core;

/// <summary>GBFS フィードの取得元。取得とキャッシュの方法は実行環境ごとに実装する。</summary>
public interface IFeedSource
{
    Task<IReadOnlyList<Port>> GetPortsAsync(Operator op, CancellationToken ct);

    /// <summary>station_id → 状態。</summary>
    Task<IReadOnlyDictionary<string, StationStatus>> GetStatusesAsync(Operator op, CancellationToken ct);

    Task<SystemInformation> GetSystemInformationAsync(Operator op, CancellationToken ct);

    /// <summary>車種のフィードがない事業者（ドコモ）は空。</summary>
    Task<IReadOnlyList<VehicleType>> GetVehicleTypesAsync(Operator op, CancellationToken ct);
}
