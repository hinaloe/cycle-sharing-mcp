namespace CycleSharingMcp.Tests;

public class GeoMathTests
{
    [Fact]
    public void Distance_TokyoToShinjuku()
        => Assert.InRange(GeoMath.DistanceMeters(35.681236, 139.767125, 35.690921, 139.700258), 6100, 6170);

    [Fact]
    public void Distance_SamePoint_IsZero() => Assert.Equal(0, GeoMath.DistanceMeters(35.6457, 139.8268, 35.6457, 139.8268));
}
