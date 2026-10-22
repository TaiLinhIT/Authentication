using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LicenseShared;

/// <summary>
/// All cryptography used by the license system.
/// The private key never leaves the issuer machine; the client only receives public keys.
/// </summary>
public static class LicenseCrypto
{
    public const string Format = "WLC-1";
    public const int MinimumPasswordLength = 12;
    public const int KeyIdHexLength = 16;

    /// <summary>PBKDF2 iterations for new private keys (OWASP 2023+ guidance for PBKDF2-HMAC-SHA256).
    /// Existing key files carry their own iteration count, so older keys still open unchanged.</summary>
    private const int PbeIterations = 600_000;
    private const int MaxEnvelopeChars = 128 * 1024;
    private const string RsaLabel = "RSA-{0}-PSS-SHA256";
    private const string EcdsaLabel = "ECDSA-P{0}-SHA{1}";

    private static readonly PbeParameters Pbe = new(
        PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, PbeIterations);

    private static readonly JsonSerializerOptions EnvelopeOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    // ---------------------------------------------------------------- keys

    /// <summary>Creates a new encrypted private key and its public key. Returns the key id.</summary>
    public static string CreateKeyPair(
        SigningAlgorithm algorithm, int keySize, string privateKeyPath, string publicKeyPath, string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < MinimumPasswordLength)
            throw new ArgumentException($"Use a private-key password with at least {MinimumPasswordLength} characters.");

