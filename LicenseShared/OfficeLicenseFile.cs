using System.Security.Cryptography;
using System.Text;

namespace LicenseShared;

/// <summary>
/// Encrypted license file ("office.dat"). The signed license JSON is sealed with
/// AES-256-GCM; the key is derived with HKDF-SHA256 from a vendor secret, the product id
/// and the machine binding value, so a copied file refuses to open on another machine and
/// inside applications that do not carry the vendor secret. The inner signature must still
/// be verified with <see cref="LicenseCrypto.Verify"/> after opening — the encryption is a
/// transport/obscurity layer, the signature is the trust anchor.
///
/// Layout (little-endian lengths):
///   0  5   magic   "OFFC1"
///   5  1   version 1
///   6  16  salt    HKDF salt (random per file)
///   22 12  nonce   AES-GCM nonce (random per file)
///   34 4   payload length N (int32)
///   38 N   AES-256-GCM ciphertext
///   38+N   16-byte GCM tag
/// </summary>
public static class OfficeLicenseFile
{
    public const string DefaultFileName = "office.dat";
    public const int SecretSizeBytes = 32;
    public const int MaxPlaintextBytes = 64 * 1024;

    private static ReadOnlySpan<byte> Magic => "OFFC1"u8;
    private const byte Version = 1;
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int HeaderSize = 5 + 1 + SaltSize + NonceSize + 4;
    private const string KdfLabel = "OFFC1";

    /// <summary>Creates a new random vendor secret (32 bytes). Embed it in the client app.</summary>
    public static byte[] CreateSecret() => RandomNumberGenerator.GetBytes(SecretSizeBytes);

    /// <summary>Seals a signed license JSON into an office.dat blob bound to one machine.</summary>
    /// <param name="licenseJson">The signed envelope JSON (<see cref="LicenseCrypto.SerializeEnvelope"/>).</param>
    /// <param name="productId">The product id the license belongs to.</param>
    /// <param name="bindingValue">The device value carried by the license: the machine
    /// fingerprint, short device id, or normalized twelve-digit MAC address.</param>
    /// <param name="secret">The vendor secret embedded in the client application.</param>
    public static byte[] Create(string licenseJson, string productId, string bindingValue, byte[] secret)
    {
        ArgumentNullException.ThrowIfNull(licenseJson);
        ValidateIdentity(productId, bindingValue, secret);

        byte[] plaintext = Encoding.UTF8.GetBytes(licenseJson);
        if (plaintext.Length == 0)
            throw new ArgumentException("The license content is empty.", nameof(licenseJson));
        if (plaintext.Length > MaxPlaintextBytes)
            throw new ArgumentException("The license content is too large.", nameof(licenseJson));

        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
        byte[] key = DeriveKey(secret, salt, productId, bindingValue);

        byte[] ciphertext = new byte[plaintext.Length];
        byte[] tag = new byte[TagSize];

        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Encrypt(nonce, plaintext, ciphertext, tag);

            byte[] file = new byte[HeaderSize + ciphertext.Length + TagSize];
            int offset = 0;
            Magic.CopyTo(file);
            offset += 5;
            file[offset++] = Version;
            salt.CopyTo(file.AsSpan(offset));
            offset += SaltSize;
            nonce.CopyTo(file.AsSpan(offset));
            offset += NonceSize;
            BitConverter.TryWriteBytes(file.AsSpan(offset, 4), ciphertext.Length);
            offset += 4;
            ciphertext.CopyTo(file.AsSpan(offset));
            offset += ciphertext.Length;
            tag.CopyTo(file.AsSpan(offset));

            return file;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    /// <summary>Opens an office.dat blob for one specific binding value.</summary>
    public static bool TryOpen(
        byte[]? file,
        string productId,
        string bindingValue,
        byte[]? secret,
        out string licenseJson,
        out string error)
    {
        licenseJson = "";
        error = "";

        if (file is null || file.Length < HeaderSize + TagSize)
        {
            error = "The license file is empty or truncated.";
            return false;
        }

        if (!file.AsSpan(0, 5).SequenceEqual(Magic))
        {
            error = "This is not an office.dat license file.";
            return false;
        }

        if (file[5] != Version)
        {
            error = $"Unsupported license file version {file[5]}.";
            return false;
        }

        if (secret is null || secret.Length != SecretSizeBytes)
        {
            error = "The application vendor secret is missing or invalid.";
            return false;
        }

        if (!IsIdentityValue(productId) || !IsIdentityValue(bindingValue))
        {
            error = "The product id or the device binding value is invalid.";
            return false;
        }

        int offset = 6;
        byte[] salt = file.AsSpan(offset, SaltSize).ToArray();
        offset += SaltSize;
        byte[] nonce = file.AsSpan(offset, NonceSize).ToArray();
        offset += NonceSize;
        int length = BitConverter.ToInt32(file, offset);
        offset += 4;

        if (length <= 0 || length > MaxPlaintextBytes || file.Length != HeaderSize + length + TagSize)
        {
            error = "The license file is damaged.";
            return false;
        }

        byte[] ciphertext = file.AsSpan(offset, length).ToArray();
        offset += length;
        byte[] tag = file.AsSpan(offset, TagSize).ToArray();

        byte[] key = DeriveKey(secret, salt, productId, bindingValue);
        byte[] plaintext = new byte[length];

        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);
        }
        catch (CryptographicException)
        {
            error = "The license file cannot be opened on this machine, or the file was modified.";
            return false;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }

