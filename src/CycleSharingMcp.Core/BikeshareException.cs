namespace CycleSharingMcp.Core;

/// <summary>利用者にそのまま見せてよい日本語のエラー。</summary>
public sealed class BikeshareException(string message) : Exception(message);
