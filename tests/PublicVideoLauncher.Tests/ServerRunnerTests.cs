using PublicVideoLauncher.Services;
using System.Reflection;
using Xunit;

namespace PublicVideoLauncher.Tests;

public sealed class ServerRunnerTests
{
    [Fact]
    public void BuildCaddyConfig_ProxiesAllTranslationCallbackPaths()
    {
        var config = BuildCaddyConfig(@"C:\temp\workspace", 8080, 18761);

        Assert.Contains("@translation path /translation-started /translation-ready /translation-failed", config);
        Assert.Contains("reverse_proxy @translation 127.0.0.1:18761", config);
    }

    [Fact]
    public void BuildCaddyConfig_WithoutCallbackPort_Returns503ForAllTranslationCallbackPaths()
    {
        var config = BuildCaddyConfig(@"C:\temp\workspace", 8080, null);

        Assert.Contains("@translation path /translation-started /translation-ready /translation-failed", config);
        Assert.Contains("respond @translation 503", config);
    }

    private static string BuildCaddyConfig(string rootFolder, int port, int? translationCallbackPort)
    {
        var method = typeof(ServerRunner).GetMethod("BuildCaddyConfig", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var config = method!.Invoke(null, [rootFolder, port, translationCallbackPort]);
        return Assert.IsType<string>(config);
    }
}
