using System.IO.Compression;
using System.Text;

namespace LicenseShared;

/// <summary>
/// Compact binary codec for license payloads and activation codes.
/// The signed bytes are the binary payload, not the JSON representation, which keeps
/// activation codes short enough to paste into the client.
/// </summary>
public static class LicenseCodec
{
    private const byte PayloadVersion = 1;
    private const byte BlobVersion = 1;
    private const string CodePrefix = "WLC1";
    private const int GroupSize = 5;
    private const int MaxStringBytes = 4096;
    private const int MaxDecompressedBytes = 64 * 1024;

    private static ReadOnlySpan<byte> BlobMagic => "WLC1"u8;

    // ---------------------------------------------------------------- payload bytes

    public static byte[] SerializePayload(LicensePayload payload)
    {
        using var stream = new MemoryStream(256);
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(PayloadVersion);
            WriteString(writer, payload.LicenseId);
            WriteString(writer, payload.ProductId);
            WriteString(writer, payload.ProductName);
            WriteString(writer, payload.CustomerName);
            WriteString(writer, payload.StationName);
            WriteString(writer, payload.DeviceId);
            writer.Write((byte)payload.BindingMode);
            writer.Write((byte)payload.LicenseType);
            writer.Write(payload.IssuedAtUtc.UtcTicks);
            writer.Write(payload.NotBeforeUtc.UtcTicks);
            writer.Write(payload.ExpiresAtUtc.UtcTicks);
            WriteString(writer, payload.KeyId);
        }

