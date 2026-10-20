using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using SystemResourceMonitor.AIResources.Core.Codex;
using static SystemResourceMonitor.Desktop.Adapters.Codex.CodexNativeMethods;

namespace SystemResourceMonitor.Desktop.Adapters.Codex;

/// <summary>
/// A verified client executable. Its handle denies writers and deleters, so the verified file stays the file at
/// <see cref="Path"/> until the caller has started it and disposed the lease.
/// </summary>
internal sealed class CodexClient(string path, SafeFileHandle lease) : IDisposable
{
    /// <summary>The canonical path of the pinned release executable.</summary>
    public string Path { get; } = path;

    public void Dispose() => lease.Dispose();
}

/// <summary>Resolves the qualified standalone Codex client (ADR 0016).</summary>
internal interface ICodexClientLocator
{
    /// <summary>
    /// Returns the verified client, or <see langword="null"/> with <paramref name="reason"/> set to
    /// <see cref="CodexUnavailableReason.ClientNotFound"/>, <see cref="CodexUnavailableReason.UnsupportedVersion"/>,
    /// <see cref="CodexUnavailableReason.ClientUntrusted"/>, or <see cref="CodexUnavailableReason.ReadFailed"/>.
    /// </summary>
    CodexClient? Locate(out CodexUnavailableReason reason);
}

/// <summary>
/// Follows the standalone installer's entry point <c>%LOCALAPPDATA%\Programs\OpenAI\Codex\bin\codex.exe</c> through
/// its junctions and accepts only the pinned release
/// <c>%USERPROFILE%\.codex\packages\standalone\releases\0.159.0-x86_64-pc-windows-msvc\bin\codex.exe</c> (ADR 0016).
/// The canonical path must contain no reparse point below the profile, every component must be owned by and writable
/// only by the current user, SYSTEM, Administrators, or TrustedInstaller, and the file must carry a valid OpenAI
/// Authenticode signature. It never reads the client's login state, and never uses <c>PATH</c> or the current directory.
/// </summary>
internal sealed class CodexClientLocator : ICodexClientLocator
{
    /// <summary>The standalone installer's release directory name of the qualified version.</summary>
    public const string QualifiedRelease = CodexContract.QualifiedVersion + "-x86_64-pc-windows-msvc";

    private const string ExecutableName = "codex.exe";
    private const string SystemSid = "S-1-5-18";
    private const string AdministratorsSid = "S-1-5-32-544";
    private const string TrustedInstallerSid = "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464";
    private const string CreatorOwnerSid = "S-1-3-0";
    private const string OwnerRightsSid = "S-1-3-4";

    // WriteData, AppendData, WriteExtendedAttributes, DeleteSubdirectoriesAndFiles, WriteAttributes, Delete,
    // ChangePermissions, TakeOwnership, GENERIC_ALL, and GENERIC_WRITE; the installation scripts use the same mask.
    private const int WriteRightsMask = 0x500D0156;

    private static readonly string[] ReleaseChain = [".codex", "packages", "standalone", "releases"];

    private readonly string _entryPath;
    private readonly string _profile;
    private readonly string _releases;
    private readonly string _expectedPath;

    /// <summary>The profile, every directory below it, and the executable last.</summary>
    private readonly string[] _chain;
    private readonly ICodexSignatureVerifier _signature;
    private readonly string[] _trustedSids;
    private FileIdentity? _verified;

