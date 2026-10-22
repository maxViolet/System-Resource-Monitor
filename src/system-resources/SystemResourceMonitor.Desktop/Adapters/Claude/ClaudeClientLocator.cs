using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using SystemResourceMonitor.AIResources.Core.Claude;
using SystemResourceMonitor.Desktop.Adapters.Codex;
using static SystemResourceMonitor.Desktop.Adapters.Codex.CodexNativeMethods;

namespace SystemResourceMonitor.Desktop.Adapters.Claude;

internal sealed class ClaudeClient(string path, SafeFileHandle lease, bool enforceImagePolicy = true) : IDisposable
{
    public string Path { get; } = path;
    public bool EnforceImagePolicy { get; } = enforceImagePolicy;
    public void Dispose() => lease.Dispose();
}

internal interface IClaudeClientLocator
{
    ClaudeClient? Locate(out ClaudeUnavailableReason reason);
}

internal sealed class ClaudeClientLocator : IClaudeClientLocator
{
    private const string AnthropicSigner = "CN=\"Anthropic, PBC\", O=\"Anthropic, PBC\", L=San Francisco, S=California, C=US, SERIALNUMBER=4860621, OID.2.5.4.15=Private Organization, OID.1.3.6.1.4.1.311.60.2.1.2=Delaware, OID.1.3.6.1.4.1.311.60.2.1.3=US";
    private static readonly string[] TrustedSystemSids = ["S-1-5-18", "S-1-5-32-544", "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464"];
    private readonly string _path;
    private readonly string[] _chain;
    private readonly string _userSid;
    private readonly ICodexSignatureVerifier _signature;
    private readonly Func<string, string?> _fileVersion;

    internal ClaudeClientLocator(string profile, string userSid,
        ICodexSignatureVerifier? signature = null, Func<string, string?>? fileVersion = null)
    {
        _path = System.IO.Path.Combine(profile, ".local", "bin", "claude.exe");
        _chain = [profile, System.IO.Path.Combine(profile, ".local"),
            System.IO.Path.Combine(profile, ".local", "bin"), _path];
        _userSid = userSid;
        _signature = signature ?? new AuthenticodeSignatureVerifier(AnthropicSigner);
        _fileVersion = fileVersion ?? (path => FileVersionInfo.GetVersionInfo(path).FileVersion);
    }

    public static ClaudeClientLocator ForCurrentUser()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return new ClaudeClientLocator(System.IO.Path.GetFullPath(profile), identity.User!.Value);
    }

    public ClaudeClient? Locate(out ClaudeUnavailableReason reason)
    {
        SafeFileHandle handle;
        try { handle = File.OpenHandle(_path, FileMode.Open, FileAccess.Read, FileShare.Read); }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        { reason = ClaudeUnavailableReason.ClientNotFound; return null; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { reason = ClaudeUnavailableReason.ReadFailed; return null; }

        var transferred = false;
        try
        {
            if (!CanonicalMatches(handle) || !TrustedChain())
            { reason = ClaudeUnavailableReason.ClientUntrusted; return null; }
            if (!ClaudeContract.IsSupportedFileVersion(_fileVersion(_path)))
            { reason = ClaudeUnavailableReason.UnsupportedVersion; return null; }
            // Another principal can write the installer's directory, so signature verification is repeated
            // on every poll while this handle denies modification of the file being started.
            if (!_signature.IsTrusted(handle, _path))
            { reason = ClaudeUnavailableReason.ClientUntrusted; return null; }

            reason = default;
            transferred = true;
            return new ClaudeClient(_path, handle);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        { reason = ClaudeUnavailableReason.ClientUntrusted; return null; }
        finally { if (!transferred) handle.Dispose(); }
    }

    private unsafe bool CanonicalMatches(SafeFileHandle handle)
    {
        Span<char> buffer = stackalloc char[1024];
        fixed (char* chars = buffer)
        {
            var length = GetFinalPathNameByHandle(handle, chars, (uint)buffer.Length,
                FILE_NAME_NORMALIZED | VOLUME_NAME_DOS);
            if (length == 0 || length >= buffer.Length) return false;
            var actual = buffer[..(int)length].ToString();
            if (actual.StartsWith(@"\\?\", StringComparison.Ordinal)) actual = actual[4..];
            return string.Equals(actual, _path, StringComparison.OrdinalIgnoreCase);
        }
    }

    private bool TrustedChain()
    {
        for (var i = 0; i < _chain.Length; i++)
        {
            FileSystemInfo item = i == _chain.Length - 1 ? new FileInfo(_chain[i]) : new DirectoryInfo(_chain[i]);
            if (i > 0 && (item.Attributes & FileAttributes.ReparsePoint) != 0) return false;
            FileSystemSecurity security = item is DirectoryInfo directory
                ? directory.GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access)
                : ((FileInfo)item).GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access);
            if (security.GetOwner(typeof(SecurityIdentifier)) is not SecurityIdentifier owner || !Trusted(owner.Value))
                return false;
            // The native installer may grant the local sandbox group write access. ADR 0018 accepts this
            // user-profile location only with a fixed canonical path, trusted owner, signature, and open lease.
        }
        return true;
    }

    private bool Trusted(string sid) => sid == _userSid || TrustedSystemSids.Contains(sid, StringComparer.Ordinal);
}
