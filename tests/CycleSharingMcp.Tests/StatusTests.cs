namespace CycleSharingMcp.Tests;

public class StatusTests
{
    private readonly FakeFeedSource fake = FakeFeedSource.FromFixtures();
    private readonly BikeshareService svc;

    public StatusTests() => svc = new BikeshareService(fake);

    [Fact]
    public async Task Status_MixedIds_KeepOrderAndPerItemErrors()
    {
        var r = await svc.GetPortStatusAsync(["docomo:5053", "hellocycling:17", "hellocycling:999999", "bad"], default);
        Assert.Equal(["docomo:5053", "hellocycling:17", "hellocycling:999999", "bad"], r.Ports.Select(p => p.PortId));

        var docomo = r.Ports[0];
        Assert.Equal(("M05.塩見縄手", 4, 2, "2026-10-24T19:54:36+09:00"),
                     (docomo.Name, docomo.NumBikesAvailable!.Value, docomo.NumDocksAvailable!.Value, docomo.LastReported));
        Assert.Equal((true, true, true), (docomo.IsInstalled, docomo.IsRenting, docomo.IsReturning));
        Assert.Null(docomo.VehicleTypesAvailable);
        Assert.Null(docomo.VehicleDocksAvailable);
        Assert.Null(docomo.Error);

        var hello = r.Ports[1];
        Assert.Equal(("新御徒町ステーション", "2026-10-24T19:50:02+09:00"), (hello.Name, hello.LastReported));
        Assert.Equal(new VehicleAvailabilityView("2", "hellocycling bicycle", "electric_assist", 2), Assert.Single(hello.VehicleTypesAvailable!));
        var dock = Assert.Single(hello.VehicleDocksAvailable!);
        Assert.Equal(["hellocycling bicycle"], dock.VehicleTypes);
        Assert.Equal(6, dock.Count);

        Assert.Equal("ポートが見つかりません。", r.Ports[2].Error);
        Assert.Null(r.Ports[2].NumBikesAvailable);
        Assert.Contains("docomo:5053", r.Ports[3].Error);
    }

    [Fact]
    public async Task Status_NullId_KeepsItsPlaceWithError()
    {
        var r = await svc.GetPortStatusAsync([null!, "docomo:5053"], default);

        // port_id が null だと JSON から項目ごと消えるので、空文字で残す
        Assert.Equal("", r.Ports[0].PortId);
        Assert.NotNull(r.Ports[0].Error);
        Assert.Equal("docomo:5053", r.Ports[1].PortId);
    }

    [Fact]
    public async Task Status_OnlyFetchesNeededOperators()
    {
        await svc.GetPortStatusAsync(["docomo:5053"], default);
        Assert.False(fake.Calls.ContainsKey((Operator.HelloCycling, "station_status")));
        Assert.False(fake.Calls.ContainsKey((Operator.HelloCycling, "station_information")));
    }

    [Fact]
    public async Task Status_DuplicateIdsInFeed_DoNotBreakOtherPorts()
    {
        // 上流のフィードに同じ station_id・vehicle_type_id が重複して載っても落ちない
        fake.Ports[Operator.HelloCycling].Add(fake.Ports[Operator.HelloCycling].Single(p => p.Id.StationId == "17"));
        fake.VehicleTypes[Operator.HelloCycling].Add(fake.VehicleTypes[Operator.HelloCycling][0]);

        var r = await svc.GetPortStatusAsync(["hellocycling:17"], default);

        Assert.Equal(2, r.Ports.Single().NumBikesAvailable);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(41)]
    public async Task Status_CountOutOfRange_Throws(int n)
        => await Assert.ThrowsAsync<BikeshareException>(() => svc.GetPortStatusAsync(Enumerable.Repeat("docomo:5053", n).ToList(), default));

    [Fact]
    public async Task Status_Exactly40_Returns40()
        => Assert.Equal(40, (await svc.GetPortStatusAsync(Enumerable.Repeat("docomo:5053", 40).ToList(), default)).Ports.Count);
}
