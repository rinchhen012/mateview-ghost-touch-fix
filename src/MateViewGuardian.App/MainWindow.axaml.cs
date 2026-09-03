using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace MateViewGuardian.App;

public sealed partial class MainWindow : Window
{
    private MainWindowViewModel? viewModel;
    private Func<bool> isQuitting = () => false;
    private bool synchronizing;

    public MainWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
    }

    public MainWindow(MainWindowViewModel viewModel, Func<bool> isQuitting)
        : this()
    {
        this.viewModel = viewModel;
        this.isQuitting = isQuitting;
        DataContext = viewModel;
    }

    public void SynchronizeControls()
    {
        if (viewModel is null)
        {
            return;
        }
        synchronizing = true;
        ProtectionToggle.IsChecked = viewModel.ProtectionEnabled;
        StartupToggle.IsChecked = viewModel.StartAtLogin;
        VolumeSlider.Value = viewModel.DesiredVolume;
        synchronizing = false;
    }

    public void ShowDiagnostics()
    {
        if (viewModel is null)
        {
            return;
        }
        var window = CreateMessageWindow("Diagnostics", viewModel.CreateDiagnostics(), "Close");
        _ = window.ShowDialog(this);
    }

    public async Task<bool> ConfirmQuitAsync()
    {
        var dialog = CreateMessageWindow(
            "Stop protection?",
            "Quitting stops the volume watchdog. The HID block may remain until logout; use Restore touch strip first if you want it removed.",
            "Cancel");
        var quit = new Button { Content = "Quit", MinWidth = 90 };
        var result = false;
        quit.Click += (_, _) =>
        {
            result = true;
            dialog.Close();
        };
        ((StackPanel)((DockPanel)dialog.Content!).Children[^1]).Children.Insert(0, quit);
        await dialog.ShowDialog(this);
        return result;
    }

    private async void ProtectionChanged(object? sender, RoutedEventArgs eventArgs)
    {
        if (synchronizing || viewModel is null)
        {
            return;
        }
        await viewModel.SetProtectionEnabledAsync(ProtectionToggle.IsChecked == true);
        SynchronizeControls();
    }

    private async void StartupChanged(object? sender, RoutedEventArgs eventArgs)
    {
        if (synchronizing || viewModel is null)
        {
            return;
        }
        await viewModel.SetStartAtLoginAsync(StartupToggle.IsChecked == true);
        SynchronizeControls();
    }

    private async void SetVolumeClicked(object? sender, RoutedEventArgs eventArgs)
    {
        if (viewModel is null)
        {
            return;
        }
        await viewModel.SetDesiredVolumeAsync((int)Math.Round(VolumeSlider.Value));
        SynchronizeControls();
    }

    private async void ApplyClicked(object? sender, RoutedEventArgs eventArgs)
    {
        if (viewModel is not null)
        {
            await viewModel.ApplyNowAsync();
        }
    }

    private void DiagnosticsClicked(object? sender, RoutedEventArgs eventArgs) => ShowDiagnostics();

    private async void RestoreClicked(object? sender, RoutedEventArgs eventArgs)
    {
        if (viewModel is null)
        {
            return;
        }
        await viewModel.SetProtectionEnabledAsync(false);
        SynchronizeControls();
    }

    private void OnClosing(object? sender, WindowClosingEventArgs eventArgs)
    {
        if (isQuitting())
        {
            return;
        }
        eventArgs.Cancel = true;
        Hide();
    }

    private static Window CreateMessageWindow(string title, string message, string closeLabel)
    {
        var window = new Window
        {
            Title = title,
            Width = 460,
            Height = 260,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var close = new Button { Content = closeLabel, MinWidth = 90 };
        close.Click += (_, _) => window.Close();
        var buttons = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 10,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Children = { close },
        };
        var panel = new DockPanel { Margin = new Avalonia.Thickness(24) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        panel.Children.Add(buttons);
        window.Content = panel;
        return window;
    }
}
