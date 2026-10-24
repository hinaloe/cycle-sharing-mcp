namespace CycleSharingMcp.Tests;

public class TextNormalizerTests
{
    [Fact]
    public void Normalize_FoldsWidthKanaCaseAndSpace()
        => Assert.Equal("しぶやport", TextNormalizer.Normalize("ｼﾌﾞﾔ　Ｐｏｒｔ"));

    [Fact]
    public void Normalize_FullWidthDigits()
        => Assert.Equal("台東4丁目31-1", TextNormalizer.Normalize("台東４丁目３１－１"));
}
