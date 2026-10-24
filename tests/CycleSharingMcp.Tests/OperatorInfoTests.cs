namespace CycleSharingMcp.Tests;

public class OperatorInfoTests
{
    private readonly BikeshareService svc = new(FakeFeedSource.FromFixtures());

    [Fact]
    public async Task OperatorInfo_Hello()
    {
        var v = (await svc.GetOperatorInfoAsync("hellocycling", default)).Operators.Single();
        Assert.Equal("hellocycling", v.Operator);
        Assert.Equal("OpenStreet株式会社 / 公共交通オープンデータ協議会", v.Credit);
        Assert.Equal("CC BY 4.0 / ODC BY 1.0 / ODbL 1.0", v.License);
        Assert.Equal("https://d1yl7kw204zjxn.cloudfront.net/gbfs/v2/public/hellocycling_gbfs_licence.txt", v.LicenseUrl);
        Assert.Equal(("HELLO CYCLING", "OpenStreet Corp."), (v.ShortName, v.OperatorName));
        Assert.Equal(3, v.PortCount);
        Assert.Equal(new VehicleTypeView("2", "hellocycling bicycle", "bicycle", "electric_assist", 70000), Assert.Single(v.VehicleTypes!));
    }

    [Fact]
    public async Task OperatorInfo_Docomo()
    {
        var v = (await svc.GetOperatorInfoAsync("docomo", default)).Operators.Single();
        Assert.Equal("株式会社ドコモ・バイクシェア / 公共交通オープンデータ協議会", v.Credit);
        Assert.Equal("CC BY 4.0", v.License);
        Assert.Equal(3, v.PortCount);
        Assert.Null(v.VehicleTypes);
    }

    [Fact]
    public async Task OperatorInfo_All_ReturnsBoth()
        => Assert.Equal(["docomo", "hellocycling"], (await svc.GetOperatorInfoAsync(null, default)).Operators.Select(o => o.Operator));
}
