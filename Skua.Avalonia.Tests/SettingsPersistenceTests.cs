using System;
using System.Collections.Generic;
using Skua.Core.Models;
using Skua.Core.Services;
using Xunit;

namespace Skua.Avalonia.Tests;

/// <summary>
/// Settings must survive a relaunch: a value Set through one
/// UnifiedSettingsService instance must come back from a fresh instance
/// (which re-reads ~/.config/Skua/Skua.settings.json). Regression coverage
/// for two silent-drop bugs: manager-section keys (ManagedAccounts) written
/// under the Client role, and Linux theme keys with no backing property —
/// both were discarded without error, losing accounts/themes on restart.
/// </summary>
public class SettingsPersistenceTests
{
    [Fact]
    public void Manager_section_keys_persist_even_under_client_role()
    {
        UnifiedSettingsService writer = new();
        writer.Initialize(AppRole.Client); // the pre-fix Linux default

        Dictionary<string, AccountData>? original = writer.Get<Dictionary<string, AccountData>>("ManagedAccounts");
        try
        {
            Dictionary<string, AccountData> accounts = new(original ?? new(), StringComparer.OrdinalIgnoreCase)
            {
                ["persist-test-user"] = new AccountData { DisplayName = "Persist Test", Password = "pw" },
            };
            writer.Set("ManagedAccounts", accounts);

            // Fresh instance = app relaunch.
            UnifiedSettingsService reader = new();
            reader.Initialize(AppRole.Client);
            Dictionary<string, AccountData>? roundTripped = reader.Get<Dictionary<string, AccountData>>("ManagedAccounts");

            Assert.NotNull(roundTripped);
            Assert.True(roundTripped!.ContainsKey("persist-test-user"), "account was not persisted across instances");
            Assert.Equal("Persist Test", roundTripped["persist-test-user"].DisplayName);
        }
        finally
        {
            writer.Set("ManagedAccounts", original ?? new Dictionary<string, AccountData>(StringComparer.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void Linux_theme_keys_persist_across_instances()
    {
        UnifiedSettingsService writer = new();
        writer.Initialize(AppRole.Manager);

        string? original = writer.Get<string>("LinuxCurrentTheme");
        try
        {
            writer.Set("LinuxCurrentTheme", "Persist Test|dark|#FF112233");

            UnifiedSettingsService reader = new();
            reader.Initialize(AppRole.Manager);

            Assert.Equal("Persist Test|dark|#FF112233", reader.Get<string>("LinuxCurrentTheme"));
        }
        finally
        {
            writer.Set("LinuxCurrentTheme", original ?? string.Empty);
        }
    }
}
