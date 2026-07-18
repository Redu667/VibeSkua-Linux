using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Threading.Tasks;
using Velopack;
using Velopack.Locators;
using Velopack.Sources;
using System.Diagnostics;
using System;
using Skua.Core.Interfaces;

namespace Skua.Core.ViewModels.Manager;

public partial class AppUpdaterViewModel : ObservableObject
{
    // The Linux AppImage releases live in this fork's repo, NOT the Windows
    // upstream (NinjaXz/VibeSkua) — pointing at upstream meant the updater
    // never found a Linux release. The releases are published on the "linux"
    // Velopack channel (releases.linux.json), so pin that explicitly.
    private const string ReleaseRepoUrl = "https://github.com/Redu667/VibeSkua-Linux";
    private const string ReleaseChannel = "linux";

    private readonly IDispatcherService _dispatcherService;
    private readonly ISettingsService? _settingsService;

    public AppUpdaterViewModel(IDispatcherService dispatcherService, ISettingsService? settingsService = null)
    {
        _dispatcherService = dispatcherService;
        _settingsService = settingsService;
    }

    /// <summary>The release repo is private, so read releases with the user's
    /// stored GitHub token (GitHub Auth tab) when present; null = anonymous
    /// (works only if the repo is public).</summary>
    private GithubSource CreateSource()
    {
        string? token = _settingsService?.Get<string>("UserGitHubToken");
        if (string.IsNullOrWhiteSpace(token))
            token = null;
        return new GithubSource(ReleaseRepoUrl, token, false);
    }

    private UpdateManager CreateManager()
    {
        var locator = VelopackLocator.CreateDefaultForPlatform(null, null);
        return new UpdateManager(CreateSource(), new UpdateOptions { ExplicitChannel = ReleaseChannel }, locator);
    }

    [ObservableProperty]
    private string _updateStatus = "Ready to check for updates.";

    [ObservableProperty]
    private bool _isChecking;

    [ObservableProperty]
    private bool _updateAvailable;

    [ObservableProperty]
    private int _progressValue;

    private UpdateInfo? _updateInfo;
    private UpdateManager? _updateManager;

    public async Task CheckForUpdateAsync()
    {
        _dispatcherService.Invoke(() =>
        {
            IsChecking = true;
            UpdateStatus = "Checking for updates...";
            ProgressValue = 0;
        });
        try
        {
            _updateManager = CreateManager();
            _updateInfo = await _updateManager.CheckForUpdatesAsync();

            _dispatcherService.Invoke(() =>
            {
                if (_updateInfo == null)
                {
                    UpdateStatus = "You are up to date!";
                    UpdateAvailable = false;
                }
                else
                {
                    UpdateStatus = $"Update {_updateInfo.TargetFullRelease.Version} available!";
                    UpdateAvailable = true;
                }
            });
        }
        catch (Exception ex)
        {
            _dispatcherService.Invoke(() =>
            {
                UpdateStatus = $"Error checking updates: {ex.Message}";
                UpdateAvailable = false;
            });
        }
        finally
        {
            _dispatcherService.Invoke(() => IsChecking = false);
        }
    }

    [RelayCommand]
    private async Task CheckForUpdate()
    {
        await CheckForUpdateAsync();
    }

    public async Task DownloadAndInstallAsync()
    {
        if (_updateInfo == null) return;

        _dispatcherService.Invoke(() =>
        {
            IsChecking = true;
            UpdateStatus = "Downloading update...";
        });
        try
        {
            _updateManager ??= CreateManager();

            Action<int> progressObj = (progress) => 
            {
                _dispatcherService.Invoke(() =>
                {
                    ProgressValue = progress;
                    UpdateStatus = $"Downloading... {progress}%";
                });
            };

            await _updateManager.DownloadUpdatesAsync(_updateInfo, progressObj);

            _dispatcherService.Invoke(() => UpdateStatus = "Installing update and restarting...");
            Task.Run(() => _updateManager.ApplyUpdatesAndRestart(_updateInfo));
        }
        catch (Exception ex)
        {
            _dispatcherService.Invoke(() => UpdateStatus = $"Error updating: {ex.Message}");
        }
        finally
        {
            _dispatcherService.Invoke(() => IsChecking = false);
        }
    }

    [RelayCommand]
    private async Task DownloadAndInstall()
    {
        await DownloadAndInstallAsync();
    }
}
