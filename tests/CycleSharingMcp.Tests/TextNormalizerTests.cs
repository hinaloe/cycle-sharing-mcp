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
    [InlineData("銀座一丁目", "銀座1-")]
    [InlineData("元町二丁目27番1", "元町2-27番1")]
    [InlineData("十丁目", "10-")]
    [InlineData("十二丁目", "12-")]
    [InlineData("二十丁目", "20-")]
    [InlineData("二十三丁目", "23-")]
    [InlineData("池袋本町 4 丁目ー26ー­13", "池袋本町4-26-13")] // 長音記号・ソフトハイフン
    [InlineData("蔵前４丁目３７−7", "蔵前4-37-7")] // マイナス記号
    [InlineData("中央1‐2—3", "中央1-2-3")] // ハイフン・ダッシュ
    [InlineData("八丁堀3-1", "八丁堀3-1")] // 「丁目」が続かない漢数字は変えない
    [InlineData("センター北1", "せんたー北1")] // 数字にはさまれない「ー」は変えない
    public void NormalizeAddress_ChomeAsHyphen(string input, string expected)
        => Assert.Equal(expected, TextNormalizer.NormalizeAddress(input));

    [Fact]
    public void Normalize_FullWidthDigits()
        => Assert.Equal("台東4丁目31-1", TextNormalizer.Normalize("台東４丁目３１－１"));
}
