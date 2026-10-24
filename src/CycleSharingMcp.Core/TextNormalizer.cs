using System.Text;

namespace CycleSharingMcp.Core;

/// <summary>住所検索・名前検索で比べる前に両方へかける正規化。</summary>
public static class TextNormalizer
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
}
