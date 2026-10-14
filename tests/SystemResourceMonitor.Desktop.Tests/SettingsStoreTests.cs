using Microsoft.Extensions.Time.Testing;
using SystemResourceMonitor.Desktop.Adapters.Settings;
using SystemResourceMonitor.Desktop.Settings;

namespace SystemResourceMonitor.Desktop.Tests;

public sealed class SettingsStoreTests
{
    [Fact]
    public async Task Missing_settings_use_Top_without_an_issue()
    {
        using var directory = new TemporaryDirectory();
        var result = await new JsonSettingsStore(directory.Path).LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new BarSettings(BarEdge.Top), result.Settings);
        Assert.Null(result.Issue);
    }

    [Theory]
    [InlineData(BarEdge.Top)]
    [InlineData(BarEdge.Bottom)]
    public async Task Valid_edges_round_trip_and_unknown_properties_are_ignored(BarEdge edge)
    {
        using var directory = new TemporaryDirectory();
        var store = new JsonSettingsStore(directory.Path);
        Assert.True((await store.SaveAsync(new BarSettings(edge), TestContext.Current.CancellationToken)).Succeeded);

        var saved = await store.LoadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(new BarSettings(edge), saved.Settings);
        Assert.Null(saved.Issue);

        File.WriteAllText(System.IO.Path.Combine(directory.Path, "settings.json"), $"{{\"future\":42,\"edge\":\"{edge}\"}}");
        var additive = await store.LoadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(new BarSettings(edge), additive.Settings);
        Assert.Null(additive.Issue);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"edge\":1}")]
    [InlineData("{\"edge\":\"Left\"}")]
    [InlineData("{\"edge\":\"Top\",\"edge\":\"Bottom\"}")]
    public async Task Missing_non_string_unknown_and_duplicate_edges_are_malformed(string content)
    {
        using var directory = new TemporaryDirectory();
        File.WriteAllText(System.IO.Path.Combine(directory.Path, "settings.json"), content);

        var result = await new JsonSettingsStore(directory.Path).LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new BarSettings(BarEdge.Top), result.Settings);
        Assert.Equal(SettingsLoadIssue.Malformed, result.Issue);
    }

    [Fact]
    public async Task Malformed_bytes_are_moved_byte_for_byte_with_a_deterministic_collision_suffix()
    {
        using var directory = new TemporaryDirectory();
        var malformed = new byte[] { 0xff, 0x00, 0x7b, 0x01 };
        var settings = System.IO.Path.Combine(directory.Path, "settings.json");
        File.WriteAllBytes(settings, malformed);
        var now = new DateTimeOffset(2026, 10, 14, 8, 9, 10, TimeSpan.Zero).AddTicks(1234567);
        var timestamp = "20261014T0809101234567Z";
        File.WriteAllText(System.IO.Path.Combine(directory.Path, $"settings.invalid-{timestamp}.json"), "existing");

        var result = await new JsonSettingsStore(directory.Path, new FakeTimeProvider(now)).LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new BarSettings(BarEdge.Top), result.Settings);
        Assert.Equal(SettingsLoadIssue.Malformed, result.Issue);
        Assert.False(File.Exists(settings));
        Assert.Equal(malformed, File.ReadAllBytes(System.IO.Path.Combine(directory.Path, $"settings.invalid-{timestamp}-1.json")));
    }

    [Fact]
    public async Task Read_and_backup_failures_return_Top_without_escaping()
    {
        using var readDirectory = new TemporaryDirectory();
        File.WriteAllText(System.IO.Path.Combine(readDirectory.Path, "settings.json"), "{\"edge\":\"Bottom\"}");
        var read = new JsonSettingsStore(readDirectory.Path, TimeProvider.System, new FaultingFiles(readFails: true));
        var readResult = await read.LoadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(new BarSettings(BarEdge.Top), readResult.Settings);
        Assert.Equal(SettingsLoadIssue.ReadFailed, readResult.Issue);

        using var backupDirectory = new TemporaryDirectory();
        var path = System.IO.Path.Combine(backupDirectory.Path, "settings.json");
        var malformed = new byte[] { 1, 2, 3 };
        File.WriteAllBytes(path, malformed);
        var backup = new JsonSettingsStore(backupDirectory.Path, TimeProvider.System, new FaultingFiles(backupFails: true));
        var backupResult = await backup.LoadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(new BarSettings(BarEdge.Top), backupResult.Settings);
        Assert.Equal(SettingsLoadIssue.BackupFailed, backupResult.Issue);
        Assert.Equal(malformed, File.ReadAllBytes(path));
    }

    [Fact]
    public async Task Atomic_replacement_round_trips_and_failed_replacement_retains_the_previous_valid_file()
    {
        using var directory = new TemporaryDirectory();
        var store = new JsonSettingsStore(directory.Path);
        Assert.True((await store.SaveAsync(new BarSettings(BarEdge.Top), TestContext.Current.CancellationToken)).Succeeded);
        var settingsPath = System.IO.Path.Combine(directory.Path, "settings.json");
        var previous = File.ReadAllBytes(settingsPath);

        var failing = new JsonSettingsStore(directory.Path, TimeProvider.System, new FaultingFiles(replaceFails: true));
        var failed = await failing.SaveAsync(new BarSettings(BarEdge.Bottom), TestContext.Current.CancellationToken);
        Assert.Equal(SettingsSaveIssue.WriteFailed, failed.Issue);
        Assert.Equal(previous, File.ReadAllBytes(settingsPath));
        Assert.DoesNotContain(Directory.GetFiles(directory.Path), path => path.EndsWith(".tmp", StringComparison.Ordinal));

        Assert.True((await store.SaveAsync(new BarSettings(BarEdge.Bottom), TestContext.Current.CancellationToken)).Succeeded);
        Assert.Equal(new BarSettings(BarEdge.Bottom), (await store.LoadAsync(TestContext.Current.CancellationToken)).Settings);
    }

    private sealed class FaultingFiles(
        bool readFails = false,
        bool backupFails = false,
        bool replaceFails = false) : ISettingsFileOperations
    {
        private readonly SettingsFileOperations _inner = new();

        public bool FileExists(string path) => _inner.FileExists(path);

        public void CreateDirectory(string path) => _inner.CreateDirectory(path);

        public byte[] ReadAllBytes(string path) => readFails ? throw new IOException("read") : _inner.ReadAllBytes(path);

        public void MoveNew(string source, string destination)
        {
            if (backupFails && System.IO.Path.GetFileName(destination).StartsWith("settings.invalid-", StringComparison.Ordinal))
            {
                throw new IOException("backup");
            }

            _inner.MoveNew(source, destination);
        }

        public void Replace(string source, string destination)
        {
            if (replaceFails)
            {
                throw new IOException("replace");
            }

            _inner.Replace(source, destination);
        }

        public void WriteAndFlush(string path, ReadOnlySpan<byte> content) => _inner.WriteAndFlush(path, content);

        public void Delete(string path) => _inner.Delete(path);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SystemResourceMonitor.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
