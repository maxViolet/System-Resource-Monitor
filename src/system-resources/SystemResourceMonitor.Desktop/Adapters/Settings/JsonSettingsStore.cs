using System.IO;

using System.Text;
using System.Text.Json;
using SystemResourceMonitor.Desktop.Settings;

namespace SystemResourceMonitor.Desktop.Adapters.Settings;

/// <summary>Reads and atomically replaces the one-file Desktop settings document.</summary>
public sealed class JsonSettingsStore : ISettingsStore
{
    private const string FileName = "settings.json";
    private readonly string _directory;
    private readonly string _path;
    private readonly TimeProvider _time;
    private readonly ISettingsFileOperations _files;
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public JsonSettingsStore(string directory, TimeProvider? time = null)
        : this(directory, time ?? TimeProvider.System, new SettingsFileOperations())
    {
    }

    internal JsonSettingsStore(string directory, TimeProvider time, ISettingsFileOperations files)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = directory;
        _path = Path.Combine(directory, FileName);
        _time = time;
        _files = files;
    }

    public Task<SettingsLoadResult> LoadAsync(CancellationToken cancellationToken = default) =>
        Task.Run(LoadCore, cancellationToken);

    public async Task<SettingsSaveResult> SaveAsync(BarSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(() => SaveCore(settings), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return new SettingsSaveResult(SettingsSaveIssue.WriteFailed);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private SettingsLoadResult LoadCore()
    {
        if (!_files.FileExists(_path))
        {
            return new SettingsLoadResult(BarSettings.Default);
        }

        byte[] content;
        try
        {
            content = _files.ReadAllBytes(_path);
        }
        catch (Exception)
        {
            return new SettingsLoadResult(BarSettings.Default, SettingsLoadIssue.ReadFailed);
        }

        if (TryParse(content, out var settings))
        {
            return new SettingsLoadResult(settings);
        }

        try
        {
            _files.MoveNew(_path, NextInvalidPath());
            return new SettingsLoadResult(BarSettings.Default, SettingsLoadIssue.Malformed);
        }
        catch (Exception)
        {
            return new SettingsLoadResult(BarSettings.Default, SettingsLoadIssue.BackupFailed);
        }
    }

    private SettingsSaveResult SaveCore(BarSettings settings)
    {
        var temporary = Path.Combine(_directory, $"settings.{Guid.NewGuid():N}.tmp");
        try
        {
            _files.CreateDirectory(_directory);
            var content = Encoding.UTF8.GetBytes($"{{\"edge\":\"{settings.Edge}\"}}");
            _files.WriteAndFlush(temporary, content);
            if (_files.FileExists(_path))
            {
                _files.Replace(temporary, _path);
            }
            else
            {
                _files.MoveNew(temporary, _path);
            }

            return new SettingsSaveResult();
        }
        catch (Exception)
        {
            try
            {
                _files.Delete(temporary);
            }
            catch (Exception)
            {
                // Only the uniquely owned temporary file is eligible for best-effort cleanup.
            }

            return new SettingsSaveResult(SettingsSaveIssue.WriteFailed);
        }
    }

    private string NextInvalidPath()
    {
        var timestamp = _time.GetUtcNow().UtcDateTime.ToString("yyyyMMdd'T'HHmmssfffffff'Z'", System.Globalization.CultureInfo.InvariantCulture);
        var candidate = Path.Combine(_directory, $"settings.invalid-{timestamp}.json");
        for (var suffix = 1; _files.FileExists(candidate); suffix++)
        {
            candidate = Path.Combine(_directory, $"settings.invalid-{timestamp}-{suffix}.json");
        }

        return candidate;
    }

    private static bool TryParse(byte[] content, out BarSettings settings)
    {
        settings = BarSettings.Default;
        try
        {
            using var document = JsonDocument.Parse(content);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            BarEdge? edge = null;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Name != "edge")
                {
                    continue;
                }

                if (edge is not null || property.Value.ValueKind != JsonValueKind.String)
                {
                    return false;
                }

                edge = property.Value.GetString() switch
                {
                    "Top" => BarEdge.Top,
                    "Bottom" => BarEdge.Bottom,
                    _ => null,
                };
                if (edge is null)
                {
                    return false;
                }
            }

            if (edge is not { } parsed)
            {
                return false;
            }

            settings = new BarSettings(parsed);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
