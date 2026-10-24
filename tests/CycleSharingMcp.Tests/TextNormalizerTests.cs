namespace CycleSharingMcp.Tests;

public class TextNormalizerTests
{
    [Fact]
    public void Normalize_FoldsWidthKanaCaseAndSpace()
        => Assert.Equal("しぶやport", TextNormalizer.Normalize("ｼﾌﾞﾔ　Ｐｏｒｔ"));

    [Theory]
    [InlineData("台東４丁目３１－１", "台東4-31-1")]
    [InlineData("台東4-30-6", "台東4-30-6")]
    [InlineData("台東4丁目", "台東4-")]
    public void NormalizeAddress_ChomeAsHyphen(string input, string expected)
        => Assert.Equal(expected, TextNormalizer.NormalizeAddress(input));

    [Fact]
    public void Normalize_FullWidthDigits()
        => Assert.Equal("台東4丁目31-1", TextNormalizer.Normalize("台東４丁目３１－１"));
}
