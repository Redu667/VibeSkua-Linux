using Skua.Core.Interfaces;

namespace Skua.Avalonia.Services;

/// <summary>
/// Minimal Linux <see cref="IScreenshotService"/>. Returns no image for now
/// (the game renders headless in Ruffle); real capture of the Ruffle surface is
/// a later refinement. Exists so consumers like DiscordWebhookService resolve.
/// </summary>
public sealed class ScreenshotService : IScreenshotService
{
    public Task<byte[]> TakeScreenshotAsync() => Task.FromResult(Array.Empty<byte>());
}
