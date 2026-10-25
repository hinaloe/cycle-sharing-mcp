namespace CycleSharingMcp.Tests;

public class BikeshareJsonTests
{
    [Fact]
    public void Serialize_SnakeCase_OmitsNull_KeepsJapanese()
    {
        var view = new PortView("docomo:1", "docomo", "新木場", 35.6, 139.8, null, null, null, null, null, null, DistanceM: 10);

        Assert.Equal("""{"ports":[{"port_id":"docomo:1","operator":"docomo","name":"新木場","lat":35.6,"lon":139.8,"distance_m":10}]}""",
                     BikeshareJson.Serialize(new PortListResponse([view], null)));
    }

    [Fact]
    public void ServerOptions_SerializeResponsesTheSameWay()
    {
        var response = new PortStatusResponse([new PortStatusView("docomo:1", Name: "新木場", NumBikesAvailable: 3)]);

        Assert.Equal(BikeshareJson.Serialize(response),
                     System.Text.Json.JsonSerializer.Serialize(response, BikeshareJson.CreateServerOptions()));
    }
}
