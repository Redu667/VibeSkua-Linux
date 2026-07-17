using System;
using System.IO;
using System.Threading;
using Skua.Avalonia.Services;
using Xunit;

namespace Skua.Avalonia.Tests;

/// <summary>
/// The Unix-domain-socket army bus: a broadcast reaches every listener in the
/// shared directory (self included, matching the Windows WM broadcast) with the
/// (msg, wParam, lParam) triple intact, and stale sockets don't break it.
/// </summary>
public class ArmyBusTests
{
    [Fact]
    public void Broadcast_delivers_the_message_triple_to_listeners()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"skua-army-test-{Guid.NewGuid():N}");
        using ArmyBus bus = new(dir);

        using ManualResetEventSlim received = new();
        (int msg, int w, int l) got = default;
        bus.MessageReceived += (msg, w, l) => { got = (msg, w, l); received.Set(); };

        bus.Broadcast(0x0400 + 450, 99, 1);

        Assert.True(received.Wait(TimeSpan.FromSeconds(5)), "message was not delivered");
        Assert.Equal(0x0400 + 450, got.msg);
        Assert.Equal(99, got.w);
        Assert.Equal(1, got.l);
    }

    [Fact]
    public void Broadcast_survives_and_cleans_up_stale_sockets()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"skua-army-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        string stale = Path.Combine(dir, "99999999.sock");
        File.WriteAllText(stale, ""); // plain file: connect fails like a dead listener

        using ArmyBus bus = new(dir);
        using ManualResetEventSlim received = new();
        bus.MessageReceived += (_, _, _) => received.Set();

        ArmyBus.Broadcast(dir, 0x0400 + 447, 0, 0);

        Assert.True(received.Wait(TimeSpan.FromSeconds(5)), "live listener should still get the message");
        Assert.False(File.Exists(stale), "stale socket should be removed");
    }
}
