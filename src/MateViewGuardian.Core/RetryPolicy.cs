namespace MateViewGuardian.Core;

public static class RetryPolicy
{
    public const int ActiveMilliseconds = 500;

    public const int MaximumMilliseconds = 10_000;

    public static int Next(int currentMilliseconds, bool succeeded)
    {
        if (succeeded)
        {
            return ActiveMilliseconds;
        }

        return Math.Min(
            MaximumMilliseconds,
            Math.Max(ActiveMilliseconds, currentMilliseconds * 2));
    }
}
