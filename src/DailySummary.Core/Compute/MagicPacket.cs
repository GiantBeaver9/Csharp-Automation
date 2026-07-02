using System.Globalization;

namespace DailySummary.Core.Compute;

/// <summary>
/// Wake-on-LAN magic packet: 6 bytes of 0xFF followed by the target MAC repeated 16 times (102 bytes).
/// Pure and dependency-free so it's unit-testable; the durable activity just broadcasts the bytes.
/// </summary>
public static class MagicPacket
{
    public const int Length = 6 + 16 * 6;

    /// <summary>Parse "AA:BB:CC:DD:EE:FF" or "AA-BB-..." into 6 bytes.</summary>
    public static byte[] ParseMac(string mac)
    {
        if (string.IsNullOrWhiteSpace(mac))
            throw new FormatException("MAC address is empty.");

        var parts = mac.Split(new[] { ':', '-' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 6)
            throw new FormatException($"Invalid MAC address '{mac}' (expected 6 octets).");

        var bytes = new byte[6];
        for (var i = 0; i < 6; i++)
        {
            if (!byte.TryParse(parts[i], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out bytes[i]))
                throw new FormatException($"Invalid MAC octet '{parts[i]}' in '{mac}'.");
        }
        return bytes;
    }

    /// <summary>Build the 102-byte magic packet for the given MAC string.</summary>
    public static byte[] Build(string mac) => Build(ParseMac(mac));

    /// <summary>Build the 102-byte magic packet for the given 6-byte MAC.</summary>
    public static byte[] Build(byte[] mac)
    {
        if (mac is not { Length: 6 })
            throw new ArgumentException("MAC must be 6 bytes.", nameof(mac));

        var packet = new byte[Length];
        for (var i = 0; i < 6; i++) packet[i] = 0xFF;
        for (var repeat = 0; repeat < 16; repeat++) Array.Copy(mac, 0, packet, 6 + repeat * 6, 6);
        return packet;
    }
}
