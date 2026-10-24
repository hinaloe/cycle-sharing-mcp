namespace CycleSharingMcp.Core;

/// <summary>ODPT の GBFS フィードの URL。アクセストークンがあればトークン版、なければ公開版。</summary>
public static class OdptEndpoints
{
    public static Uri FeedUri(Operator op, string feed, string? accessToken) => accessToken is null
        ? new Uri($"https://api-public.odpt.org/api/v4/gbfs/{op.SystemId()}/{feed}.json")
        : new Uri($"https://api.odpt.org/api/v4/gbfs/{op.SystemId()}/{feed}.json?acl:consumerKey={Uri.EscapeDataString(accessToken)}");
}
