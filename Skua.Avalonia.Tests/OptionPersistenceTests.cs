using System;
using System.IO;
using Skua.Core.Options;
using Xunit;

namespace Skua.Avalonia.Tests;

/// <summary>
/// Script options (e.g. FarmerJoeDoAll's "Skip this window next time") must
/// persist between sessions. They are written by <see cref="OptionContainer.Save"/>
/// to <c>~/.config/Skua/options/{Storage}.cfg</c>. The Avalonia port never called
/// <c>IClientFilesService.CreateDirectories()</c> at startup (WPF does), so that
/// directory didn't exist and <c>File.WriteAllLines</c> threw a
/// <c>DirectoryNotFoundException</c> that silently dropped the value — the option
/// dialog kept reappearing. Save now ensures the directory exists.
/// </summary>
public class OptionPersistenceTests
{
    [Fact]
    public void Save_creates_missing_options_directory_and_value_survives_reload()
    {
        // A fresh, never-created nested directory, mirroring ~/.config/Skua/options
        // when CreateDirectories was never invoked.
        string dir = Path.Combine(Path.GetTempPath(), "skua-opt-test-" + Guid.NewGuid().ToString("N"));
        string file = Path.Combine(dir, "default.cfg");
        Assert.False(Directory.Exists(dir));

        try
        {
            // Session 1: check "Skip this window next time" and save.
            var container = new OptionContainer(null!) { OptionsFile = file };
            container.Options.Add(new Option<bool>("skipOptions", "Skip this window next time", "", false));
            container.SetDefaults();
            container.Set("skipOptions", true); // triggers Save()

            Assert.True(File.Exists(file)); // before the fix, Save threw here and lost it

            // Session 2: a fresh container loads the persisted value.
            var reloaded = new OptionContainer(null!) { OptionsFile = file };
            reloaded.Options.Add(new Option<bool>("skipOptions", "Skip this window next time", "", false));
            reloaded.SetDefaults();
            reloaded.Load();

            Assert.True(reloaded.Get<bool>("skipOptions"));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
    }
}
