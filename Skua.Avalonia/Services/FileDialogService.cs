using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Skua.Core.Interfaces;

namespace Skua.Avalonia.Services;

/// <summary>
/// Linux <see cref="IFileDialogService"/> backed by Avalonia's async
/// <c>StorageProvider</c>. The Skua.Core contract is synchronous (it returns a
/// path), so each call runs the async picker and pumps a nested dispatcher frame
/// until it resolves — the same shape WPF's blocking <c>ShowDialog</c> has. Skua
/// filter strings use the WPF form <c>"Skua Scripts (*.cs)|*.cs"</c>, parsed here
/// into <see cref="FilePickerFileType"/>s.
/// </summary>
public sealed class FileDialogService : IFileDialogService
{
    public string? OpenFile() => OpenFile(string.Empty);

    public string? OpenFile(string filters) => OpenFile(string.Empty, filters);

    public string? OpenFile(string initialDirectory, string filters) => RunSync(async () =>
    {
        TopLevel? top = ActiveTopLevel();
        if (top is null)
            return null;
        var result = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            FileTypeFilter = ParseFilters(filters),
            SuggestedStartLocation = await FolderFrom(top, initialDirectory),
        });
        return result.Count > 0 ? result[0].TryGetLocalPath() : null;
    });

    public string? OpenFolder() => OpenFolder(string.Empty);

    public string? OpenFolder(string initialDirectory) => RunSync(async () =>
    {
        TopLevel? top = ActiveTopLevel();
        if (top is null)
            return null;
        var result = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            AllowMultiple = false,
            SuggestedStartLocation = await FolderFrom(top, initialDirectory),
        });
        return result.Count > 0 ? result[0].TryGetLocalPath() : null;
    });

    public IEnumerable<string>? OpenText()
    {
        string? path = OpenFile();
        if (path is null || !File.Exists(path))
            return null;
        try { return File.ReadAllLines(path); }
        catch { return null; }
    }

    public string? Save() => Save(string.Empty);

    public string? Save(string filters) => Save(string.Empty, filters);

    public string? Save(string initialDirectory, string filters) => RunSync(async () =>
    {
        TopLevel? top = ActiveTopLevel();
        if (top is null)
            return null;
        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            FileTypeChoices = ParseFilters(filters),
            SuggestedStartLocation = await FolderFrom(top, initialDirectory),
        });
        return file?.TryGetLocalPath();
    });

    public void SaveText(string contents)
    {
        string? path = Save();
        if (path is not null)
            try { File.WriteAllText(path, contents); } catch { }
    }

    public void SaveText(IEnumerable<string> contents)
    {
        string? path = Save();
        if (path is not null)
            try { File.WriteAllLines(path, contents); } catch { }
    }

    // --- helpers ------------------------------------------------------------

    /// <summary>The active (or main) window as a <see cref="TopLevel"/> to host the picker.</summary>
    private static TopLevel? ActiveTopLevel()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return null;
        Window? window = desktop.Windows.FirstOrDefault(w => w.IsActive)
                         ?? desktop.MainWindow
                         ?? desktop.Windows.FirstOrDefault();
        return window;
    }

    private static async Task<IStorageFolder?> FolderFrom(TopLevel top, string dir)
    {
        if (string.IsNullOrWhiteSpace(dir))
            return null;
        try
        {
            return await top.StorageProvider.TryGetFolderFromPathAsync(dir);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Parse a WPF-style filter string into Avalonia file types.</summary>
    private static IReadOnlyList<FilePickerFileType> ParseFilters(string filters)
    {
        if (string.IsNullOrWhiteSpace(filters))
            return new[] { FilePickerFileTypes.All };

        // "Name (*.ext)|*.ext;*.ext2|Other|*.foo"
        string[] parts = filters.Split('|');
        List<FilePickerFileType> types = new();
        for (int i = 0; i + 1 < parts.Length; i += 2)
        {
            string name = parts[i].Trim();
            string[] patterns = parts[i + 1].Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .Where(p => p.Length > 0)
                .ToArray();
            types.Add(new FilePickerFileType(name) { Patterns = patterns });
        }
        if (types.Count == 0)
            types.Add(FilePickerFileTypes.All);
        return types;
    }

    /// <summary>
    /// Run an async picker to completion synchronously. On the UI thread, pump a
    /// nested dispatcher frame (so the picker's own async continuations run); off
    /// it, marshal to the UI thread and block-wait (the UI thread stays pumping).
    /// </summary>
    private static string? RunSync(Func<Task<string?>> func)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            string? result = null;
            var frame = new DispatcherFrame();
            _ = Dispatcher.UIThread.InvokeAsync(async () =>
            {
                try { result = await func(); }
                catch (Exception ex) { Console.Error.WriteLine($"file dialog failed: {ex}"); }
                finally { frame.Continue = false; }
            });
            Dispatcher.UIThread.PushFrame(frame);
            return result;
        }

        try
        {
            return Dispatcher.UIThread.InvokeAsync(func).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"file dialog failed: {ex}");
            return null;
        }
    }
}
