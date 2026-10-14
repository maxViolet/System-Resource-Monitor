namespace SystemResourceMonitor.Desktop.Settings;

/// <summary>The primary-monitor edge reserved by the bar.</summary>
public enum BarEdge
{
    Top,
    Bottom,
}

/// <summary>All persisted first-release settings.</summary>
public sealed record BarSettings(BarEdge Edge)
{
    public static BarSettings Default { get; } = new(BarEdge.Top);
}

public enum SettingsLoadIssue
{
    ReadFailed,
    Malformed,
    BackupFailed,
}

public readonly record struct SettingsLoadResult(BarSettings Settings, SettingsLoadIssue? Issue = null);

public enum SettingsSaveIssue
{
    WriteFailed,
}

public readonly record struct SettingsSaveResult(SettingsSaveIssue? Issue = null)
{
    public bool Succeeded => Issue is null;
}

/// <summary>Loads and atomically persists the bar's settings.</summary>
public interface ISettingsStore
{
    Task<SettingsLoadResult> LoadAsync(CancellationToken cancellationToken = default);

    Task<SettingsSaveResult> SaveAsync(BarSettings settings, CancellationToken cancellationToken = default);
}
