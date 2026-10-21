using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Citrus.Windows.Updates;

internal sealed record UpdateInstallation(string Directory, string InventoryHash, bool CanUpdateSilently)
{
    internal const string RegistryPath = @"Software\Citrus";
    internal static string ExpectedDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Citrus");

    internal static UpdateInstallation? ReadCurrent()
    {
        using var hive = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
        using var key = hive.OpenSubKey(RegistryPath);
        if (key is null) return null;
        return Validate(AppContext.BaseDirectory, ExpectedDirectory,
            key.GetValue("InstallLocation") as string, key.GetValue("PackageInventory") as string,
            key.GetValue("RecoveryIdentity") as string, key.GetValue("AcceptedSdkLicenseSha256") as string);
    }

    internal static bool HasPendingTransaction()
    {
        using var hive = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
        using var key = hive.OpenSubKey(RegistryPath);
        return IsTransactionPending(key?.GetValue("UpdateState") as string);
    }
    internal static bool IsTransactionPending(string? state) => !string.IsNullOrEmpty(state);

    internal static UpdateInstallation? Validate(string runningDirectory, string expectedDirectory, string? registeredDirectory,
        string? inventory, string? recoveryIdentity, string? acceptedLicense)
    {
        try
        {
            string directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(expectedDirectory));
            if (!StringComparer.OrdinalIgnoreCase.Equals(directory, Path.TrimEndingDirectorySeparator(Path.GetFullPath(runningDirectory))) ||
                !StringComparer.OrdinalIgnoreCase.Equals(directory, registeredDirectory) || !IsHash(inventory) ||
                !StringComparer.OrdinalIgnoreCase.Equals(recoveryIdentity, directory + "|" + inventory) ||
                HasLinkedAncestor(directory)) return null;
            string manifest = Path.Combine(directory, "install-inventory.json");
            if (HasLinkedAncestor(manifest) || !HashMatches(manifest, inventory!)) return null;
            string license = Path.Combine(directory, "notices", "presentation", "Windows-SDK-License.rtf");
            bool accepted = IsHash(acceptedLicense) && !HasLinkedAncestor(license) && HashMatches(license, acceptedLicense!);
            return new(directory, inventory!.ToLowerInvariant(), accepted);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
        { return null; }
    }

    internal static bool IsHash(string? value) => value is not null && Regex.IsMatch(value, "\\A[a-fA-F0-9]{64}\\z", RegexOptions.CultureInvariant);
    internal static bool HashMatches(string file, string expected)
    {
        if (!File.Exists(file)) return false;
        using var stream = File.OpenRead(file);
        return StringComparer.OrdinalIgnoreCase.Equals(Convert.ToHexString(SHA256.HashData(stream)), expected);
    }
    internal static bool HasLinkedAncestor(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return true;
        return false;
    }
}
