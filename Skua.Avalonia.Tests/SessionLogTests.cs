using System;
using System.IO;
using Skua.Avalonia.Services;
using Xunit;

namespace Skua.Avalonia.Tests;

/// <summary>
/// Per-launch log routing: each launch gets its own timestamped files under
/// logs/, named by role (+ instance), published as env vars the native bridge
/// honors. Regression guard for the "logs grow to hundreds of thousands of
/// lines" report — every process no longer appends to the same four files.
/// </summary>
public class SessionLogTests
{
    [Fact]
    public void Init_routes_a_client_launch_to_its_own_timestamped_files()
    {
        DateTime now = new(2026, 7, 18, 3, 14, 22);
        SessionLog.Init(new[] { "--client", "--instance", "Bob Smith" }, now);

        string? clientPath = Environment.GetEnvironmentVariable(SessionLog.ClientVar);
        string? gamePath = Environment.GetEnvironmentVariable(SessionLog.GameVar);
        string? rufflePath = Environment.GetEnvironmentVariable(SessionLog.RuffleVar);

        Assert.NotNull(clientPath);
        Assert.NotNull(gamePath);
        Assert.NotNull(rufflePath);

        // Named by role + sanitized instance + date + time, in a logs/ folder.
        string name = Path.GetFileName(clientPath!);
        Assert.StartsWith("client-Bob_Smith-2026-07-18_031422-", name);
        Assert.EndsWith(".client.log", name);
        Assert.Equal("logs", Path.GetFileName(Path.GetDirectoryName(clientPath!)));

        // All three share the same per-launch stem (differ only by suffix).
        string stem = name[..name.IndexOf(".client.log", StringComparison.Ordinal)];
        Assert.EndsWith($"{stem}.game.log", gamePath!);
        Assert.EndsWith($"{stem}.ruffle.log", rufflePath!);

        // ResolveClientLog returns this launch's file.
        Assert.Equal(clientPath, SessionLog.ResolveClientLog());
    }

    [Fact]
    public void Manager_launch_is_labelled_manager()
    {
        SessionLog.Init(Array.Empty<string>(), new DateTime(2026, 7, 18, 9, 0, 0));
        string name = Path.GetFileName(Environment.GetEnvironmentVariable(SessionLog.ClientVar)!);
        Assert.StartsWith("manager-2026-07-18_090000-", name);
    }
}