        return stream.ToArray();
    }

    public static LicensePayload DeserializePayload(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is < 8 or > MaxDecompressedBytes)
            throw new FormatException("License payload has an invalid size.");

        using var stream = new MemoryStream(bytes.ToArray(), writable: false);
        using var reader = new BinaryReader(stream, Encoding.UTF8);

        byte version = reader.ReadByte();
        if (version != PayloadVersion)
            throw new FormatException($"Unsupported license payload version {version}.");

        var payload = new LicensePayload
        {
            SchemaVersion = version,
            LicenseId = ReadString(reader),
            ProductId = ReadString(reader),
            ProductName = ReadString(reader),
            CustomerName = ReadString(reader),
            StationName = ReadString(reader),
            DeviceId = ReadString(reader)
        };

        payload.BindingMode = (DeviceBindingMode)reader.ReadByte();
        payload.LicenseType = (LicenseType)reader.ReadByte();
        payload.IssuedAtUtc = new DateTimeOffset(reader.ReadInt64(), TimeSpan.Zero);
        payload.NotBeforeUtc = new DateTimeOffset(reader.ReadInt64(), TimeSpan.Zero);
        payload.ExpiresAtUtc = new DateTimeOffset(reader.ReadInt64(), TimeSpan.Zero);
        payload.KeyId = ReadString(reader);

        if (stream.Position != stream.Length)
            throw new FormatException("License payload contains trailing data.");

        return payload;
    }

    // ---------------------------------------------------------------- activation code

    /// <summary>Builds a grouped, uppercase activation code from a signed license envelope.</summary>
    public static string CreateActivationCode(SignedLicense envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        byte[] payload = Convert.FromBase64String(envelope.PayloadBase64);
        byte[] signature = Convert.FromBase64String(envelope.SignatureBase64);
        byte[] keyId = Convert.FromHexString(envelope.KeyId);

        byte algorithmId = LicenseCrypto.AlgorithmId(envelope.Algorithm);

        using var rawStream = new MemoryStream(payload.Length + signature.Length + 24);
        using (var writer = new BinaryWriter(rawStream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(BlobMagic);
            writer.Write(BlobVersion);
            writer.Write(algorithmId);
            writer.Write((byte)keyId.Length);
            writer.Write(keyId);
            writer.Write((ushort)payload.Length);
            writer.Write(payload);
            writer.Write((ushort)signature.Length);
            writer.Write(signature);
        }

        using var compressedStream = new MemoryStream();
        using (var gzip = new GZipStream(compressedStream, CompressionLevel.Optimal, leaveOpen: true))
            gzip.Write(rawStream.ToArray());

        string body = Base32.Encode(compressedStream.ToArray());
        return FormatCode(body);
    }

    public static bool TryDecodeActivationCode(string? code, out SignedLicense? envelope, out string error)
    {
        envelope = null;
        error = "";

        if (string.IsNullOrWhiteSpace(code))
        {
            error = "Enter a license key.";
            return false;
        }

        var cleaned = new StringBuilder(code.Length);
        foreach (char c in code)
        {
            if (char.IsWhiteSpace(c) || c is '-' or '_') continue;
            cleaned.Append(c);
        }

        string text = cleaned.ToString();
        if (!text.StartsWith(CodePrefix, StringComparison.OrdinalIgnoreCase))
        {
            error = "This key does not start with 'WLC1'. Check that the whole key was pasted.";
            return false;
        }

        text = text[CodePrefix.Length..];

        byte[] compressed;
        try
        {
            compressed = Base32.Decode(text);
        }
        catch (FormatException ex)
        {
            error = ex.Message;
            return false;
        }

        if (compressed.Length is < 32 or > MaxDecompressedBytes)
        {
            error = "The license key has an invalid length.";
            return false;
        }

        try
        {
            using var input = new MemoryStream(compressed, writable: false);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            CopyWithLimit(gzip, output, MaxDecompressedBytes);
            envelope = ParseBlob(output.ToArray());
            return true;
        }
        catch (InvalidDataException)
        {
            error = "The license key is damaged or incomplete.";
            return false;
        }
        catch (Exception ex) when (ex is FormatException or EndOfStreamException or ArgumentException or OverflowException)
        {
            error = ex.Message;
            return false;
        }
    }

    private static SignedLicense ParseBlob(byte[] blob)
    {
        using var stream = new MemoryStream(blob, writable: false);
        using var reader = new BinaryReader(stream, Encoding.UTF8);

        var magic = reader.ReadBytes(4);
        if (!magic.AsSpan().SequenceEqual(BlobMagic))
            throw new FormatException("The license key is not a Warehouse license.");

        byte version = reader.ReadByte();
        if (version != BlobVersion)
            throw new FormatException($"Unsupported license key version {version}.");

        byte algorithmId = reader.ReadByte();
        byte keyIdLength = reader.ReadByte();
        if (keyIdLength is 0 or > 32)
            throw new FormatException("The license key has an invalid key id.");

        string keyId = Convert.ToHexString(reader.ReadBytes(keyIdLength));

        ushort payloadLength = reader.ReadUInt16();
        if (payloadLength is 0 or > MaxStringBytes)
            throw new FormatException("The license key has an invalid payload length.");
        byte[] payload = reader.ReadBytes(payloadLength);

        ushort signatureLength = reader.ReadUInt16();
        if (signatureLength is 0 or > 1024)
            throw new FormatException("The license key has an invalid signature length.");
        byte[] signature = reader.ReadBytes(signatureLength);

        if (stream.Position != stream.Length)
            throw new FormatException("The license key contains trailing data.");

        return new SignedLicense
        {
            Format = LicenseCrypto.Format,
            Algorithm = LicenseCrypto.AlgorithmLabel(algorithmId),
            KeyId = keyId,
            PayloadBase64 = Convert.ToBase64String(payload),
            SignatureBase64 = Convert.ToBase64String(signature)
        };
    }

    private static string FormatCode(string body)
    {
        var sb = new StringBuilder(body.Length + body.Length / GroupSize + 8);
        sb.Append(CodePrefix);

        for (int i = 0; i < body.Length; i += GroupSize)
        {
            sb.Append('-');
            sb.Append(body, i, Math.Min(GroupSize, body.Length - i));
        }

        return sb.ToString();
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>Parses either a signed license JSON file or a WLC1 activation code.</summary>
    public static bool TryParseLicenseText(string? text, out SignedLicense? envelope, out string error)
    {
        envelope = null;
        error = "";

        if (string.IsNullOrWhiteSpace(text))
        {
            error = "Enter a license key or select a license file.";
            return false;
        }

        string trimmed = text.Trim();
        if (trimmed.StartsWith('{'))
            return LicenseCrypto.TryDeserializeEnvelope(trimmed, out envelope, out error);

        return TryDecodeActivationCode(trimmed, out envelope, out error);
    }

    private static void WriteString(BinaryWriter writer, string? value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
        if (bytes.Length > MaxStringBytes)
            throw new ArgumentException("A license field is too long.");
        writer.Write((ushort)bytes.Length);
        writer.Write(bytes);
    }

    private static string ReadString(BinaryReader reader)
    {
        ushort length = reader.ReadUInt16();
        if (length > MaxStringBytes)
            throw new FormatException("A license field is too long.");
        return Encoding.UTF8.GetString(reader.ReadBytes(length));
    }

    private static void CopyWithLimit(Stream source, Stream destination, int limit)
    {
        byte[] buffer = new byte[8192];
        int total = 0;
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += read;
            if (total > limit)
                throw new FormatException("The license key expands beyond the allowed size.");
            destination.Write(buffer, 0, read);
        }
    }
}
