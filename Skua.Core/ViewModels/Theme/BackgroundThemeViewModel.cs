using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Skua.Core.Interfaces;
using Skua.Core.Models;
using Skua.Core.Services;
using Skua.Core.Utils;

namespace Skua.Core.ViewModels;

public class BackgroundThemeViewModel : ObservableObject
{
    private readonly BackgroundThemeService _backgroundService;
    private readonly IFileDialogService _fileDialogService;

    public BackgroundThemeViewModel(BackgroundThemeService backgroundService, IFileDialogService fileDialogService)
    {
        _backgroundService = backgroundService;
        _fileDialogService = fileDialogService;
        BrowseBackgroundCommand = new AsyncRelayCommand(BrowseBackgroundAsync);
        OpenThemesFolderCommand = new RelayCommand(OpenThemesFolder);
        RefreshBackgroundsCommand = new RelayCommand(() => OnPropertyChanged(nameof(AvailableBackgrounds)));
        GetBackgroundsCommand = new RelayCommand(GetBackgrounds);
    }

    public List<string> AvailableBackgrounds => _backgroundService.GetAvailableBackgrounds();

    public string SelectedBackground
    {
        get => _backgroundService.CurrentBackground;
        set
        {
            if (_backgroundService.CurrentBackground != value)
            {
                _backgroundService.CurrentBackground = value;
                OnPropertyChanged();
            }
        }
    }



    public IAsyncRelayCommand BrowseBackgroundCommand { get; }
    public IRelayCommand OpenThemesFolderCommand { get; }
    public IRelayCommand RefreshBackgroundsCommand { get; }
    public IRelayCommand GetBackgroundsCommand { get; }

    private async Task BrowseBackgroundAsync()
    {
        string? selectedFilePath = _fileDialogService.OpenFile(
            ClientFileSources.SkuaThemesDIR,
            "SWF files (*.swf)|*.swf|All files (*.*)|*.*");

        if (!string.IsNullOrEmpty(selectedFilePath))
        {
            string fileName = Path.GetFileName(selectedFilePath);
            string destinationPath = Path.Combine(ClientFileSources.SkuaThemesDIR, fileName);

            if (selectedFilePath != destinationPath)
            {
                try
                {
                    File.Copy(selectedFilePath, destinationPath, true);
                }
                catch (Exception)
                {
                    return;
                }
            }

            OnPropertyChanged(nameof(AvailableBackgrounds));
            SelectedBackground = fileName;
        }
    }


    private void OpenThemesFolder()
    {
        try
        {
            Directory.CreateDirectory(ClientFileSources.SkuaThemesDIR);
            // Open the folder in the platform file manager. explorer.exe is
            // Windows-only; on Linux use xdg-open, on macOS `open`.
            (string cmd, string args) = OperatingSystem.IsWindows()
                ? ("explorer.exe", ClientFileSources.SkuaThemesDIR)
                : OperatingSystem.IsMacOS()
                    ? ("open", ClientFileSources.SkuaThemesDIR)
                    : ("xdg-open", ClientFileSources.SkuaThemesDIR);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(cmd, args) { UseShellExecute = false });
        }
        catch (Exception)
        {
        }
    }

    private void GetBackgrounds()
    {
        try
        {
            Link.OpenBrowser("https://github.com/auqw/SkuaBackgrounds");
        }
        catch (Exception)
        {
        }
    }
}