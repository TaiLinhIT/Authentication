using Microsoft.Win32;
using System.Security.Cryptography;
using System.Text;

namespace LicenseShared;

/// <summary>
/// Practical Windows identity signal: MachineGuid + machine name hashed with SHA-256.
/// This is not hardware-rooted: it can change after a Windows reinstall and a privileged
/// attacker can copy it. Use a server or TPM-backed device key for higher assurance.
/// </summary>
public static class MachineFingerprint
{
    private const string Prefix = "WarehouseLicense|v1|";

    public static string GetCurrent()
    {
        string machineGuid = "";
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            machineGuid = key?.GetValue("MachineGuid")?.ToString() ?? "";
        }
        catch (Exception)
        {
            // Reported below; registry access can be restricted by policy.
        }

        if (string.IsNullOrWhiteSpace(machineGuid))
            throw new InvalidOperationException(
                "Cannot read the Windows MachineGuid. Run on Windows and check registry permissions.");

        byte[] bytes = Encoding.UTF8.GetBytes(
            $"{Prefix}{machineGuid}|{Environment.MachineName.ToUpperInvariant()}");

        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    /// <summary>Short, human-friendly device id shown by the client for manual registration.</summary>
    public static string GetShortDeviceId() => "DEV-" + GetCurrent()[..16];

    public static string GetMachineLabel() => Environment.MachineName;
}
