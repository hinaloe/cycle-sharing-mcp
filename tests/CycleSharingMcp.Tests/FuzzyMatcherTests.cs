namespace CycleSharingMcp.Tests;

public class FuzzyMatcherTests
{
    [Theory]
    [InlineData("新木場", "h1-57.新木場駅", 0)]
    [InlineData("新木馬", "h1-57.新木場駅", 1)]
    [InlineData("h1-57", "h1-57.新木場駅", 0)]
    [InlineData("abc", "", 3)]
    [InlineData("", "abc", 0)]
    [InlineData("新木場駅前ポート東口", "新木場駅", 6)] // 検索語が名前より長くても落ちない
    public void SubstringEditDistance_Cases(string pattern, string text, int expected)
        => Assert.Equal(expected, FuzzyMatcher.SubstringEditDistance(pattern, text));

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 0)]
    [InlineData(3, 1)]
    [InlineData(7, 1)]
    [InlineData(8, 2)]
    [InlineData(12, 3)]
    public void MaxDistance_Cases(int length, int expected) => Assert.Equal(expected, FuzzyMatcher.MaxDistance(length));
}
