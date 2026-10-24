namespace CycleSharingMcp.Core;

/// <summary>名前のあいまい検索。どちらの引数も <see cref="TextNormalizer.Normalize"/> 済みであること。</summary>
public static class FuzzyMatcher
{
    /// <summary>
    /// <paramref name="text"/> のどこかの部分文字列と <paramref name="pattern"/> との最小編集距離（Sellers のアルゴリズム）。
    /// 名前全体と比べると「新木場」と「H1-57.新木場駅」の長さの差がそのまま距離になるので、部分文字列で測る。
    /// </summary>
    public static int SubstringEditDistance(string pattern, string text)
    {
        // prev[i]: pattern[..i] と、text の直前の文字で終わる部分文字列との最小距離
        var prev = new int[pattern.Length + 1];
        var cur = new int[pattern.Length + 1];
        for (var i = 0; i <= pattern.Length; i++) prev[i] = i;

        var best = pattern.Length;
        foreach (var t in text)
        {
            cur[0] = 0; // 部分文字列はどこからでも始められる
            for (var i = 1; i <= pattern.Length; i++)
            {
                cur[i] = Math.Min(
                    prev[i - 1] + (pattern[i - 1] == t ? 0 : 1),
                    Math.Min(prev[i] + 1, cur[i - 1] + 1));
            }
            best = Math.Min(best, cur[pattern.Length]);
            (prev, cur) = (cur, prev);
        }
        return best;
    }

    /// <summary>検索語の長さに対して許す距離。2 文字以下は完全一致のみ（1 文字違いが大量に当たるため）。</summary>
    public static int MaxDistance(int patternLength) => patternLength <= 2 ? 0 : Math.Max(1, patternLength / 4);
}
