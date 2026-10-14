using System.IO;

namespace SystemResourceMonitor.Desktop.Adapters.Settings;

/// <summary>File primitives whose failures cannot be induced portably against a real temporary directory.</summary>
internal interface ISettingsFileOperations
{
    bool FileExists(string path);

    void CreateDirectory(string path);

    byte[] ReadAllBytes(string path);

    void MoveNew(string source, string destination);

    void MoveOverwrite(string source, string destination);

    void WriteAndFlush(string path, ReadOnlySpan<byte> content);

    void Delete(string path);
}

internal sealed class SettingsFileOperations : ISettingsFileOperations
{
    public bool FileExists(string path) => File.Exists(path);

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public byte[] ReadAllBytes(string path) => File.ReadAllBytes(path);

    public void MoveNew(string source, string destination) => File.Move(source, destination);

    public void MoveOverwrite(string source, string destination) => File.Move(source, destination, overwrite: true);

    public void WriteAndFlush(string path, ReadOnlySpan<byte> content)
    {
        using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.WriteThrough);
        stream.Write(content);
        stream.Flush(flushToDisk: true);
    }

    public void Delete(string path) => File.Delete(path);
}
