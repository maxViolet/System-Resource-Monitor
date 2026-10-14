using System.IO;

using SystemResourceMonitor.Desktop.Adapters.Settings;
using SystemResourceMonitor.Desktop.Settings;

namespace SystemResourceMonitor.Desktop.TestHost;

internal enum SettingsFault
{
    Read,
    Backup,
    Write,
}

internal sealed class FaultingSettingsFileOperations(SettingsFault fault) : ISettingsFileOperations
{
    private readonly SettingsFileOperations _inner = new();

    public bool FileExists(string path) => _inner.FileExists(path);

    public void CreateDirectory(string path) => _inner.CreateDirectory(path);

    public byte[] ReadAllBytes(string path) => fault == SettingsFault.Read
        ? throw new IOException("Injected settings read failure.")
        : _inner.ReadAllBytes(path);

    public void MoveNew(string source, string destination)
    {
        if (fault == SettingsFault.Backup && Path.GetFileName(destination).StartsWith("settings.invalid-", StringComparison.Ordinal))
        {
            throw new IOException("Injected malformed-settings backup failure.");
        }

        _inner.MoveNew(source, destination);
    }

    public void Replace(string source, string destination)
    {
        if (fault == SettingsFault.Write)
        {
            throw new IOException("Injected settings replacement failure.");
        }

        _inner.Replace(source, destination);
    }

    public void WriteAndFlush(string path, ReadOnlySpan<byte> content) => _inner.WriteAndFlush(path, content);

    public void Delete(string path) => _inner.Delete(path);
}

internal sealed class ObservedSettingsStore(ISettingsStore inner, int expectedSaves = 1) : ISettingsStore
{
    private readonly TaskCompletionSource<SettingsSaveResult> _saveCompleted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _completedSaves;

    public Task<SettingsSaveResult> SaveCompleted => _saveCompleted.Task;

    public Task<SettingsLoadResult> LoadAsync(CancellationToken cancellationToken = default) =>
        inner.LoadAsync(cancellationToken);

    public async Task<SettingsSaveResult> SaveAsync(BarSettings settings, CancellationToken cancellationToken = default)
    {
        var result = await inner.SaveAsync(settings, cancellationToken);
        if (Interlocked.Increment(ref _completedSaves) == expectedSaves)
        {
            _saveCompleted.TrySetResult(result);
        }
        return result;
    }
}
