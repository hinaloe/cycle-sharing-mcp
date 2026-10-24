using System.Text;
using System.Text.RegularExpressions;

namespace CycleSharingMcp.Core;

/// <summary>住所検索・名前検索で比べる前に両方へかける正規化。</summary>
public static partial class TextNormalizer
{
    /// <summary>NFKC（全角英数・半角カナを揃える）→ 空白を除く → カタカナをひらがなに → 小文字に。</summary>
    public static string Normalize(string value)
    {
        var nfkc = value.Normalize(NormalizationForm.FormKC);
        var sb = new StringBuilder(nfkc.Length);
        foreach (var c in nfkc)
        {
            if (char.IsWhiteSpace(c)) continue;
            sb.Append(c is >= 'ァ' and <= 'ヶ' ? (char)(c - 0x60) : char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    /// <summary>
    /// 住所用。<see cref="Normalize"/> に加えて、番地の書き方の揺れを揃える。
    /// 「一丁目」→「1丁目」、「丁目」→「-」（「台東4丁目」と「台東4-30-6」が互いに当たるように）、
    /// 数字の後のハイフンに似た記号（ー − ‐ — など）→「-」。
    /// </summary>
    public static string NormalizeAddress(string value)
    {
        var s = Normalize(value.Replace("­", "", StringComparison.Ordinal)); // ソフトハイフン
        s = KanjiChome().Replace(s, m => KanjiNumber(m.Groups[1].Value) is { } n ? $"{n}丁目" : m.Value);
        s = s.Replace("丁目", "-", StringComparison.Ordinal);
        // 「4-ー26」のように丁目の後に記号が重なることもあるので、置き換えたあと連続した「-」を 1 つにする
        s = HyphenAfterDigit().Replace(s, "-");
        return MultipleHyphens().Replace(s, "-");
    }

    /// <summary>一〜九十九の漢数字。それ以外（百以上、「一一」のような並び）は null。</summary>
    private static int? KanjiNumber(string kanji)
    {
        const string Digits = "一二三四五六七八九";
        int Digit(string d) => d.Length == 0 ? 0 : d.Length == 1 && Digits.Contains(d[0]) ? Digits.IndexOf(d[0]) + 1 : -1;

        var parts = kanji.Split('十');
        int tens, ones;
        switch (parts.Length)
        {
            case 1: tens = 0; ones = Digit(parts[0]); break;
            case 2: tens = parts[0].Length == 0 ? 1 : Digit(parts[0]); ones = Digit(parts[1]); break;
            default: return null;
        }
        if (tens < 0 || ones < 0) return null;
        var n = tens * 10 + ones;
        return n > 0 ? n : null;
    }

    [GeneratedRegex("([一二三四五六七八九十]+)丁目")]
    private static partial Regex KanjiChome();

    // ー（長音記号）はカタカナ語にも使われるので、数字か「-」の直後で、次が数字のときだけ置き換える
    [GeneratedRegex(@"(?<=[0-9\-])[‐-―−ー](?=[0-9])")]
    private static partial Regex HyphenAfterDigit();

    [GeneratedRegex("-{2,}")]
    private static partial Regex MultipleHyphens();
}
