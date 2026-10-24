namespace CycleSharingMcp.Core;

/// <summary>
/// 住所検索の一致が多すぎるとき、次に足すと絞り込める検索語の候補を作る。
/// 住所は解析しない（町名の区切りは地域ごとにまちまちなので）。一致した住所の「検索語の後に続く文字」でまとめ、
/// 上限を超えるまとまりは 1 文字先で分け直し、各まとまりはその全住所に共通する続きまで伸ばす。
/// </summary>
public static class AddressGrouper
{
    /// <param name="query">正規化済みの検索語。</param>
    /// <param name="addresses">正規化済みで、<paramref name="query"/> を含む住所。</param>
    /// <returns>候補（検索語 + 続き）と、それで当たる件数。件数の多い順。</returns>
    public static IReadOnlyList<(string Text, int Count)> Split(string query, IEnumerable<string> addresses, int limit)
    {
        var tails = addresses
            .Select(a => a.IndexOf(query, StringComparison.Ordinal) is var i and >= 0 ? a[(i + query.Length)..] : null)
            .OfType<string>()
            .ToList();

        var result = new List<(string Text, int Count)>();
        var pending = new Stack<(string Prefix, List<string> Tails)>();
        pending.Push(("", tails));
        while (pending.Count > 0)
        {
            var (prefix, group) = pending.Pop();
            var common = CommonPrefix(group);
            prefix += common;
            // 共通部分を除いた残りが全部空なら、同じ住所ばかりでこれ以上分けられない
            if (group.Count <= limit || group.All(t => t.Length == common.Length))
            {
                result.Add((query + prefix, group.Count));
                continue;
            }
            foreach (var sub in group.Select(t => t[common.Length..]).GroupBy(t => t.Length == 0 ? "" : t[..1]))
            {
                if (sub.Key.Length == 0) result.Add((query + prefix, sub.Count()));
                else pending.Push((prefix + sub.Key, sub.Select(t => t[1..]).ToList()));
            }
        }

        return result
            .OrderByDescending(r => r.Count)
            .ThenBy(r => r.Text, StringComparer.Ordinal)
            .ToList();
    }

    private static string CommonPrefix(List<string> values)
    {
        if (values.Count == 0) return "";
        var length = values[0].Length;
        foreach (var v in values)
        {
            length = Math.Min(length, v.Length);
            for (var i = 0; i < length; i++)
            {
                if (v[i] != values[0][i])
                {
                    length = i;
                    break;
                }
            }
        }
        return values[0][..length];
    }
}
