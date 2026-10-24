using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using LicenseShared;

namespace LicenseIntegration;

/// <summary>
/// Copy into a .NET 8 Windows client referencing LicenseShared.
/// trustedBundleJson and secretBase64 must come from YOUR embedded build resources,
/// never from the customer, the license file, or an untrusted adjacent key file.
/// Re-run before protected operations and periodically while the app stays open.
/// </summary>
public static class LicenseGate
{
    public static LicenseValidationResult ValidateFile(string path, string productId,
        string trustedBundleJson, string secretBase64)
    {
        byte[]? secret = null;
        try
        {
            if (!File.Exists(path))
                return LicenseValidationResult.Fail(LicenseStatus.Missing, "Không tìm thấy office.dat.");
            // OFFC1 header (38) + maximum plaintext (65536) + GCM tag (16).
            if (new FileInfo(path).Length > OfficeLicenseFile.MaxPlaintextBytes + 54)
                return LicenseValidationResult.Fail(LicenseStatus.Malformed, "File license quá lớn.");
            secret = Convert.FromBase64String(secretBase64.Trim());
            if (!OfficeLicenseFile.TryOpenForThisMachine(File.ReadAllBytes(path), productId, secret,
                    out string json, out string error))
                return LicenseValidationResult.Fail(LicenseStatus.Malformed, error);
            if (!LicenseCrypto.TryDeserializeEnvelope(json, out var envelope, out error))
                return LicenseValidationResult.Fail(LicenseStatus.Malformed, error);

            var bundle = PublicKeyBundleSerializer.Deserialize(trustedBundleJson);
            var key = bundle?.Keys.FirstOrDefault(k =>
                string.Equals(k.ProductId, productId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(k.KeyId, envelope!.KeyId, StringComparison.OrdinalIgnoreCase));
            if (key is null)
                return LicenseValidationResult.Fail(LicenseStatus.UnknownKey, "Không có public key tin cậy cho license này.");
            if (!LicenseCrypto.Verify(envelope!, key.PublicKeyPem, out var payload, out error))
                return LicenseValidationResult.Fail(LicenseStatus.InvalidSignature, error);

            // This integration also requires MachineGuid for DPAPI clock-state protection,
            // including when the signed license itself uses MAC binding.
            string fingerprint;
            try { fingerprint = MachineFingerprint.GetCurrent(); }
            catch (InvalidOperationException ex)
            {
                return LicenseValidationResult.Fail(LicenseStatus.DeviceIdentityUnavailable, ex.Message);
            }
            var now = DateTimeOffset.UtcNow;
            var result = LicenseRules.Evaluate(payload!, productId, fingerprint,
                "DEV-" + fingerprint[..16], now);
            if (!result.IsValid) return result;
            var clock = new ClockGuard(productId).Check(now, fingerprint);
            if (clock.RollbackDetected)
                return LicenseValidationResult.Fail(LicenseStatus.ClockRollback, "Phát hiện đồng hồ bị lùi.", payload);
            return result;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or FormatException or ArgumentException or CryptographicException
            or System.Text.Json.JsonException or System.ComponentModel.Win32Exception)
        {
            return LicenseValidationResult.Fail(LicenseStatus.Malformed, "Không thể kiểm tra license: " + ex.Message);
        }
        finally
        {
            if (secret is not null) CryptographicOperations.ZeroMemory(secret);
        }
    }
}
