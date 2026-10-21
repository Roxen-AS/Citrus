using System.Text.Json;
using Microsoft.Win32;

namespace Citrus.Windows;

public sealed class SettingsStore(string? root = null)
{
    public string FilePath { get; } = Path.Combine(root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Citrus"), "settings.json");
    public string? LoadWarning { get; private set; }
    public CitrusSettings Load()
    {
        LoadWarning = null;
        try
        {
            if (!File.Exists(FilePath)) return new();
            var settings = JsonSerializer.Deserialize<CitrusSettings>(File.ReadAllText(FilePath)) ?? throw new JsonException("Empty settings.");
            settings.Validate();
            return settings;
        }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException or ArgumentException)
        { LoadWarning = "Settings could not be read; defaults are active. " + error.Message; return new(); }
    }
    public void Save(CitrusSettings settings)
    {
        settings.Validate();
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        string temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, FilePath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

public sealed class StartupRegistration(string keyPath = @"Software\Microsoft\Windows\CurrentVersion\Run", string valueName = "Citrus", string? executable = null)
{
    public void Apply(bool enabled)
    {
        if (enabled)
        {
            string path = executable ?? Environment.ProcessPath ?? throw new InvalidOperationException("Citrus executable path is unavailable.");
            if (!Path.IsPathFullyQualified(path) || path.Contains('"')) throw new ArgumentException("Startup requires an absolute executable path.");
            using var key = Registry.CurrentUser.CreateSubKey(keyPath);
            key.SetValue(valueName, $"\"{path}\" --background", RegistryValueKind.String);
        }
        else
        {
            using var key = Registry.CurrentUser.OpenSubKey(keyPath, true);
            key?.DeleteValue(valueName, false);
        }
    }
}
