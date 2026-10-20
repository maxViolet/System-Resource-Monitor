using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using SystemResourceMonitor.AIResources.Core.Codex;
using SystemResourceMonitor.Desktop.Adapters.Codex;

namespace SystemResourceMonitor.Desktop.Tests;

/// <summary>
/// Fake installer layouts in a unique temporary directory that serves as both the local application data root and the
/// user profile. Like the real installer, the entry directory is a junction to <c>standalone\current\bin</c>, and
/// <c>current</c> is a junction to a release.
/// </summary>
public sealed class CodexClientLocatorTests : IDisposable
{
    private const string QualifiedRelease = "0.159.0-x86_64-pc-windows-msvc";

    private readonly string _root;
    private readonly string _standalone;
    private readonly string _entryBin;
    private readonly string _current;
    private readonly string _expectedPath;
    private readonly List<string> _junctions = [];
    private readonly FakeVerifier _verifier = new();

    public CodexClientLocatorTests()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"srm-codex-{Guid.NewGuid():N}")).FullName;
        // The locator compares final (long) paths, so an 8.3 temporary path must not reach it.
        _root = LongPath(root);
        TrustOnlyOwnerAndSystem(_root);
        _standalone = Path.Combine(_root, ".codex", "packages", "standalone");
        _entryBin = Path.Combine(_root, "Programs", "OpenAI", "Codex", "bin");
        _current = Path.Combine(_standalone, "current");
        _expectedPath = Path.Combine(_standalone, "releases", QualifiedRelease, "bin", "codex.exe");
        WriteExecutable(_expectedPath);
    }

    [Fact]
    public void Installer_layout_resolves_to_the_pinned_release_and_leases_it_against_writers()
    {
        InstallEntry(Path.GetDirectoryName(Path.GetDirectoryName(_expectedPath))!);

        using (var client = Locate(out _))
        {
            Assert.NotNull(client);
            Assert.Equal(_expectedPath, client.Path, ignoreCase: true);
            Assert.Throws<IOException>(() => File.Open(_expectedPath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite).Dispose());
        }

        File.Open(_expectedPath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite).Dispose();
        Assert.Equal(1, _verifier.Calls);
    }

    [Fact]
    public void Missing_entry_point_is_client_not_found()
    {
        Assert.Null(Locate(out var reason));
        Assert.Equal(CodexUnavailableReason.ClientNotFound, reason);
        Assert.Equal(0, _verifier.Calls);
    }

    [Fact]
    public void Another_release_is_an_unsupported_version()
    {
        var other = Path.Combine(_standalone, "releases", "0.160.0-x86_64-pc-windows-msvc");
        WriteExecutable(Path.Combine(other, "bin", "codex.exe"));
        InstallEntry(other);

        Assert.Null(Locate(out var reason));
        Assert.Equal(CodexUnavailableReason.UnsupportedVersion, reason);
        Assert.Equal(0, _verifier.Calls);
    }

    [Fact]
    public void Entry_redirected_outside_the_releases_directory_is_untrusted()
    {
        var elsewhere = Path.Combine(_root, "elsewhere", QualifiedRelease);
        WriteExecutable(Path.Combine(elsewhere, "bin", "codex.exe"));
        InstallEntry(elsewhere);

        Assert.Null(Locate(out var reason));
        Assert.Equal(CodexUnavailableReason.ClientUntrusted, reason);
        Assert.Equal(0, _verifier.Calls);
    }

    [Fact]
    public void Release_directory_writable_by_other_users_is_untrusted()
    {
        InstallEntry(Path.GetDirectoryName(Path.GetDirectoryName(_expectedPath))!);
        var bin = new DirectoryInfo(Path.GetDirectoryName(_expectedPath)!);
        var security = bin.GetAccessControl();
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), FileSystemRights.Write, AccessControlType.Allow));
        bin.SetAccessControl(security);

        Assert.Null(Locate(out var reason));
        Assert.Equal(CodexUnavailableReason.ClientUntrusted, reason);
        Assert.Equal(0, _verifier.Calls);
    }

    [Fact]
    public void Rejected_signature_is_untrusted_and_rechecked_on_the_next_locate()
    {
        InstallEntry(Path.GetDirectoryName(Path.GetDirectoryName(_expectedPath))!);
        var locator = Locator();
        _verifier.Trusted = false;

        Assert.Null(locator.Locate(out var reason));
        Assert.Equal(CodexUnavailableReason.ClientUntrusted, reason);

        _verifier.Trusted = true;
        using var client = locator.Locate(out _);
        Assert.NotNull(client);
        Assert.Equal(2, _verifier.Calls);
    }

    [Fact]
    public void Signature_is_verified_once_per_file_identity()
    {
        InstallEntry(Path.GetDirectoryName(Path.GetDirectoryName(_expectedPath))!);
        var locator = Locator();

        locator.Locate(out _)!.Dispose();
        locator.Locate(out _)!.Dispose();
        Assert.Equal(1, _verifier.Calls);

        using (var stream = new FileStream(_expectedPath, FileMode.Append, FileAccess.Write))
        {
            stream.WriteByte(0x2A);
        }

        using var client = locator.Locate(out _);
        Assert.NotNull(client);
        Assert.Equal(2, _verifier.Calls);
    }

    [Fact]
    public void Authenticode_rejects_files_not_signed_by_OpenAI()
    {
        var verifier = new AuthenticodeSignatureVerifier();
        string[] files =
        [
            // Unsigned.
            Path.Combine(AppContext.BaseDirectory, "SystemResourceMonitor.Desktop.TestHost.exe"),
            // Signed by Microsoft.
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "notepad.exe"),
            Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "coreclr.dll"),
        ];

        foreach (var file in files)
        {
            using var handle = File.OpenHandle(file, FileMode.Open, FileAccess.Read, FileShare.Read);
            Assert.False(verifier.IsTrusted(handle, file), file);
        }
    }

    public void Dispose()
    {
        // Junctions first and non-recursively, so the recursive delete never follows one.
        foreach (var junction in Enumerable.Reverse(_junctions))
        {
            if (Directory.Exists(junction))
            {
                Directory.Delete(junction);
            }
        }

        Directory.Delete(_root, recursive: true);
    }

    private CodexClientLocator Locator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new CodexClientLocator(_root, _root, _verifier, identity.User!);
    }

    private CodexClient? Locate(out CodexUnavailableReason reason) => Locator().Locate(out reason);

    /// <summary>Points <c>current</c> at <paramref name="release"/> and the entry <c>bin</c> at <c>current\bin</c>.</summary>
    private void InstallEntry(string release)
    {
        CreateJunction(_current, release);
        Directory.CreateDirectory(Path.GetDirectoryName(_entryBin)!);
        CreateJunction(_entryBin, Path.Combine(_current, "bin"));
    }

    private void CreateJunction(string link, string target)
    {
        var startInfo = new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in (string[])["/c", "mklink", "/J", link, target])
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("cmd did not start.");
        // mklink prints one short line, far below the pipe buffer, so sequential reads cannot block.
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            Assert.Fail($"mklink /J failed: {output}");
        }

        _junctions.Add(link);
    }

    private static void WriteExecutable(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [0x4D, 0x5A, 0x00, 0x00]);
    }

    /// <summary>
    /// Replaces the inherited temp-folder ACL with the one a real profile has, so the fixture does not depend on which
    /// other accounts the machine's temp folder admits: only the user, SYSTEM, and Administrators, inherited below.
    /// </summary>
    private static void TrustOnlyOwnerAndSystem(string path)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(identity.User!);
        foreach (var sid in (SecurityIdentifier[])[identity.User!, new(WellKnownSidType.LocalSystemSid, null), new(WellKnownSidType.BuiltinAdministratorsSid, null)])
        {
            security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        }

        new DirectoryInfo(path).SetAccessControl(security);
    }

    private static string LongPath(string path)
    {
        var buffer = new char[1024];
        var length = GetLongPathNameW(path, buffer, (uint)buffer.Length);
        Assert.True(length is > 0 and < 1024, "GetLongPathNameW failed.");
        return Path.TrimEndingDirectorySeparator(new string(buffer, 0, (int)length));
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetLongPathNameW(string shortPath, [Out] char[] longPath, uint bufferLength);

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
