namespace CycleSharingMcp.Core;

public enum Operator
{
    Docomo,
    HelloCycling,
}

public static class OperatorExtensions
{
    public static IReadOnlyList<Operator> All { get; } = [Operator.Docomo, Operator.HelloCycling];

    /// <summary>ツールの引数とポート ID で使う名前。</summary>
    public static string Key(this Operator op) => op switch
    {
        Operator.Docomo => "docomo",
        Operator.HelloCycling => "hellocycling",
        _ => throw new ArgumentOutOfRangeException(nameof(op)),
    };

    /// <summary>ODPT の GBFS の URL に入る system 名。</summary>
    public static string SystemId(this Operator op) => op switch
    {
        Operator.Docomo => "docomo-cycle",
        Operator.HelloCycling => "hellocycling",
        _ => throw new ArgumentOutOfRangeException(nameof(op)),
    };

    public static string DisplayName(this Operator op) => op switch
    {
        Operator.Docomo => "ドコモ・バイクシェア",
        Operator.HelloCycling => "HELLO CYCLING",
        _ => throw new ArgumentOutOfRangeException(nameof(op)),
    };

    /// <summary>ODPT のデータのクレジット表示（提供者名）。</summary>
    public static string Credit(this Operator op) => op switch
    {
        Operator.Docomo => "株式会社ドコモ・バイクシェア / 公共交通オープンデータ協議会",
        Operator.HelloCycling => "OpenStreet株式会社 / 公共交通オープンデータ協議会",
        _ => throw new ArgumentOutOfRangeException(nameof(op)),
    };

    public static string License(this Operator op) => op switch
    {
        Operator.Docomo => "CC BY 4.0",
        Operator.HelloCycling => "CC BY 4.0 / ODC BY 1.0 / ODbL 1.0",
        _ => throw new ArgumentOutOfRangeException(nameof(op)),
    };

    public static bool TryParse(string? value, out Operator op)
    {
        foreach (var candidate in All)
        {
            if (string.Equals(value?.Trim(), candidate.Key(), StringComparison.OrdinalIgnoreCase))
            {
                op = candidate;
                return true;
            }
        }
        op = default;
        return false;
    }

    /// <summary>省略（null・空白）なら null（両方の事業者）を返す。</summary>
    public static Operator? ParseOptional(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return TryParse(value, out var op)
            ? op
            : throw new BikeshareException($"事業者「{value}」は指定できません。docomo か hellocycling を指定するか、省略してください。");
    }
}
