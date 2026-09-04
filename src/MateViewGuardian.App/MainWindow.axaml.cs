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
        var dialog = CreateMessageDialog("Diagnostics", viewModel.CreateDiagnostics(), "Close");
        _ = dialog.Window.ShowDialog(this);
    }

    public async Task<bool> ConfirmQuitAsync()
    {
        var dialog = CreateMessageDialog(
            "Stop protection?",
            "Quitting stops the volume watchdog. The HID block may remain until logout; use Restore touch strip first if you want it removed.",
            "Cancel");
        var quit = CreateDialogButton("Quit");
        var result = false;
        quit.Click += (_, _) =>
        {
            result = true;
            dialog.Window.Close();
        };
        dialog.Buttons.Children.Insert(0, quit);
        await dialog.Window.ShowDialog(this);
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

    private void VolumePreviewChanged(object? sender, RoutedEventArgs eventArgs)
    {
        if (!synchronizing && viewModel is not null)
        {
            viewModel.SetVolumePreview((int)Math.Round(VolumeSlider.Value));
        }
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

    internal static MessageDialog CreateMessageDialog(string title, string message, string closeLabel)
    {
        var window = new Window
        {
            Title = title,
            Width = 460,
            Height = 260,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var content = CreateMessageDialogContent(message, closeLabel, window.Close);
        window.Content = content.Grid;
        return new MessageDialog(window, content.Buttons);
    }

    internal sealed record MessageDialog(Window Window, StackPanel Buttons);

    internal sealed record MessageDialogContent(Grid Grid, StackPanel Buttons);

    internal static MessageDialogContent CreateMessageDialogContent(string message, string closeLabel) =>
        CreateMessageDialogContent(message, closeLabel, static () => { });

    private static MessageDialogContent CreateMessageDialogContent(
        string message,
        string closeLabel,
        Action close)
    {
        var closeButton = CreateDialogButton(closeLabel);
        closeButton.Click += (_, _) => close();
        var buttons = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 10,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Children = { closeButton },
        };
        var grid = new Grid
        {
            Margin = new Avalonia.Thickness(24),
            RowDefinitions = new RowDefinitions("*,Auto"),
            RowSpacing = 16,
        };
        grid.Children.Add(new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        Grid.SetRow(buttons, 1);
        grid.Children.Add(buttons);
        return new MessageDialogContent(grid, buttons);
    }

    internal static Button CreateDialogButton(string label) => new()
    {
        Content = label,
        MinWidth = 90,
        MinHeight = 40,
        Padding = new Avalonia.Thickness(16, 8),
        HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
        VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center,
    };
}
