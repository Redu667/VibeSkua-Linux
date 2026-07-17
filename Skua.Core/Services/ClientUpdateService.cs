using CommunityToolkit.Mvvm.Messaging;
using Newtonsoft.Json;
using Skua.Core.Interfaces;
using Skua.Core.Messaging;
using Skua.Core.Models.GitHub;
using Skua.Core.Utils;
using System.Diagnostics;
using System.IO.Compression;

namespace Skua.Core.Services;

public class ClientUpdateService : IClientUpdateService
{
    private readonly ISettingsService _settingsService;
    private readonly IDialogService _dialogService;

    public ClientUpdateService(ISettingsService settingsService, IDialogService dialogService)
    {
        _settingsService = settingsService;
        _dialogService = dialogService;
    }

    public List<UpdateInfo> Releases { get; set; } = new();

    public async Task GetReleasesAsync()
    {
        try
        {
            string releases = await ValidatedHttpExtensions.GetStringAsync(HttpClients.GitHubRaw, "auqw/Skua/refs/heads/master/releases.json");
            List<UpdateInfo>? releaseList = JsonConvert.DeserializeObject<List<UpdateInfo>>(releases);
            if (releaseList is null || releaseList.Count == 0)
                return;

            Releases.Clear();
            Releases = releaseList.OrderByDescending(r => r.ParsedVersion).ToList();
        }
        catch
        {
            // Silently fail - UI will show no releases available
        }
    }

    public async Task DownloadUpdateAsync(IProgress<string>? progress, UpdateInfo info)
    {
        try
        {
            progress?.Report("Downloading...");
            string? downloadUrl = Environment.Is64BitOperatingSystem ? info.Assets.FirstOrDefault(a => a.BrowserUrl!.Contains("x64"))?.BrowserUrl : info.Assets.FirstOrDefault(a => a.BrowserUrl!.Contains("x86"))?.BrowserUrl;

            string? fileName = downloadUrl!.Split('/').Last();

            byte[] file = await HttpClients.Default.GetByteArrayAsync(downloadUrl);

            progress?.Report("Writing to folder...");
            string path = _settingsService.Get("ClientDownloadPath", string.Empty);
            if (string.IsNullOrEmpty(path) && !AppDomain.CurrentDomain.BaseDirectory.Contains("Program Files"))
                path = Directory.GetParent(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))?.FullName ?? AppContext.BaseDirectory;

            string filePath = Path.Combine(path, fileName);
            await File.WriteAllBytesAsync(filePath, file);
            string extension = Path.GetExtension(filePath);
            if (extension is ".msi" or ".exe")
            {
                string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                ProcessStartInfo startInfo = new(Path.Combine(winDir, @"System32\msiexec.exe"),
                    $"/i {filePath} /quiet /passive /qb!- /norestart ALLUSERS=1")
                {
                    Verb = "runas",
                    UseShellExecute = true
                };
                Process? proc = Process.Start(startInfo);
                await proc!.WaitForExitAsync();
                if (proc.ExitCode == 0)
                {
                    _settingsService.Set("ChangeLogActivated", false);
                    string startMenuPath = AppContext.BaseDirectory;
                    string appPath = Path.Combine(startMenuPath, "Skua.Manager.exe");
                    Process.Start(appPath);
                    Environment.Exit(0);
                }
            }
            else
            {
                string updateFolder = Path.Combine(path, fileName.Replace(".zip", string.Empty));
                if (Directory.Exists(updateFolder))
                    Directory.Delete(updateFolder, true);

                progress?.Report("Extracting files...");
                ZipFile.ExtractToDirectory(filePath, updateFolder);

                if (_settingsService.Get<bool>("DeleteZipFileAfter"))
                    File.Delete(filePath);

                progress?.Report("Checking for Skua Manager...");
                if (File.Exists(Path.Combine(updateFolder, "Skua.Manager.exe")))
                {
                    progress?.Report("Waiting for services shutdown...");
                    if (!await StrongReferenceMessenger.Default.Send<UpdateStartedMessage>())
                    {
                        progress?.Report("Something went wrong finishing services");
                        return;
                    }

                    progress?.Report("Starting updated version...");

                    Process.Start(Path.Combine(path, updateFolder, "Skua.Manager.exe"));
                    //StrongReferenceMessenger.Default.Send<UpdateFinishedMessage>();
                    return;
                }
            }
        }
        catch (Exception e)
        {
            progress?.Report("Error while updating.");
            _dialogService.ShowMessageBox($"Error Message:\r\n{e.Message}", "Update Error");
            return;
        }
        progress?.Report("Failed to start downloaded version");
    }
}