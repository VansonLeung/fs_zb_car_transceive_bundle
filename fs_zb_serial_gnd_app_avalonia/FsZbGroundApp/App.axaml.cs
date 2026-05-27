using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using Avalonia.Markup.Xaml;
using FsZbGroundApp.Services;
using FsZbGroundApp.ViewModels;
using FsZbGroundApp.Views;

namespace FsZbGroundApp;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var launchOptions = AppLaunchOptions.Parse(Environment.GetCommandLineArgs());

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainViewModel = new MainViewModel(launchOptions);

            desktop.MainWindow = new MainWindow
            {
                DataContext = mainViewModel
            };

            desktop.MainWindow.Opened += async (_, _) =>
            {
                await mainViewModel.InitializeAsync();

                if (launchOptions.AutoStartWfb)
                {
                    var success = await mainViewModel.RunStartupAutomationAsync(launchOptions);
                    if (launchOptions.ExitAfterAutomation)
                    {
                        desktop.Shutdown(success ? 0 : 1);
                    }
                }
            };

            desktop.Exit += (_, _) => mainViewModel.Dispose();
        }
        else if (ApplicationLifetime is IActivityApplicationLifetime singleViewFactoryApplicationLifetime)
        {
            singleViewFactoryApplicationLifetime.MainViewFactory = () => new MainView { DataContext = new MainViewModel(launchOptions) };
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            singleViewPlatform.MainView = new MainView
            {
                DataContext = new MainViewModel(launchOptions)
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}