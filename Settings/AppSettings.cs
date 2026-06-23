namespace ShakeToBigCursor.Settings;

/// <summary>User-facing settings persisted to %APPDATA%\WindowsShakeToFindCursor\settings.json.</summary>
public sealed class AppSettings
{
    public const int DefaultMaxCursorHeight = 432;
    public const int DefaultActivationDelayMilliseconds = 850;
    public const int DefaultReleaseHoldMilliseconds = 450;
    public const int CurrentSettingsVersion = 1;

    public int SettingsVersion { get; set; }

    public int MaxCursorHeight { get; set; } = DefaultMaxCursorHeight;

    public int ActivationDelayMilliseconds { get; set; } = DefaultActivationDelayMilliseconds;

    public int ReleaseHoldMilliseconds { get; set; } = DefaultReleaseHoldMilliseconds;

    public void Normalize()
    {
        if (SettingsVersion == 0 &&
            MaxCursorHeight == 64 &&
            ActivationDelayMilliseconds == 250 &&
            ReleaseHoldMilliseconds == 0)
        {
            MaxCursorHeight = DefaultMaxCursorHeight;
            ActivationDelayMilliseconds = DefaultActivationDelayMilliseconds;
            ReleaseHoldMilliseconds = DefaultReleaseHoldMilliseconds;
        }

        MaxCursorHeight = Math.Clamp(MaxCursorHeight, 64, 512);
        ActivationDelayMilliseconds = Math.Clamp(ActivationDelayMilliseconds, 250, 2000);
        ReleaseHoldMilliseconds = Math.Clamp(ReleaseHoldMilliseconds, 0, 1500);
        SettingsVersion = CurrentSettingsVersion;
    }

    public AppSettings Clone() => (AppSettings)MemberwiseClone();
}
