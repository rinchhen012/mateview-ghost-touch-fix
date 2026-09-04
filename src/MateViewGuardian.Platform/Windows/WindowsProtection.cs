using System.ComponentModel;
using MateViewGuardian.Core;

namespace MateViewGuardian.Platform.Windows;

public sealed class WindowsProtection : IPlatformProtection
{
    private const int ErrorGraphicsDdcCiVcpNotSupportedByMonitor =
        unchecked((int)0xC0262584);

    private readonly IWindowsMonitorApi monitorApi;
    private readonly IWindowsHidProtection hidProtection;
    private bool hidBlocked;

    public WindowsProtection(IWindowsMonitorApi monitorApi, IWindowsHidProtection hidProtection)
    {
        this.monitorApi = monitorApi ?? throw new ArgumentNullException(nameof(monitorApi));
        this.hidProtection = hidProtection ?? throw new ArgumentNullException(nameof(hidProtection));
    }

    public void ResetHidElevationSuppression() => hidProtection.ResetElevationDenial();

    public async Task<IReadOnlyList<string>> ApplyHidBlockAsync(
        IReadOnlyList<string> recordedIds,
        CancellationToken cancellationToken)
    {
        var ids = await hidProtection.DisableAsync(recordedIds, cancellationToken).ConfigureAwait(false);
        hidBlocked = ids.Count > 0;
        return ids;
    }

    public async Task ClearHidBlockAsync(
        IReadOnlyList<string> recordedIds,
        CancellationToken cancellationToken)
    {
        ResetHidElevationSuppression();
        await hidProtection.EnableAsync(recordedIds, cancellationToken).ConfigureAwait(false);
        hidBlocked = false;
    }

    public Task<PlatformObservation> ObserveAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var monitors = monitorApi.Enumerate();
        try
        {
            var monitor = SelectMonitor(monitors);
            if (monitor is null)
            {
                return Task.FromResult(new PlatformObservation(
                    false, hidBlocked, hidBlocked, false, 0, null, true, null, null));
            }

            var volume = monitor.Read(0x62);
            var mute = ReadMute(monitor, out var supportsMute);
            if (volume > 100 || (mute.HasValue && mute.Value is not (1 or 2)))
            {
                throw new InvalidOperationException(
                    $"The MateView returned unsafe speaker state (volume {volume}, mute {mute}).");
            }

            return Task.FromResult(new PlatformObservation(
                true,
                hidBlocked,
                hidBlocked,
                true,
                (int)volume,
                mute.HasValue ? (int)mute.Value : null,
                supportsMute,
                monitor.Identity,
                null));
        }
        finally
        {
            foreach (var monitor in monitors)
            {
                monitor.Dispose();
            }
        }
    }

    public Task WriteDdcAsync(DdcCorrection correction, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateCorrection(correction);
        var monitors = monitorApi.Enumerate();
        try
        {
            var monitor = SelectMonitor(monitors) ??
                throw new InvalidOperationException("No ZQE-CAA display was found.");
            monitor.Write(correction.Code, correction.Value);
            return Task.CompletedTask;
        }
        finally
        {
            foreach (var monitor in monitors)
            {
                monitor.Dispose();
            }
        }
    }

    private static uint? ReadMute(IWindowsPhysicalMonitor monitor, out bool supportsMute)
    {
        try
        {
            var mute = monitor.Read(0x8D);
            supportsMute = true;
            return mute;
        }
        catch (Win32Exception exception)
            when (exception.NativeErrorCode == ErrorGraphicsDdcCiVcpNotSupportedByMonitor)
        {
            supportsMute = false;
            return null;
        }
    }

    private static IWindowsPhysicalMonitor? SelectMonitor(IReadOnlyList<IWindowsPhysicalMonitor> monitors)
    {
        var matching = monitors.Where(monitor => WindowsMonitorIdentity.IsMateView(
            monitor.Description,
            monitor.DeviceString,
            monitor.DeviceId)).ToArray();
        return matching.Length switch
        {
            0 => null,
            1 => matching[0],
            _ => throw new InvalidOperationException(
                "Multiple ZQE-CAA displays were found; disconnect the unused MateView."),
        };
    }

    private static void ValidateCorrection(DdcCorrection correction)
    {
        if ((correction.Code == 0x62 && correction.Value <= 100) ||
            (correction.Code == 0x8D && correction.Value == 2))
        {
            return;
        }

        throw new InvalidOperationException(
            "Windows permits only VCP 0x62 volume (0-100) and 0x8D unmute (2) writes.");
    }
}
