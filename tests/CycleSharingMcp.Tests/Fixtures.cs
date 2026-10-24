namespace CycleSharingMcp.Tests;

internal static class Fixtures
{
    public static byte[] Read(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
}
