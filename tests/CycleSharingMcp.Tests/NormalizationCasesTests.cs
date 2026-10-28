using System.Text.Json;

namespace CycleSharingMcp.Tests;

/// <summary>正規化の表（Fixtures/normalization_cases.json）。Workers 版の確認（CI のスモークテスト）でも同じ表を使う。</summary>
public class NormalizationCasesTests
{
    public static TheoryData<string, string, string> Cases()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var c in JsonDocument.Parse(Fixtures.Read("normalization_cases.json")).RootElement.EnumerateArray())
            data.Add(c.GetProperty("kind").GetString()!, c.GetProperty("input").GetString()!, c.GetProperty("expected").GetString()!);
        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Normalize_MatchesTable(string kind, string input, string expected)
        => Assert.Equal(expected, kind == "address" ? TextNormalizer.NormalizeAddress(input) : TextNormalizer.Normalize(input));
}
