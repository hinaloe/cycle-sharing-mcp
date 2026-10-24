namespace CycleSharingMcp.Tests;

public class AddressGrouperTests
{
    [Fact]
    public void ExtendsEachGroupToItsCommonContinuation()
    {
        var addresses = Enumerable.Range(1, 30).Select(i => $"東京都台東区浅草{i}")
            .Concat(Enumerable.Range(1, 15).Select(i => $"東京都台東区上野{i}"));

        var groups = AddressGrouper.Split("台東区", addresses, 40);

        Assert.Equal([("台東区浅草", 30), ("台東区上野", 15)], groups);
    }

    [Fact]
    public void SplitsGroupsOverTheLimitFurther()
    {
        var groups = AddressGrouper.Split("丸の内", Enumerable.Range(0, 41).Select(i => $"東京都千代田区丸の内{i}"), 40);

        Assert.Contains(("丸の内1", 11), groups);
        Assert.All(groups, g => Assert.InRange(g.Count, 1, 40));
        Assert.Equal(41, groups.Sum(g => g.Count));
    }

    [Fact]
    public void IdenticalAddresses_CannotBeSplit_AreReturnedAsOne()
        => Assert.Equal([("丸の内1", 45)], AddressGrouper.Split("丸の内", Enumerable.Repeat("東京都千代田区丸の内1", 45), 40));

    [Fact]
    public void OrdersByCountThenText()
    {
        var addresses = new[] { "x市b1", "x市a1", "x市c1", "x市c2" };

        Assert.Equal([("x市c", 2), ("x市a1", 1), ("x市b1", 1)], AddressGrouper.Split("x市", addresses, 3));
    }
}
