namespace CycleSharingMcp.Tests;

public class SearchTests
{
    private readonly FakeFeedSource fake = new();
    private readonly BikeshareService svc;

    public SearchTests()
    {
        fake.Ports[Operator.Docomo].AddRange([
            FakeFeedSource.MakePort(Operator.Docomo, "100", "H1-57.新木場駅", 35.6457, 139.8268),
            FakeFeedSource.MakePort(Operator.Docomo, "5053", "M05.塩見縄手", 35.478819, 133.04884),
        ]);
        fake.Ports[Operator.HelloCycling].AddRange([
            FakeFeedSource.MakePort(Operator.HelloCycling, "200", "新木場駅前", 35.6460, 139.8270, "東京都江東区新木場１丁目５－１"),
            FakeFeedSource.MakePort(Operator.HelloCycling, "17", "新御徒町ステーション", 35.707252, 139.777587, "東京都台東区台東４丁目３１－１"),
            FakeFeedSource.MakePort(Operator.HelloCycling, "300", "シブヤ公園ポート", 35.6640, 139.7010),
        ]);
        svc = new BikeshareService(fake);
    }

    [Fact]
    public async Task Nearby_SortsByDistanceAcrossOperators()
    {
        var r = await svc.SearchNearbyAsync(35.6457, 139.8268, null, 2, default);
        Assert.Equal(["docomo:100", "hellocycling:200"], r.Ports.Select(p => p.PortId));
        Assert.Equal(0, r.Ports[0].DistanceM);
        Assert.InRange(r.Ports[1].DistanceM!.Value, 30, 45);
    }

    [Fact]
    public async Task Nearby_OperatorFilter_DoesNotFetchOther()
    {
        var r = await svc.SearchNearbyAsync(35.6457, 139.8268, "hellocycling", 10, default);
        Assert.All(r.Ports, p => Assert.Equal("hellocycling", p.Operator));
        Assert.False(fake.Calls.ContainsKey((Operator.Docomo, "station_information")));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(41)]
    public async Task Nearby_LimitOutOfRange_Throws(int limit)
        => await Assert.ThrowsAsync<BikeshareException>(() => svc.SearchNearbyAsync(35.6, 139.7, null, limit, default));

    [Fact]
    public async Task Nearby_SwappedLatLon_Throws()
    {
        var ex = await Assert.ThrowsAsync<BikeshareException>(() => svc.SearchNearbyAsync(139.7, 35.6, null, 10, default));
        Assert.Contains("緯度", ex.Message);
        Assert.Contains("取り違え", ex.Message);
    }

    [Theory]
    [InlineData("台東４丁目")]
    [InlineData("台東4丁目")]
    public async Task Address_PartialMatch(string query)
    {
        var r = await svc.SearchByAddressAsync(query, default);
        Assert.Equal(["hellocycling:17"], r.Ports.Select(p => p.PortId));
        Assert.Equal("東京都台東区台東４丁目３１－１", r.Ports[0].Address);
    }

    [Theory]
    [InlineData("台東4丁目")]
    [InlineData("台東4-")]
    public async Task Address_ChomeAndHyphenWritingsMatchEachOther(string query)
    {
        fake.Ports[Operator.HelloCycling].Add(
            FakeFeedSource.MakePort(Operator.HelloCycling, "4760", "ファミリーマート台東四丁目店", 35.7071, 139.7776, "東京都台東区台東4-30-6"));

        var r = await svc.SearchByAddressAsync(query, default);

        Assert.Equal(["hellocycling:17", "hellocycling:4760"], r.Ports.Select(p => p.PortId).Order());
    }

    [Fact]
    public async Task Address_NoMatch_ReturnsEmptyWithNote()
    {
        var r = await svc.SearchByAddressAsync("たいとう", default);
        Assert.Empty(r.Ports);
        // 入力の誤りと決めつけず、HELLO CYCLING に無いだけの可能性と次の手段を示す
        Assert.Contains("HELLO CYCLING", r.Note);
        Assert.Contains("search_ports_nearby", r.Note);
    }

    [Fact]
    public async Task Address_DoesNotFetchDocomo()
    {
        await svc.SearchByAddressAsync("台東", default);
        Assert.False(fake.Calls.ContainsKey((Operator.Docomo, "station_information")));
    }

    [Fact]
    public async Task Address_Blank_Throws()
        => await Assert.ThrowsAsync<BikeshareException>(() => svc.SearchByAddressAsync("  ", default));

    [Fact]
    public async Task Address_Over40_Throws()
    {
        var ex = await Assert.ThrowsAsync<BikeshareException>(() => WithMarunouchi(41).SearchByAddressAsync("丸の内", default));
        Assert.Contains("41", ex.Message);
        // 次に検索すれば 40 件以内に収まる候補を件数付きで示す
        Assert.Contains("丸の内1 (11)", ex.Message);
    }

    [Fact]
    public async Task Address_Exactly40_Returns40()
        => Assert.Equal(40, (await WithMarunouchi(40).SearchByAddressAsync("丸の内", default)).Ports.Count);

    [Fact]
    public async Task Name_SortsByDistanceThenLength()
    {
        var r = await svc.SearchByNameAsync("新木場", null, 5, default);
        Assert.Equal(["hellocycling:200", "docomo:100"], r.Ports.Select(p => p.PortId));
        Assert.All(r.Ports, p => Assert.Equal(0, p.EditDistance));
    }

    [Fact]
    public async Task Name_KanaFold()
        => Assert.Equal("hellocycling:300", (await svc.SearchByNameAsync("しぶや", null, 5, default)).Ports.Single().PortId);

    [Fact]
    public async Task Name_ByDocomoNumber()
        => Assert.Equal("docomo:100", (await svc.SearchByNameAsync("H1-57", null, 5, default)).Ports.First().PortId);

    [Fact]
    public async Task Name_Typo()
        => Assert.Equal(1, (await svc.SearchByNameAsync("新木馬", "docomo", 5, default)).Ports.Single().EditDistance);

    [Fact]
    public async Task Name_NoMatch_ReturnsEmptyWithNote()
    {
        var r = await svc.SearchByNameAsync("存在しないポート名", null, 5, default);
        Assert.Empty(r.Ports);
        Assert.NotNull(r.Note);
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("　")]
    public async Task Name_EmptyAfterNormalize_Throws(string query)
        => await Assert.ThrowsAsync<BikeshareException>(() => svc.SearchByNameAsync(query, null, 5, default));

    [Theory]
    [InlineData(0)]
    [InlineData(41)]
    public async Task Name_LimitOutOfRange_Throws(int limit)
        => await Assert.ThrowsAsync<BikeshareException>(() => svc.SearchByNameAsync("新木場", null, limit, default));

    private static BikeshareService WithMarunouchi(int count)
    {
        var f = new FakeFeedSource();
        for (var i = 0; i < count; i++)
            f.Ports[Operator.HelloCycling].Add(FakeFeedSource.MakePort(Operator.HelloCycling, $"{i}", $"ポート{i}", 35.68, 139.76, $"東京都千代田区丸の内{i}"));
        return new BikeshareService(f);
    }
}
