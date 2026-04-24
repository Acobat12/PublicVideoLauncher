using System;
using System.IO;
using System.Text.Json;

namespace PublicVideoLauncher.Services;

public static class UiSettingsStore
{
    private sealed class UiSettings
    {
        public string? Language { get; set; }
    }

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PublicVideoLauncher",
        "ui-settings.json");

    public static AppLanguage LoadLanguage()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return AppLanguage.English;

            var settings = JsonSerializer.Deserialize<UiSettings>(File.ReadAllText(SettingsPath));
            return Enum.TryParse<AppLanguage>(settings?.Language, ignoreCase: true, out var language)
                ? language
                : AppLanguage.English;
        }
        catch
        {
            return AppLanguage.English;
        }
    }

    public static void SaveLanguage(AppLanguage language)
    {
        try
        {
            var directory = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            var settings = new UiSettings { Language = language.ToString() };
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, new JsonSerializerOptions
            {
                WriteIndented = true
            }));
        }
        catch
        {
        }
    }
}
