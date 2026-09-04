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
using MateViewGuardian.Platform.Startup;
using MateViewGuardian.Platform.Windows;

namespace MateViewGuardian.App;

public sealed partial class App : Application
{
    private MainWindow? mainWindow;
    private MainWindowViewModel? viewModel;
    private TrayIcon? trayIcon;
    private readonly MacStatusMenuLauncher macStatusMenuLauncher = new();
    private readonly MacMenuCommandServer macMenuCommandServer = new();
    private NativeMenuItem? protectionItem;
    private NativeMenuItem? startupItem;
    private JsonSettingsStore? settingsStore;
    private LegacyMigration? legacyMigration;
    private bool settingsExisted;
    private bool isQuitting;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var runtime = CreateRuntime();
            settingsStore = runtime.SettingsStore;
            legacyMigration = runtime.Migration;
            settingsExisted = File.Exists(runtime.SettingsPath);
            viewModel = new MainWindowViewModel(runtime.Coordinator, runtime.StartupManager);
            mainWindow = new MainWindow(viewModel, () => isQuitting);
            desktop.MainWindow = mainWindow;
            desktop.ShutdownRequested += (_, _) => BeginExternalQuit();
            if (desktop is IActivatableLifetime activatableLifetime)
            {
                activatableLifetime.Activated += (_, eventArgs) =>
                {
                    if (eventArgs.Kind == ActivationKind.Reopen)
                    {
                        ShowSettings();
                    }
                };
            }
            CreateTray(desktop);
            if (Program.ConsumeActivationRequest())
            {
                ShowSettings();
            }
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

        if (!settingsExisted && settingsStore is not null && legacyMigration is not null)
        {
            var migrated = await legacyMigration.ImportSettingsAsync(GuardianSettings.Default);
            await settingsStore.SaveAsync(migrated);
        }

