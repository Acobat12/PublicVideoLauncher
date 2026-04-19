using System;
using System.IO;
using System.Text.Json;
using PublicVideoLauncher.Models;

namespace PublicVideoLauncher.Services;

public static class TunnelSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static TunnelLaunchSettings Load(string rootFolder)
    {
        try
        {
            var path = GetSettingsPath(rootFolder);
            if (!File.Exists(path))
                return new TunnelLaunchSettings();

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<TunnelLaunchSettings>(json, JsonOptions) ?? new TunnelLaunchSettings();
        }
        catch
        {
            return new TunnelLaunchSettings();
        }
    }

    public static void Save(string rootFolder, TunnelLaunchSettings settings)
    {
        if (string.IsNullOrWhiteSpace(rootFolder))
            return;

        var path = GetSettingsPath(rootFolder);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(settings ?? new TunnelLaunchSettings(), JsonOptions);
        File.WriteAllText(path, json);
    }

    public static string GetSettingsPath(string rootFolder)
    {
        var toolsRoot = Path.Combine(rootFolder, ".tools");
        return Path.Combine(toolsRoot, "tunnel-settings.json");
    }
}
