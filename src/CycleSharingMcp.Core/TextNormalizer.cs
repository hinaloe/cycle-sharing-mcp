using System.Text;
using System.Text.RegularExpressions;

namespace CycleSharingMcp.Core;

/// <summary>住所検索・名前検索で比べる前に両方へかける正規化。</summary>
public static partial class TextNormalizer
{
    /// <summary>
    /// NFKC。既定は <see cref="string.Normalize(NormalizationForm)"/>。WebAssembly では .NET の NFKC が使えないので、
    /// 実行環境が起動時に 1 度だけ差し替える（Workers 版はホストの JavaScript の normalize を使う）。
    /// </summary>
    public static Func<string, string> Compatibility { get; set; } = static s => s.Normalize(NormalizationForm.FormKC);

    /// <summary>NFKC（全角英数・半角カナを揃える）→ 空白を除く → カタカナをひらがなに → 小文字に。</summary>
    public static string Normalize(string value)
    {
        var nfkc = Compatibility(value);
        var sb = new StringBuilder(nfkc.Length);
        foreach (var c in nfkc)
        {
            if (char.IsWhiteSpace(c)) continue;
            sb.Append(c is >= '\u30A1' and <= '\u30F6' ? (char)(c - 0x60) : char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    /// <summary>
    /// 住所用。<see cref="Normalize"/> に加えて、番地の書き方の揺れを揃える。
    /// 「一丁目」→「1丁目」、「丁目」→「-」（「台東4丁目」と「台東4-30-6」が互いに当たるように）、
    /// ハイフンに似た記号（− ‐ — ― など）→「-」、ひらがな・カタカナの後ろ以外の長音記号「ー」→「-」。
    /// </summary>
    public static string NormalizeAddress(string value)
    {
        var s = Normalize(value.Replace("\u00AD", "", StringComparison.Ordinal)); // ソフトハイフン
        s = KanjiChome().Replace(s, m => KanjiNumber(m.Groups[1].Value) is { } n ? $"{n}丁目" : m.Value);
        s = s.Replace("丁目", "-", StringComparison.Ordinal);
        s = Dashes().Replace(s, "-");
        s = LongVowelAsHyphen().Replace(s, "-");
        // 「4丁目ー26」→「4-ー26」→「4--26」のように重なるので、連続した「-」を 1 つにする
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

    // ハイフン・ダッシュ類（Unicode の Dash_Punctuation）と、Pd に入っていないマイナス記号・ハイフン箇条書き記号。
    // 仮名の語には使われないので前後を問わず置き換える。波ダッシュと波線ダッシュは Pd だが、
    // 「1〜3番」のように範囲を表すので除く
    [GeneratedRegex(@"[\p{Pd}\u2212\u2043-[\u301c\u3030]]")]
    private static partial Regex Dashes();

    // 長音記号はカタカナ語（「センター」）にも使われるので、ひらがな・カタカナのブロック（長音記号自身を含む）の文字の後ろにないものだけ置き換える。
    // 「4ーー2」のように続いたものはまとめて 1 つの「-」にし、「ドーーム」の 2 つ目は前が長音記号なので置き換えない
    [GeneratedRegex(@"(?<![\p{IsHiragana}\p{IsKatakana}])\u30FC+")]
    private static partial Regex LongVowelAsHyphen();

    [GeneratedRegex("-{2,}")]
    private static partial Regex MultipleHyphens();
}
