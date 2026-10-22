using System.Text;

namespace LicenseShared;

/// <summary>
/// Crockford base32: uppercase, no ambiguous characters, tolerant decoding
/// (O -> 0, I/L -> 1), suitable for license keys that may be pasted or typed.
/// </summary>
public static class Base32
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public static string Encode(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty) return string.Empty;

        var sb = new StringBuilder((data.Length * 8 + 4) / 5);
        int buffer = 0;
        int bits = 0;

        foreach (byte value in data)
        {
            buffer = (buffer << 8) | value;
            bits += 8;

            while (bits >= 5)
            {
                sb.Append(Alphabet[(buffer >> (bits - 5)) & 0x1F]);
                bits -= 5;
            }
        }

        if (bits > 0)
            sb.Append(Alphabet[(buffer << (5 - bits)) & 0x1F]);

        return sb.ToString();
    }

    public static byte[] Decode(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<byte>();

        var output = new List<byte>(text.Length * 5 / 8);
        int buffer = 0;
        int bits = 0;

        foreach (char raw in text)
        {
            char c = char.ToUpperInvariant(raw);
            c = c switch
            {
                'O' => '0',
                'I' => '1',
                'L' => '1',
                _ => c
            };

            int value = Alphabet.IndexOf(c);
            if (value < 0)
                throw new FormatException($"Invalid character '{raw}' in the license key.");

            buffer = (buffer << 5) | value;
            bits += 5;

            if (bits >= 8)
            {
                output.Add((byte)((buffer >> (bits - 8)) & 0xFF));
                bits -= 8;
            }
        }

        return output.ToArray();
    }
}