        await viewModel.InitializeAsync(applyProtection: !Program.RestoreAndExit);
        if (Program.RestoreAndExit)
        {
            await viewModel.SetProtectionEnabledAsync(false);
            await viewModel.SetStartAtLoginAsync(false);
            if (legacyMigration is not null)
            {
                await legacyMigration.StopLegacyStartupAsync();
            }
            isQuitting = true;
            trayIcon?.Dispose();
            macStatusMenuLauncher.Dispose();
            macMenuCommandServer.Dispose();
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
            {
                lifetime.Shutdown();
            }
            return;
        }

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
        if (legacyMigration is not null)
        {
            await legacyMigration.StopLegacyStartupAsync();
        }
    }

    private RuntimeServices CreateRuntime()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roamingAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dataDirectory = OperatingSystem.IsMacOS()
            ? Path.Combine(home, "Library", "Application Support", "MateViewGuardian")
            : Path.Combine(localAppData, "MateViewGuardian");
        var settingsPath = Path.Combine(dataDirectory, "settings.json");
        var settings = new JsonSettingsStore(settingsPath);
        IPlatformProtection platform;
        IStartupManager startupManager;
        LegacyMigration migration;
        if (OperatingSystem.IsMacOS())
        {
            platform = new MacProtection(
                new ProcessRunner(),
                "/usr/bin/hidutil",
                FindResource("ASDDC"));
            var launchAgent = Path.Combine(
                home, "Library", "LaunchAgents", "com.mateview.guardian.plist");
            startupManager = new MacStartupManager(
                launchAgent,
                "/Applications/MateView Guardian.app/Contents/MacOS/MateViewGuardian.App");
            var legacyPlist = Path.Combine(
                home, "Library", "LaunchAgents", "com.mateview-ghost-touch-fix.plist");
            migration = new LegacyMigration(
                legacyPlist,
                null,
                null,
                cancellationToken => StopLegacyMacAsync(legacyPlist, cancellationToken));
        }
        else if (OperatingSystem.IsWindows())
        {
            var hid = new WindowsHidProtection(
                new ProcessRunner(),
                new ElevatedProcessRunner(),
                "powershell.exe",
                FindResource(Path.Combine("platform-tools", "windows", "MateViewHid.ps1")),
                WindowsHidProtection.ReleaseHelperSha256);
            platform = new WindowsProtection(new WindowsMonitorApi(), hid);
            var launcher = Path.Combine(
                roamingAppData,
                "Microsoft", "Windows", "Start Menu", "Programs", "Startup", "MateViewGuardian.cmd");
            startupManager = new WindowsStartupManager(
                launcher,
                Path.Combine(AppContext.BaseDirectory, "MateViewGuardian.App.exe"));
            var legacyDirectory = Path.Combine(localAppData, "MateViewGhostTouchFix");
            var legacyScript = Path.Combine(legacyDirectory, "MateViewFix.ps1");
            migration = new LegacyMigration(
                null,
                Path.Combine(legacyDirectory, "config.json"),
                Path.Combine(
                    roamingAppData,
                    "Microsoft", "Windows", "Start Menu", "Programs", "Startup", "MateViewGhostTouchFix.cmd"),
                cancellationToken => StopLegacyWindowsAsync(legacyScript, cancellationToken));
        }
        else
        {
            throw new PlatformNotSupportedException("MateView Guardian supports macOS and Windows.");
        }

        return new RuntimeServices(
            new ProtectionCoordinator(platform, settings),
            settings,
            settingsPath,
            startupManager,
            migration);
    }

    private void CreateTray(IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (viewModel is null)
        {
            return;
        }

        if (OperatingSystem.IsMacOS())
        {
            macMenuCommandServer.Start(HandleMacMenuCommandAsync);
            macStatusMenuLauncher.Start(FindResource("MateViewGuardianMenuBar"), FindMacAppBundle());
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

        foreach (var preset in VolumePresets.Values)
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
            IsVisible = false,
        };
        trayIcon.Clicked += (_, _) => ShowSettings();
        viewModel.PropertyChanged += ViewModelOnPropertyChanged;
        TrayIcon.SetIcons(this, new TrayIcons { trayIcon });
        trayIcon.IsVisible = true;
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
        macStatusMenuLauncher.Dispose();
        macMenuCommandServer.Dispose();
        await viewModel.StopAsync();
        desktop.Shutdown();
    }

    private void BeginExternalQuit()
    {
        if (isQuitting)
        {
            return;
        }

        isQuitting = true;
        trayIcon?.Dispose();
        macStatusMenuLauncher.Dispose();
        macMenuCommandServer.Dispose();
        if (viewModel is not null)
        {
            _ = viewModel.StopAsync();
        }
    }

    private Task HandleMacMenuCommandAsync(string command)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                if (viewModel is not null)
                {
                    switch (command)
                    {
                        case "toggle-protection":
                            await viewModel.SetProtectionEnabledAsync(!viewModel.ProtectionEnabled);
                            break;
                        case "toggle-startup":
                            await viewModel.SetStartAtLoginAsync(!viewModel.StartAtLogin);
                            break;
                        case "diagnostics":
                            mainWindow?.ShowDiagnostics();
                            break;
                        case "show-settings":
                            ShowSettings();
                            break;
                        case "quit":
                            BeginExternalQuit();
                            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                            {
                                desktop.Shutdown();
                            }
                            break;
                        default:
                            if (VolumePresets.TryParseCommand(command, out var volume))
                            {
                                await viewModel.SetDesiredVolumeAsync(volume);
                            }
                            break;
                    }
                    mainWindow?.SynchronizeControls();
                }
                completion.TrySetResult();
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        });
        return completion.Task;
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

    internal void ActivateFromSecondInstance()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (mainWindow is null)
            {
                return;
            }

            Program.ConsumeActivationRequest();
            ShowSettings();
        });
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
        foreach (var extension in new[] { "ico", "png" })
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

        var resourceSibling = Path.GetFullPath(Path.Combine(baseDirectory, "..", relativePath));
        if (File.Exists(resourceSibling))
        {
            return resourceSibling;
        }

        var bundleResource = Path.GetFullPath(Path.Combine(baseDirectory, "..", "Resources", relativePath));
        return File.Exists(bundleResource) ? bundleResource : direct;
    }

    private static string FindMacAppBundle()
    {
        var bundle = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
        return Directory.Exists(bundle) ? bundle : "/Applications/MateView Guardian.app";
    }

    private static async Task StopLegacyMacAsync(string plistPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(plistPath))
        {
            return;
        }
        await new ProcessRunner().RunAsync(
            "/bin/launchctl",
            ["unload", plistPath],
            TimeSpan.FromSeconds(5),
            cancellationToken);
    }

    private static async Task StopLegacyWindowsAsync(string scriptPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(scriptPath))
        {
            return;
        }
        await new ProcessRunner().RunAsync(
            "powershell.exe",
            ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", scriptPath, "Disable"],
            TimeSpan.FromSeconds(10),
            cancellationToken);
    }

    private sealed record RuntimeServices(
        ProtectionCoordinator Coordinator,
        JsonSettingsStore SettingsStore,
        string SettingsPath,
        IStartupManager StartupManager,
        LegacyMigration Migration);
}
