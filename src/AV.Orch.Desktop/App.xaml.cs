using System.Windows;
using AV.Orch.Desktop.Services;

namespace AV.Orch.Desktop;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var composition = DesktopServiceFactory.Create();
        var settings = composition.SettingsService.Load();
        composition.Logger.PurgeOldLogs(settings.LogRetentionDays);

        // Support silent installer switch: AVOrch.exe --install-ribbon
        if (e.Args.Any(a => string.Equals(a, "--install-ribbon", StringComparison.OrdinalIgnoreCase)))
        {
            await composition.RibbonInstaller.InstallOrUpdateRibbonAsync(settings.CustomPyRevitPath);
            Shutdown(0);
            return;
        }

        // Support silent uninstaller cleanup switch: AVOrch.exe --uninstall-cleanup
        if (e.Args.Any(a => string.Equals(a, "--uninstall-cleanup", StringComparison.OrdinalIgnoreCase)))
        {
            await composition.UninstallCleanupService.RemoveAllAvOrchRegistrationsAsync();
            Shutdown(0);
            return;
        }

        ApplyTheme(settings.DarkTheme);
        composition.MainViewModel.ThemeChanged += ApplyTheme;

        var window = new MainWindow(composition.MainViewModel);
        MainWindow = window;
        window.Show();

        await composition.MainViewModel.RefreshAsync();
    }

    public void ApplyTheme(bool darkTheme)
    {
        var themeUri = new Uri(darkTheme ? "Themes/DarkTheme.xaml" : "Themes/LightTheme.xaml", UriKind.Relative);
        var dicts = Resources.MergedDictionaries;
        if (dicts.Count > 0)
        {
            dicts[0] = new ResourceDictionary { Source = themeUri };
        }
        else
        {
            dicts.Add(new ResourceDictionary { Source = themeUri });
        }
    }
}
