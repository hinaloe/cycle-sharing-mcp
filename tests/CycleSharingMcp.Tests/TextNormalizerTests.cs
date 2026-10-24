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
    [InlineData("池袋本町 4 丁目ー26ー\u00AD13", "池袋本町4-26-13")] // 長音記号・ソフトハイフン
    [InlineData("蔵前４丁目３７\u22127", "蔵前4-37-7")] // マイナス記号
    [InlineData("中央1\u20102\u20143", "中央1-2-3")] // ハイフン・ダッシュ
    [InlineData("八丁堀3-1", "八丁堀3-1")] // 「丁目」が続かない漢数字は変えない
    // 以下は実データの書き方。ダッシュ類・マイナス記号は前後を問わず「-」にする
    [InlineData("大阪府大阪市城東区中央２丁目１４\u2212Ｃ", "大阪府大阪市城東区中央2-14-c")]
    [InlineData("愛知県名古屋市中区栄１丁目２\u2212番４９号", "愛知県名古屋市中区栄1-2-番49号")]
    [InlineData("沖縄県西原町上原１丁目20番地\u2212４", "沖縄県西原町上原1-20番地-4")]
    [InlineData("埼玉県さいたま市浦和区常磐9丁目\u201015\u201017", "埼玉県さいたま市浦和区常磐9-15-17")]
    // 長音記号は、ひらがな・カタカナの後ろ以外なら「-」にする
    [InlineData("高山町8916番地ー5", "高山町8916番地-5")]
    [InlineData("新開4ーー2", "新開4-2")]
    [InlineData("センター北1", "せんたー北1")]
    [InlineData("ドーーム前1", "どーーむ前1")]
    [InlineData("ヴィラルヴェロ", "ゔぃらるゔぇろ")]
    [InlineData("1⁃2", "1-2")] // U+2043 ハイフン箇条書き記号（Pd ではないが含める）
    [InlineData("1〜3", "1〜3")] // U+301C 波ダッシュ（Pd だが範囲の意味なので変えない）
    [InlineData("1〰3", "1〰3")] // U+3030 波線ダッシュも同じ
    public void NormalizeAddress_ChomeAsHyphen(string input, string expected)
        => Assert.Equal(expected, TextNormalizer.NormalizeAddress(input));

    [Fact]
    public void Normalize_FullWidthDigits()
        => Assert.Equal("台東4丁目31-1", TextNormalizer.Normalize("台東４丁目３１－１"));
}
