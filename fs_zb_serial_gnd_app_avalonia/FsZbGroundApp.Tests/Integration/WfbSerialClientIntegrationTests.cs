using System;
using System.Collections.Generic;
using System.Linq;
using FsZbGroundApp.Services;
using Xunit;

namespace FsZbGroundApp.Tests.Integration;

public class WfbSerialClientIntegrationTests
{
    [Fact]
    public void GetAvailablePorts_RecognizesExecutableCandidateVidPidSet()
    {
        using var client = new WfbSerialClient(
            () => new[] { "COM7", "COM9", "COM10" },
            () => new Dictionary<string, WfbUsbDeviceMetadata>(StringComparer.OrdinalIgnoreCase)
            {
                ["COM7"] = new("0bda:881a", "USB\\VID_0BDA&PID_881A\\A1", "RTL Device A"),
                ["COM9"] = new("2b89:0043", "USB\\VID_2B89&PID_0043\\B2", "RTL Device B"),
                ["COM10"] = new("0bda:8812", "USB\\VID_0BDA&PID_8812\\Legacy", "Legacy Device")
            });

        var devices = client.GetAvailablePorts();

        Assert.Equal(3, devices.Count);

        var com7 = devices.Single(d => d.PortName == "COM7");
        var com9 = devices.Single(d => d.PortName == "COM9");
        var com10 = devices.Single(d => d.PortName == "COM10");

        Assert.True(com7.IsRtl8812AuCompatible);
        Assert.True(com9.IsRtl8812AuCompatible);
        Assert.False(com10.IsRtl8812AuCompatible);

        Assert.Equal("0bda:881a", com7.VidPid, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("2b89:0043", com9.VidPid, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("USB\\VID_0BDA&PID_881A\\A1", com7.Address);
        Assert.Equal("USB\\VID_2B89&PID_0043\\B2", com9.Address);
        Assert.True(com7.SupportsSerialConnection);
        Assert.True(com9.SupportsSerialConnection);
    }

    [Fact]
    public void GetAvailablePorts_UsesUnknownFallbackWhenMetadataMissing()
    {
        using var client = new WfbSerialClient(
            () => new[] { "COM3" },
            () => new Dictionary<string, WfbUsbDeviceMetadata>(StringComparer.OrdinalIgnoreCase));

        var devices = client.GetAvailablePorts();

        var item = Assert.Single(devices);
        Assert.Equal("COM3", item.PortName);
        Assert.Equal("UNKNOWN", item.VidPid);
        Assert.Equal("-", item.Address);
        Assert.False(item.IsRtl8812AuCompatible);
        Assert.True(item.SupportsSerialConnection);
    }

    [Fact]
    public void GetAvailablePorts_IncludesKnownCandidatesWithoutComTransport()
    {
        using var client = new WfbSerialClient(
            () => new[] { "COM3" },
            () => new Dictionary<string, WfbUsbDeviceMetadata>(StringComparer.OrdinalIgnoreCase)
            {
                ["COM3"] = new("1a86:7523", "USB\\VID_1A86&PID_7523\\Bridge", "Bridge Port")
            },
            () => new[]
            {
                new WfbUsbDeviceMetadata("0bda:881a", "USB\\VID_0BDA&PID_881A&MI_00\\A1", "RTL Candidate A"),
                new WfbUsbDeviceMetadata("2b89:0043", "USB\\VID_2B89&PID_0043&MI_00\\B2", "RTL Candidate B")
            });

        var devices = client.GetAvailablePorts();

        Assert.Equal(3, devices.Count);

        var rtlA = devices.Single(d => d.VidPid.Equals("0bda:881a", StringComparison.OrdinalIgnoreCase));
        var rtlB = devices.Single(d => d.VidPid.Equals("2b89:0043", StringComparison.OrdinalIgnoreCase));

        Assert.True(rtlA.IsRtl8812AuCompatible);
        Assert.True(rtlB.IsRtl8812AuCompatible);
        Assert.False(rtlA.SupportsSerialConnection);
        Assert.False(rtlB.SupportsSerialConnection);
        Assert.Equal("USB\\VID_0BDA&PID_881A&MI_00\\A1", rtlA.Address);
        Assert.Equal("USB\\VID_2B89&PID_0043&MI_00\\B2", rtlB.Address);
    }
}