    public CodexClientLocator(string localAppData, string userProfile, ICodexSignatureVerifier signature, SecurityIdentifier user)
    {
        _entryPath = System.IO.Path.Combine(localAppData, "Programs", "OpenAI", "Codex", "bin", ExecutableName);
        _profile = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(userProfile));
        _releases = System.IO.Path.Combine([_profile, .. ReleaseChain]);
        _expectedPath = System.IO.Path.Combine(_releases, QualifiedRelease, "bin", ExecutableName);
        _chain =
        [
            _profile,
            .. ReleaseChain.Select((_, index) => System.IO.Path.Combine([_profile, .. ReleaseChain[..(index + 1)]])),
            System.IO.Path.Combine(_releases, QualifiedRelease),
            System.IO.Path.Combine(_releases, QualifiedRelease, "bin"),
            _expectedPath,
        ];
        _signature = signature;
        _trustedSids = [user.Value, SystemSid, AdministratorsSid, TrustedInstallerSid];
    }

    /// <summary>The production locator for the current user.</summary>
    public static CodexClientLocator ForCurrentUser()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new CodexClientLocator(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile, Environment.SpecialFolderOption.DoNotVerify),
            new AuthenticodeSignatureVerifier(),
            identity.User ?? throw new InvalidOperationException("The current user has no SID."));
    }

    public CodexClient? Locate(out CodexUnavailableReason reason)
    {
        SafeFileHandle handle;
        try
        {
            // FileShare.Read denies writers and deleters while the lease is held.
            handle = File.OpenHandle(_entryPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            reason = CodexUnavailableReason.ClientNotFound;
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            reason = CodexUnavailableReason.ReadFailed;
            return null;
        }

        var leased = false;
        try
        {
            if (Verify(handle) is { } failed)
            {
                reason = failed;
                return null;
            }

            // The lease moves to the client, which the caller disposes after starting it.
            reason = default;
            leased = true;
            return new CodexClient(_expectedPath, handle);
        }
        finally
        {
            if (!leased)
            {
                handle.Dispose();
            }
        }
    }

    /// <summary>Returns <see langword="null"/> when every check passes, otherwise the failure.</summary>
    private CodexUnavailableReason? Verify(SafeFileHandle handle)
    {
        string finalPath;
        BY_HANDLE_FILE_INFORMATION information;
        try
        {
            finalPath = FinalPath(handle);
            if (!GetFileInformationByHandle(handle, out information))
            {
                return CodexUnavailableReason.ReadFailed;
            }
        }
        catch (IOException)
        {
            return CodexUnavailableReason.ReadFailed;
        }

        if (!string.Equals(finalPath, _expectedPath, StringComparison.OrdinalIgnoreCase))
        {
            // Another release under the installer's releases directory is an unqualified version; anything else
            // is a redirection the installer does not create.
            var release = System.IO.Path.GetDirectoryName(System.IO.Path.GetDirectoryName(finalPath));
            return string.Equals(System.IO.Path.GetDirectoryName(release), _releases, StringComparison.OrdinalIgnoreCase)
                && string.Equals(System.IO.Path.GetFileName(finalPath), ExecutableName, StringComparison.OrdinalIgnoreCase)
                ? CodexUnavailableReason.UnsupportedVersion
                : CodexUnavailableReason.ClientUntrusted;
        }

        try
        {
            if (!IsTrustedChain())
            {
                return CodexUnavailableReason.ClientUntrusted;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or InvalidOperationException or IdentityNotMappedException)
        {
            return CodexUnavailableReason.ClientUntrusted;
        }

        var identity = new FileIdentity(
            information.dwVolumeSerialNumber,
            ((ulong)information.nFileIndexHigh << 32) | information.nFileIndexLow,
            ((ulong)information.nFileSizeHigh << 32) | information.nFileSizeLow,
            ((ulong)information.ftLastWriteTimeHigh << 32) | information.ftLastWriteTimeLow);
        if (_verified != identity)
        {
            // Hashing the 300 MB executable takes seconds, so the signature is verified once per file identity.
            if (!_signature.IsTrusted(handle, finalPath))
            {
                return CodexUnavailableReason.ClientUntrusted;
            }

            _verified = identity;
        }

        return null;
    }

    /// <summary>The profile, every directory below it, and the executable: no reparse point and only trusted writers.</summary>
    private bool IsTrustedChain()
    {
        for (var index = 0; index < _chain.Length; index++)
        {
            FileSystemInfo item = index == _chain.Length - 1 ? new FileInfo(_chain[index]) : new DirectoryInfo(_chain[index]);
            // The profile root is the operating system's; the final path already proved it is not redirected.
            if (index > 0 && (item.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                return false;
            }

            FileSystemSecurity security = item is DirectoryInfo directory
                ? directory.GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access)
                : ((FileInfo)item).GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access);
            if (!IsTrustedSecurity(security))
            {
                return false;
            }
        }

        return true;
    }

    private bool IsTrustedSecurity(FileSystemSecurity security)
    {
        if (security.GetOwner(typeof(SecurityIdentifier)) is not SecurityIdentifier owner || !_trustedSids.Contains(owner.Value))
        {
            return false;
        }

        foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            if (rule.AccessControlType != AccessControlType.Allow)
            {
                continue;
            }

            var sid = rule.IdentityReference.Value;
            if (_trustedSids.Contains(sid) || sid == OwnerRightsSid
                || (sid == CreatorOwnerSid && (rule.PropagationFlags & PropagationFlags.InheritOnly) != 0))
            {
                // Windows replaces an inherit-only CREATOR OWNER entry with the creating user on new children.
                continue;
            }

            if (((int)rule.FileSystemRights & WriteRightsMask) != 0)
            {
                return false;
            }
        }

        return true;
    }

    private static unsafe string FinalPath(SafeFileHandle handle)
    {
        Span<char> buffer = stackalloc char[1024];
        uint length;
        fixed (char* pointer = buffer)
        {
            length = GetFinalPathNameByHandle(handle, pointer, (uint)buffer.Length, FILE_NAME_NORMALIZED | VOLUME_NAME_DOS);
        }

        if (length == 0 || length >= buffer.Length)
        {
            throw new IOException("GetFinalPathNameByHandle failed.", Marshal.GetLastPInvokeError());
        }

        var path = buffer[..(int)length];
        return path.StartsWith(@"\\?\UNC\", StringComparison.Ordinal) ? @"\\" + path[8..].ToString()
            : path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path[4..].ToString()
            : path.ToString();
    }

    private readonly record struct FileIdentity(uint Volume, ulong Index, ulong Size, ulong LastWrite);
}
