namespace CycleSharingMcp.Tests;

public class ServerOptionsTests
{
    private static string? NoEnv(string _) => null;

    [Fact]
    public void Defaults() => Assert.Equal(new ServerOptions(false, "127.0.0.1", 8080, null), ServerOptions.Parse([], NoEnv));

    [Fact]
    public void EnvToken() => Assert.Equal("e", ServerOptions.Parse([], k => k == "ODPT_ACCESS_TOKEN" ? "e" : null).AccessToken);

    [Fact]
    public void EmptyEnvToken_IsNull() => Assert.Null(ServerOptions.Parse([], _ => "").AccessToken);

    [Fact]
    public void ArgTokenWins() => Assert.Equal("a", ServerOptions.Parse(["--odpt-token", "a"], _ => "e").AccessToken);

    [Fact]
    public void Http() => Assert.Equal(new ServerOptions(true, "0.0.0.0", 9000, null),
        ServerOptions.Parse(["--http", "--port", "9000", "--host", "0.0.0.0"], NoEnv));

    [Theory]
    [InlineData("--port", "abc")]
    [InlineData("--port", "0")]
    [InlineData("--port", "65536")]
    [InlineData("--foo")]
    [InlineData("--odpt-token")]
    [InlineData("--odpt-token", "")]
    [InlineData("--host")]
    public void Invalid_Throws(params string[] args) => Assert.Throws<ArgumentException>(() => ServerOptions.Parse(args, NoEnv));
}
