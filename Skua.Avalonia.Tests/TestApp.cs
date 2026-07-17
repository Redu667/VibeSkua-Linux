using Avalonia;
using Avalonia.Headless;
using Skua.Avalonia;
using Skua.Avalonia.Tests;

[assembly: AvaloniaTestApplication(typeof(TestApp))]

namespace Skua.Avalonia.Tests;

/// <summary>
/// Headless AppBuilder for the UI tests — the real <see cref="App"/> (styles,
/// ViewLocator, DI) on Avalonia's headless platform, so tests run with no display.
/// </summary>
public static class TestApp
{
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
