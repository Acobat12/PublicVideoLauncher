namespace PublicVideoLauncher.Models;

public sealed class TunnelProviderOption
{
    public required string Key { get; init; }
    public required string DisplayName { get; init; }

    public override string ToString() => DisplayName;
}
