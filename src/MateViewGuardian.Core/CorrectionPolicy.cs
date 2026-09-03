namespace MateViewGuardian.Core;

public static class CorrectionPolicy
{
    public static IReadOnlyList<DdcCorrection> GetCorrections(
        int currentVolume,
        int? currentMute,
        GuardianSettings settings,
        bool supportsMute)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var normalized = settings.Normalize();
        if (!normalized.ProtectionEnabled)
        {
            return [];
        }

        var corrections = new List<DdcCorrection>(2);
        if (currentVolume != normalized.DesiredVolume)
        {
            corrections.Add(new DdcCorrection(0x62, (uint)normalized.DesiredVolume));
        }

        if (supportsMute && currentMute is not null && currentMute != 2)
        {
            corrections.Add(new DdcCorrection(0x8D, 2));
        }

        return corrections;
    }
}
