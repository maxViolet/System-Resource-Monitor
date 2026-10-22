using System.Diagnostics;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using SystemResourceMonitor.AIResources.Core.Claude;
using SystemResourceMonitor.Desktop.Adapters.Claude;
using SystemResourceMonitor.Desktop.Adapters.Codex;

namespace SystemResourceMonitor.Desktop.Tests;

public sealed class ClaudeClientLocatorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"srm-claude-locator-{Guid.NewGuid():N}");
    private readonly FakeVerifier _signature = new();
    private readonly string _userSid;
    private string? _junction;

    public ClaudeClientLocatorTests()
    {
        using var identity = WindowsIdentity.GetCurrent();
        _userSid = identity.User!.Value;
    }

    [Fact]
    public void Missing_fixed_path_is_not_found()
    {
        Assert.Null(Locator().Locate(out var reason));
        Assert.Equal(ClaudeUnavailableReason.ClientNotFound, reason);
        Assert.Equal(0, _signature.Calls);
    }

    [Fact]
    public void Qualified_client_is_leased_against_writes()
    {
        var path = WriteImage();
        using (var client = Locator().Locate(out _))
        {
            Assert.NotNull(client);
            Assert.Equal(path, client.Path);
            Assert.True(client.EnforceImagePolicy);
            Assert.Throws<IOException>(() => File.Open(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite).Dispose());
        }
        Assert.Equal(1, _signature.Calls);
    }

    [Theory]
    [InlineData("2.1.306.0")]
    [InlineData("2.1.307.0")]
    [InlineData("2.1.310.0")]
    [InlineData("2.2.0.0")]
    [InlineData("3.0.0.0")]
    public void Minimum_or_later_client_is_accepted(string version)
    {
        WriteImage();
        using var client = Locator(version: version).Locate(out _);
        Assert.NotNull(client);
        Assert.Equal(1, _signature.Calls);
    }

    [Theory]
    [InlineData("2.1.303.0")]
    [InlineData("2.0.999.0")]
    [InlineData("1.9.0.0")]
    [InlineData("2.1.304")]
    [InlineData("")]
    [InlineData("not-a-version")]
    [InlineData(null)]
    public void Older_or_unreadable_version_is_rejected_before_signature_verification(string? version)
    {
        WriteImage();
        Assert.Null(new ClaudeClientLocator(_root, _userSid, _signature, _ => version).Locate(out var reason));
        Assert.Equal(ClaudeUnavailableReason.UnsupportedVersion, reason);
        Assert.Equal(0, _signature.Calls);
    }

    [Fact]
    public void Wrong_owner_or_signature_is_untrusted()
    {
        WriteImage();
        Assert.Null(Locator(userSid: "S-1-5-21-0-0-0-0").Locate(out var ownerReason));
        Assert.Equal(ClaudeUnavailableReason.ClientUntrusted, ownerReason);
        Assert.Equal(0, _signature.Calls);

        _signature.Trusted = false;
        Assert.Null(Locator().Locate(out var signatureReason));
        Assert.Equal(ClaudeUnavailableReason.ClientUntrusted, signatureReason);
        Assert.Equal(1, _signature.Calls);
    }

    [Fact]
    public void Redirected_bin_directory_is_untrusted()
    {
        var realBin = Path.Combine(_root, "real-bin");
        Directory.CreateDirectory(realBin);
        File.WriteAllBytes(Path.Combine(realBin, "claude.exe"), [0x4D, 0x5A]);
        var local = Directory.CreateDirectory(Path.Combine(_root, ".local")).FullName;
        _junction = Path.Combine(local, "bin");
        var start = new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in (string[])["/c", "mklink", "/J", _junction, realBin])
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("cmd did not start.");
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, output);

        Assert.Null(Locator().Locate(out var reason));
        Assert.Equal(ClaudeUnavailableReason.ClientUntrusted, reason);
        Assert.Equal(0, _signature.Calls);
    }

    public void Dispose()
    {
        if (_junction is not null && Directory.Exists(_junction)) Directory.Delete(_junction);
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private string WriteImage()
    {
        var path = Path.Combine(_root, ".local", "bin", "claude.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [0x4D, 0x5A]);
        return path;
    }

    private ClaudeClientLocator Locator(string? version = null, string? userSid = null) =>
        new(_root, userSid ?? _userSid, _signature, _ => version ?? "2.1.304.0");

    private sealed class FakeVerifier : ICodexSignatureVerifier
    {
        public bool Trusted { get; set; } = true;
        public int Calls { get; private set; }

        public bool IsTrusted(SafeFileHandle file, string path)
        {
            Calls++;
            return Trusted;
        }
    }
}