        licenseJson = Encoding.UTF8.GetString(plaintext);
        CryptographicOperations.ZeroMemory(plaintext);
        return true;
    }

    /// <summary>
    /// Opens an office.dat blob on the current machine. Tries the full fingerprint first,
    /// then the short device id and every available normalized MAC address.
    /// </summary>
    public static bool TryOpenForThisMachine(
        byte[]? file,
        string productId,
        byte[]? secret,
        out string licenseJson,
        out string error)
    {
        string fingerprint;
        string shortId;
        try
        {
            fingerprint = MachineFingerprint.GetCurrent();
            shortId = MachineFingerprint.GetShortDeviceId();
        }
        catch (InvalidOperationException ex)
        {
            licenseJson = "";
            error = ex.Message;
            foreach (string mac in MacAddress.GetCurrent())
                if (TryOpen(file, productId, mac, secret, out licenseJson, out error)) return true;
            return false;
        }

        if (TryOpen(file, productId, fingerprint, secret, out licenseJson, out error))
            return true;

        // The fingerprint path is the common case; keep its message when both fail.
        string fingerprintError = error;

        if (TryOpen(file, productId, shortId, secret, out licenseJson, out error))
            return true;

        foreach (string mac in MacAddress.GetCurrent())
            if (TryOpen(file, productId, mac, secret, out licenseJson, out error)) return true;

        error = fingerprintError;
        return false;
    }

    /// <summary>HKDF-SHA256 over the vendor secret, salted and bound to product + machine.</summary>
    public static byte[] DeriveKey(byte[] secret, byte[] salt, string productId, string bindingValue)
    {
        byte[] info = Encoding.UTF8.GetBytes(
            $"{KdfLabel}|{productId.Trim().ToUpperInvariant()}|{bindingValue.Trim().ToUpperInvariant()}");

        return HKDF.DeriveKey(HashAlgorithmName.SHA256, secret, 32, salt, info);
    }

    private static void ValidateIdentity(string productId, string bindingValue, byte[] secret)
    {
        ArgumentNullException.ThrowIfNull(productId);
        ArgumentNullException.ThrowIfNull(bindingValue);
        ArgumentNullException.ThrowIfNull(secret);

        if (!IsIdentityValue(productId))
            throw new ArgumentException("The product id is empty or too long.", nameof(productId));
        if (!IsIdentityValue(bindingValue))
            throw new ArgumentException("The device binding value is empty or too long.", nameof(bindingValue));
        if (secret.Length != SecretSizeBytes)
            throw new ArgumentException($"The vendor secret must be {SecretSizeBytes} bytes.", nameof(secret));
    }

    private static bool IsIdentityValue(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length is > 0 and <= 256;
}
