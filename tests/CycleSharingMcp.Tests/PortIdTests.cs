namespace CycleSharingMcp.Tests;

public class PortIdTests
{
    [Theory]
    [InlineData("docomo:5053", Operator.Docomo, "5053")]
    [InlineData("hellocycling:17", Operator.HelloCycling, "17")]
    [InlineData("  Docomo:5053 ", Operator.Docomo, "5053")]
    public void Parse_AcceptsValidIds(string input, Operator op, string station)
        => Assert.Equal(new PortId(op, station), PortId.Parse(input));

    [Theory]
    [InlineData("5053")]
    [InlineData("hello:17")]
    [InlineData("docomo:")]
    [InlineData("")]
    public void Parse_RejectsInvalidIds(string input)
    {
        var ex = Assert.Throws<BikeshareException>(() => PortId.Parse(input));
        Assert.Contains("docomo:5053", ex.Message); // 正しい形式の例を示す
    }

    [Fact]
    public void ToString_RoundTrips() => Assert.Equal("hellocycling:17", PortId.Parse("hellocycling:17").ToString());

    [Fact]
    public void ParseOptional_NullMeansAll() => Assert.Null(OperatorExtensions.ParseOptional(null));

    [Fact]
    public void ParseOptional_Unknown_Throws() => Assert.Throws<BikeshareException>(() => OperatorExtensions.ParseOptional("luup"));
}
