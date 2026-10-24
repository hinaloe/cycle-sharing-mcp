namespace CycleSharingMcp.Tests;

/// <summary>record の ToString() は全項目を並べるので、設定をうっかりログに出してもトークンが出ないようにする。</summary>
public class SecretRedactionTests
{
    [Fact]
    public void OdptOptions_ToString_HidesToken()
    {
        var text = new OdptOptions("secret-token").ToString();

        Assert.DoesNotContain("secret-token", text);
        Assert.Contains("AccessToken = ***", text);
    }

    [Fact]
    public void ServerOptions_ToString_HidesTokenButShowsOthers()
    {
        var text = new ServerOptions(true, "127.0.0.1", 8080, "secret-token").ToString();

        Assert.DoesNotContain("secret-token", text);
        Assert.Contains("AccessToken = ***", text);
        Assert.Contains("Port = 8080", text);
    }

    [Fact]
    public void ToString_WithoutToken_ShowsNull()
        => Assert.Contains("AccessToken = (なし)", new OdptOptions(null).ToString());
}
