namespace CycleSharingMcp.Core;

/// <summary>事業者と GBFS の station_id の組。表記は <c>docomo:5053</c> のように事業者名を前に付ける（両社の station_id は数字だけで衝突しうるため）。</summary>
public readonly record struct PortId(Operator Operator, string StationId)
{
    public static bool TryParse(string? value, out PortId id)
    {
        id = default;
        var parts = value?.Trim().Split(':', 2);
        if (parts is not [var op, var station] || station.Length == 0) return false;
        if (!OperatorExtensions.TryParse(op, out var parsed)) return false;
        id = new PortId(parsed, station);
        return true;
    }

    public static PortId Parse(string value) => TryParse(value, out var id)
        ? id
        : throw new BikeshareException(InvalidMessage(value));

    internal static string InvalidMessage(string? value) =>
        $"ポート ID「{value}」の形式が正しくありません。検索結果の port_id（例: docomo:5053, hellocycling:17）を指定してください。";

    public override string ToString() => $"{Operator.Key()}:{StationId}";
}
