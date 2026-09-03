using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using MateViewGuardian.Core;
using MateViewGuardian.Platform;
using MateViewGuardian.Platform.Mac;
using MateViewGuardian.Platform.Windows;

namespace MateViewGuardian.App;

public sealed partial class App : Application
{
    private MainWindow? mainWindow;
    private MainWindowViewModel? viewModel;
    private TrayIcon? trayIcon;
    private NativeMenuItem? protectionItem;
    private NativeMenuItem? startupItem;
    private bool isQuitting;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var coordinator = CreateCoordinator();
            viewModel = new MainWindowViewModel(coordinator);
            mainWindow = new MainWindow(viewModel, () => isQuitting);
            desktop.MainWindow = mainWindow;
            CreateTray(desktop);
            _ = InitializeRuntimeAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async Task InitializeRuntimeAsync()
    {
        if (viewModel is null || mainWindow is null)
        {
            return;
        }

        await viewModel.InitializeAsync();
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            mainWindow.SynchronizeControls();
            UpdateTray();
            if (!Program.StartHidden)
            {
                ShowSettings();
            }
        });
        await viewModel.StartAsync();
    }

    private ProtectionCoordinator CreateCoordinator()
    {
        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MateViewGuardian");
        var settings = new JsonSettingsStore(Path.Combine(dataDirectory, "settings.json"));
        IPlatformProtection platform;
        if (OperatingSystem.IsMacOS())
        {
            platform = new MacProtection(
                new ProcessRunner(),
                "/usr/bin/hidutil",
                FindResource("ASDDC"));
        }
        else if (OperatingSystem.IsWindows())
        {
            var hid = new WindowsHidProtection(
                new ProcessRunner(),
                new ElevatedProcessRunner(),
                "powershell.exe",
                FindResource(Path.Combine("platform-tools", "windows", "MateViewHid.ps1")));
            platform = new WindowsProtection(new WindowsMonitorApi(), hid);
        }
        else
        {
            throw new PlatformNotSupportedException("MateView Guardian supports macOS and Windows.");
        }

        return new ProtectionCoordinator(platform, settings);
    }

    private void CreateTray(IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (viewModel is null)
        {
            return;
        }

        var menu = new NativeMenu();
        var show = new NativeMenuItem("Show Settings");
        show.Click += (_, _) => ShowSettings();
        menu.Items.Add(show);

        protectionItem = new NativeMenuItem("Protection")
        {
            ToggleType = MenuItemToggleType.CheckBox,
        };
        protectionItem.Click += (_, _) => _ = ToggleProtectionFromTrayAsync();
        menu.Items.Add(protectionItem);
        menu.Items.Add(new NativeMenuItemSeparator());

        foreach (var preset in new[] { 20, 30, 40, 60 })
        {
            var item = new NativeMenuItem($"Target Volume {preset}");
            item.Click += (_, _) => _ = SetPresetFromTrayAsync(preset);
            menu.Items.Add(item);
        }

        menu.Items.Add(new NativeMenuItemSeparator());
        startupItem = new NativeMenuItem("Start at Login")
        {
            ToggleType = MenuItemToggleType.CheckBox,
        };
        startupItem.Click += (_, _) => _ = ToggleStartupFromTrayAsync();
        menu.Items.Add(startupItem);

        var diagnostics = new NativeMenuItem("Diagnostics");
        diagnostics.Click += (_, _) => mainWindow?.ShowDiagnostics();
        menu.Items.Add(diagnostics);
        menu.Items.Add(new NativeMenuItemSeparator());

        var quit = new NativeMenuItem("Quit MateView Guardian");
        quit.Click += (_, _) => _ = QuitAsync(desktop);
        menu.Items.Add(quit);

        trayIcon = new TrayIcon
        {
            Icon = TryLoadIcon(GuardianState.Disconnected),
            ToolTipText = "MateView Guardian",
            Menu = menu,
            IsVisible = true,
        };
        trayIcon.Clicked += (_, _) => ShowSettings();
        viewModel.PropertyChanged += ViewModelOnPropertyChanged;
        UpdateTray();
    }

    private async Task ToggleProtectionFromTrayAsync()
    {
        if (viewModel is null)
        {
            return;
        }
        await viewModel.SetProtectionEnabledAsync(!viewModel.ProtectionEnabled);
        await Dispatcher.UIThread.InvokeAsync(() => mainWindow?.SynchronizeControls());
    }

    private async Task SetPresetFromTrayAsync(int volume)
    {
        if (viewModel is null)
        {
            return;
        }
        await viewModel.SetDesiredVolumeAsync(volume);
        await Dispatcher.UIThread.InvokeAsync(() => mainWindow?.SynchronizeControls());
    }

    private async Task ToggleStartupFromTrayAsync()
    {
        if (viewModel is null)
        {
            return;
        }
        await viewModel.SetStartAtLoginAsync(!viewModel.StartAtLogin);
        await Dispatcher.UIThread.InvokeAsync(() => mainWindow?.SynchronizeControls());
    }

    private async Task QuitAsync(IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (mainWindow is null || viewModel is null ||
            !await mainWindow.ConfirmQuitAsync())
        {
            return;
        }

        isQuitting = true;
        trayIcon?.Dispose();
        await viewModel.StopAsync();
        desktop.Shutdown();
    }

    private void ShowSettings()
    {
        if (mainWindow is null)
        {
            return;
        }
        mainWindow.Show();
        mainWindow.Activate();
    }

    private void ViewModelOnPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(MainWindowViewModel.State) or
            nameof(MainWindowViewModel.ProtectionEnabled) or
            nameof(MainWindowViewModel.StartAtLogin))
        {
            Dispatcher.UIThread.Post(UpdateTray);
        }
    }

    private void UpdateTray()
    {
        if (viewModel is null)
        {
            return;
        }
        if (trayIcon is not null)
        {
            trayIcon.ToolTipText = $"MateView Guardian — {viewModel.OverallStatusText}";
            trayIcon.Icon = TryLoadIcon(viewModel.State);
        }
        if (protectionItem is not null)
        {
            protectionItem.IsChecked = viewModel.ProtectionEnabled;
        }
        if (startupItem is not null)
        {
            startupItem.IsChecked = viewModel.StartAtLogin;
        }
    }

    private static WindowIcon? TryLoadIcon(GuardianState state)
    {
        var variant = state switch
        {
            GuardianState.Protected => "protected",
            GuardianState.Error => "error",
            GuardianState.Disabled => "disabled",
            _ => "partial",
        };
        foreach (var extension in OperatingSystem.IsWindows()
                     ? new[] { "ico", "png" }
                     : new[] { "png", "ico" })
        {
            var name = $"guardian-{variant}.{extension}";
            var uri = new Uri($"avares://MateViewGuardian.App/Assets/{name}");
            if (AssetLoader.Exists(uri))
            {
                return new WindowIcon(AssetLoader.Open(uri));
            }
        }
        return null;
    }

    private static string FindResource(string relativePath)
    {
        var baseDirectory = AppContext.BaseDirectory;
        var direct = Path.Combine(baseDirectory, relativePath);
        if (File.Exists(direct))
        {
            return direct;
        }

        var bundleResource = Path.GetFullPath(Path.Combine(baseDirectory, "..", "Resources", relativePath));
        return File.Exists(bundleResource) ? bundleResource : direct;
    }
}
