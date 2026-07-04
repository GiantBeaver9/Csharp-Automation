using DailySummary.Core.Compute;
using Xunit;

namespace DailySummary.Tests;

public class MagicPacketTests
{
    [Fact]
    public void ParseMac_AcceptsColonAndDash()
    {
        var colon = MagicPacket.ParseMac("AA:BB:CC:DD:EE:FF");
        var dash = MagicPacket.ParseMac("aa-bb-cc-dd-ee-ff");

        Assert.Equal(new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF }, colon);
        Assert.Equal(colon, dash); // case- and separator-insensitive
    }

    [Theory]
    [InlineData("AA:BB:CC:DD:EE")]        // too few octets
    [InlineData("AA:BB:CC:DD:EE:FF:00")]  // too many
    [InlineData("ZZ:BB:CC:DD:EE:FF")]     // non-hex
    [InlineData("")]                       // empty
    public void ParseMac_RejectsMalformed(string mac)
    {
        Assert.Throws<FormatException>(() => MagicPacket.ParseMac(mac));
    }

    [Fact]
    public void Build_HasSyncStreamThenMacTimesSixteen()
    {
        var packet = MagicPacket.Build("AA:BB:CC:DD:EE:FF");

        Assert.Equal(102, packet.Length);
        Assert.Equal(MagicPacket.Length, packet.Length);

        // First 6 bytes are the 0xFF sync stream.
        for (var i = 0; i < 6; i++) Assert.Equal(0xFF, packet[i]);

        // Then the MAC repeated 16 times.
        var mac = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF };
        for (var repeat = 0; repeat < 16; repeat++)
            for (var b = 0; b < 6; b++)
                Assert.Equal(mac[b], packet[6 + repeat * 6 + b]);
    }
}
