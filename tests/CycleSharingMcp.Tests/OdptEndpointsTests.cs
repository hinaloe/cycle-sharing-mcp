namespace CycleSharingMcp.Tests;

public class OdptEndpointsTests
{
    [Fact]
    public void FeedUri_Public()
        => Assert.Equal("https://api-public.odpt.org/api/v4/gbfs/docomo-cycle/station_status.json",
                        OdptEndpoints.FeedUri(Operator.Docomo, "station_status", null).AbsoluteUri);

    [Fact]
    public void FeedUri_Token_Escaped()
        => Assert.Equal("https://api.odpt.org/api/v4/gbfs/hellocycling/station_information.json?acl:consumerKey=a%20b%26c",
                        OdptEndpoints.FeedUri(Operator.HelloCycling, "station_information", "a b&c").AbsoluteUri);
}