        ValidateKeySize(algorithm, keySize);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(privateKeyPath))!);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(publicKeyPath))!);

        byte[] publicKeyBytes;
        string encryptedPrivatePem;
        string publicPem;

        if (algorithm == SigningAlgorithm.Rsa)
        {
            using var rsa = RSA.Create(keySize);
            publicKeyBytes = rsa.ExportSubjectPublicKeyInfo();
            encryptedPrivatePem = rsa.ExportEncryptedPkcs8PrivateKeyPem(password, Pbe);
            publicPem = rsa.ExportSubjectPublicKeyInfoPem();
        }
        else
        {
            using var ecdsa = ECDsa.Create(CurveFromKeySize(keySize));
            publicKeyBytes = ecdsa.ExportSubjectPublicKeyInfo();
            encryptedPrivatePem = ecdsa.ExportEncryptedPkcs8PrivateKeyPem(password, Pbe);
            publicPem = ecdsa.ExportSubjectPublicKeyInfoPem();
        }

        File.WriteAllText(privateKeyPath, encryptedPrivatePem, new UTF8Encoding(false));
        File.WriteAllText(publicKeyPath, publicPem, new UTF8Encoding(false));

        return ComputeKeyId(publicKeyBytes);
    }

    /// <summary>Re-exports the public key from an encrypted private key.</summary>
    public static string ExportPublicKeyPem(string privateKeyPath, string password, SigningAlgorithm algorithm)
    {
        string encryptedPem = File.ReadAllText(privateKeyPath);
        if (algorithm == SigningAlgorithm.Rsa)
        {
            using var rsa = RSA.Create();
            ImportPrivate(rsa, encryptedPem, password);
            return rsa.ExportSubjectPublicKeyInfoPem();
        }

        using var ecdsa = ECDsa.Create();
        ImportPrivate(ecdsa, encryptedPem, password);
        return ecdsa.ExportSubjectPublicKeyInfoPem();
    }

    public static string ComputeKeyId(ReadOnlySpan<byte> subjectPublicKeyInfo) =>
        Convert.ToHexString(SHA256.HashData(subjectPublicKeyInfo))[..KeyIdHexLength];

    public static string ComputeKeyIdFromPublicPem(string publicKeyPem) =>
        ComputeKeyId(PemBodyToDer(publicKeyPem));

    /// <summary>Checks that a password opens an encrypted private key file.</summary>
    public static bool VerifyPrivateKeyPassword(string privateKeyPath, string password)
    {
        if (!File.Exists(privateKeyPath) || string.IsNullOrEmpty(password)) return false;

        try
        {
            string pem = File.ReadAllText(privateKeyPath);

            using (var rsa = RSA.Create())
            {
                try { rsa.ImportFromEncryptedPem(pem, password); return true; }
                catch (CryptographicException) { }
                catch (ArgumentException) { }
            }

            using (var ecdsa = ECDsa.Create())
            {
                try { ecdsa.ImportFromEncryptedPem(pem, password); return true; }
                catch (CryptographicException) { }
                catch (ArgumentException) { }
            }

            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    // ---------------------------------------------------------------- signing

    public static SignedLicense Sign(
        LicensePayload payload, string privateKeyPath, string password, SigningAlgorithm algorithm)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (!File.Exists(privateKeyPath))
            throw new FileNotFoundException("The signing key for this product is missing.", privateKeyPath);

        string encryptedPem = File.ReadAllText(privateKeyPath);
        byte[] signature;
        string keyId;
        string algorithmLabel;

        if (algorithm == SigningAlgorithm.Rsa)
        {
            using var rsa = RSA.Create();
            ImportPrivate(rsa, encryptedPem, password);

            byte[] publicKeyBytes = rsa.ExportSubjectPublicKeyInfo();
            keyId = ComputeKeyId(publicKeyBytes);
            payload.KeyId = keyId;

            byte[] payloadBytes = LicenseCodec.SerializePayload(payload);
            signature = rsa.SignData(payloadBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
            algorithmLabel = string.Format(RsaLabel, rsa.KeySize);

            return BuildEnvelope(algorithmLabel, keyId, payloadBytes, signature);
        }
        else
        {
            using var ecdsa = ECDsa.Create();
            ImportPrivate(ecdsa, encryptedPem, password);

            byte[] publicKeyBytes = ecdsa.ExportSubjectPublicKeyInfo();
            keyId = ComputeKeyId(publicKeyBytes);
            payload.KeyId = keyId;

            var hash = ecdsa.KeySize >= 384 ? HashAlgorithmName.SHA384 : HashAlgorithmName.SHA256;
            byte[] payloadBytes = LicenseCodec.SerializePayload(payload);
            signature = ecdsa.SignData(payloadBytes, hash, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            algorithmLabel = string.Format(EcdsaLabel, ecdsa.KeySize, hash == HashAlgorithmName.SHA384 ? "384" : "256");

            return BuildEnvelope(algorithmLabel, keyId, payloadBytes, signature);
        }
    }

    private static SignedLicense BuildEnvelope(
        string algorithmLabel, string keyId, byte[] payloadBytes, byte[] signature) => new()
        {
            Format = Format,
            Algorithm = algorithmLabel,
            KeyId = keyId,
            PayloadBase64 = Convert.ToBase64String(payloadBytes),
            SignatureBase64 = Convert.ToBase64String(signature)
        };

    // ---------------------------------------------------------------- verification

    /// <summary>
    /// Verifies the signature and key id of an envelope and decodes the payload.
    /// Does not check product, device or dates; callers use <see cref="LicenseRules"/> for that.
    /// </summary>
    public static bool Verify(
        SignedLicense envelope, string publicKeyPem, out LicensePayload? payload, out string error)
    {
        payload = null;
        error = "";

        if (envelope is null)
        {
            error = "The license is empty.";
            return false;
        }

        if (!string.Equals(envelope.Format, Format, StringComparison.Ordinal))
        {
            error = "Unsupported license format.";
            return false;
        }

        if (!TryParseAlgorithmLabel(envelope.Algorithm, out var algorithm, out _, out var hash))
        {
            error = "Unsupported license algorithm.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(publicKeyPem))
        {
            error = "No public key is available for this license.";
            return false;
        }

        byte[] payloadBytes;
        byte[] signature;
        try
        {
            payloadBytes = Convert.FromBase64String(envelope.PayloadBase64);
            signature = Convert.FromBase64String(envelope.SignatureBase64);
        }
        catch (FormatException)
        {
            error = "The license data is not valid base64.";
            return false;
        }

        bool verified;
        try
        {
            verified = algorithm == SigningAlgorithm.Rsa
                ? VerifyRsa(payloadBytes, signature, hash, publicKeyPem)
                : VerifyEcdsa(payloadBytes, signature, hash, publicKeyPem);
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException)
        {
            error = "The public key is not valid for this license.";
            return false;
        }

        if (!verified)
        {
            error = "The license signature is invalid. The key was altered or was not issued by this vendor.";
            return false;
        }

        string actualKeyId;
        try
        {
            actualKeyId = ComputeKeyIdFromPublicPem(publicKeyPem);
        }
        catch (FormatException)
        {
            error = "The public key is not valid.";
            return false;
        }

        if (!string.Equals(actualKeyId, envelope.KeyId, StringComparison.OrdinalIgnoreCase))
        {
            error = "The license was signed with a different signing key.";
            return false;
        }

        try
        {
            payload = LicenseCodec.DeserializePayload(payloadBytes);
        }
        catch (Exception ex) when (ex is FormatException or EndOfStreamException or ArgumentException)
        {
            error = $"The license payload is invalid: {ex.Message}";
            return false;
        }

        if (!string.Equals(payload.KeyId, envelope.KeyId, StringComparison.OrdinalIgnoreCase))
        {
            error = "The license payload is inconsistent with its envelope.";
            payload = null;
            return false;
        }

        return true;
    }

    private static bool VerifyRsa(
        byte[] payloadBytes, byte[] signature, HashAlgorithmName hash, string publicKeyPem)
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(publicKeyPem);
        return rsa.VerifyData(payloadBytes, signature, hash, RSASignaturePadding.Pss);
    }

    private static bool VerifyEcdsa(
        byte[] payloadBytes, byte[] signature, HashAlgorithmName hash, string publicKeyPem)
    {
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(publicKeyPem);

        if (ecdsa.VerifyData(payloadBytes, signature, hash, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
            return true;

        // Accept DER-encoded signatures too, for interoperability with other tools.
        if (signature.Length > 8)
        {
            try
            {
                return ecdsa.VerifyData(payloadBytes, signature, hash, DSASignatureFormat.Rfc3279DerSequence);
            }
            catch (CryptographicException)
            {
                return false;
            }
        }

        return false;
    }

    // ---------------------------------------------------------------- envelope json

    public static string SerializeEnvelope(SignedLicense envelope) =>
        JsonSerializer.Serialize(envelope, EnvelopeOptions);

    public static bool TryDeserializeEnvelope(string? json, out SignedLicense? envelope, out string error)
    {
        envelope = null;
        error = "";

        if (string.IsNullOrWhiteSpace(json))
        {
            error = "The license file is empty.";
            return false;
        }

        if (json.Length > MaxEnvelopeChars)
        {
            error = "The license file is too large.";
            return false;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<SignedLicense>(json, EnvelopeOptions);
            if (parsed is null)
            {
                error = "The license file is empty.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(parsed.PayloadBase64) || string.IsNullOrWhiteSpace(parsed.SignatureBase64))
            {
                error = "The license file is missing signed data.";
                return false;
            }

            envelope = parsed;
            return true;
        }
        catch (JsonException)
        {
            error = "The license file is not valid JSON.";
            return false;
        }
    }

    // ---------------------------------------------------------------- algorithms

    public static string AlgorithmLabel(SigningAlgorithm algorithm, int keySize) =>
        algorithm == SigningAlgorithm.Rsa
            ? string.Format(RsaLabel, keySize)
            : keySize >= 384
                ? string.Format(EcdsaLabel, 384, "384")
                : string.Format(EcdsaLabel, 256, "256");

    public static string AlgorithmLabel(byte algorithmId) => algorithmId switch
    {
        1 => "RSA-2048-PSS-SHA256",
        2 => "RSA-3072-PSS-SHA256",
        3 => "RSA-4096-PSS-SHA256",
        4 => "ECDSA-P256-SHA256",
        5 => "ECDSA-P384-SHA384",
        _ => throw new FormatException($"Unknown license algorithm id {algorithmId}.")
    };

    public static byte AlgorithmId(string? algorithmLabel)
    {
        if (!TryParseAlgorithmLabel(algorithmLabel, out var algorithm, out int keySize, out _))
            throw new FormatException($"Unsupported algorithm '{algorithmLabel}'.");

        return (algorithm, keySize) switch
        {
            (SigningAlgorithm.Rsa, 2048) => 1,
            (SigningAlgorithm.Rsa, 3072) => 2,
            (SigningAlgorithm.Rsa, 4096) => 3,
            (SigningAlgorithm.Ecdsa, <= 256) => 4,
            (SigningAlgorithm.Ecdsa, _) => 5,
            _ => throw new FormatException($"Unsupported algorithm '{algorithmLabel}'.")
        };
    }

    public static bool TryParseAlgorithmLabel(
        string? label, out SigningAlgorithm algorithm, out int keySize, out HashAlgorithmName hash)
    {
        algorithm = SigningAlgorithm.Rsa;
        keySize = 0;
        hash = HashAlgorithmName.SHA256;

        if (string.IsNullOrWhiteSpace(label)) return false;

        string value = label.Trim().ToUpperInvariant();
        hash = value.Contains("SHA384", StringComparison.Ordinal) ? HashAlgorithmName.SHA384 : HashAlgorithmName.SHA256;

        if (value.StartsWith("RSA-", StringComparison.Ordinal))
        {
            string remainder = value[4..];
            int dash = remainder.IndexOf('-');
            string sizePart = dash > 0 ? remainder[..dash] : remainder;
            if (!int.TryParse(sizePart, out keySize)) return false;
            if (keySize is not (2048 or 3072 or 4096)) return false;
            algorithm = SigningAlgorithm.Rsa;
            return true;
        }

        if (value.StartsWith("ECDSA", StringComparison.Ordinal))
        {
            algorithm = SigningAlgorithm.Ecdsa;
            keySize = value.Contains("P384", StringComparison.Ordinal) ? 384 : 256;
            hash = keySize == 384 ? HashAlgorithmName.SHA384 : HashAlgorithmName.SHA256;
            return true;
        }

        return false;
    }

    public static void ValidateKeySize(SigningAlgorithm algorithm, int keySize)
    {
        bool valid = algorithm == SigningAlgorithm.Rsa
            ? keySize is 2048 or 3072 or 4096
            : keySize is 256 or 384;

        if (!valid)
            throw new ArgumentException($"Unsupported key size {keySize} for {algorithm}.");
    }

    private static ECCurve CurveFromKeySize(int keySize) => keySize switch
    {
        384 => ECCurve.NamedCurves.nistP384,
        _ => ECCurve.NamedCurves.nistP256
    };

    // ---------------------------------------------------------------- helpers

    private static void ImportPrivate(AsymmetricAlgorithm key, string encryptedPem, string password)
    {
        try
        {
            switch (key)
            {
                case RSA rsa:
                    rsa.ImportFromEncryptedPem(encryptedPem, password);
                    break;
                case ECDsa ecdsa:
                    ecdsa.ImportFromEncryptedPem(encryptedPem, password);
                    break;
            }
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException)
        {
            throw new CryptographicException(
                "The signing key could not be opened. The password is incorrect or the key file is damaged.", ex);
        }
    }

    private static byte[] PemBodyToDer(string pem)
    {
        if (string.IsNullOrWhiteSpace(pem)) throw new FormatException("The public key is empty.");

        var sb = new StringBuilder(pem.Length);
        foreach (string line in pem.Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith("-----", StringComparison.Ordinal)) continue;
            sb.Append(trimmed);
        }

        return Convert.FromBase64String(sb.ToString());
    }
}
