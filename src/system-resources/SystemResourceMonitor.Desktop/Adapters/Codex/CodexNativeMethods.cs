using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SystemResourceMonitor.Desktop.Adapters.Codex;

/// <summary>
/// The kernel32 and wintrust calls behind Codex client resolution and child-process ownership: final-path
/// canonicalization, file identity, Authenticode verification, the job object that owns each client tree, and the
/// client's creation inside that job.
/// </summary>
internal static partial class CodexNativeMethods
{
    internal const uint FILE_NAME_NORMALIZED = 0x0;
    internal const uint VOLUME_NAME_DOS = 0x0;

    internal const int JobObjectBasicAccountingInformation = 1;
    internal const int JobObjectExtendedLimitInformation = 9;
    internal const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

    internal const uint HANDLE_FLAG_INHERIT = 0x1;
    internal const uint STARTF_USESTDHANDLES = 0x100;
    internal const uint CREATE_UNICODE_ENVIRONMENT = 0x400;
    internal const uint EXTENDED_STARTUPINFO_PRESENT = 0x80000;
    internal const uint CREATE_NO_WINDOW = 0x08000000;
    internal const int ERROR_INSUFFICIENT_BUFFER = 122;

    /// <summary>ProcThreadAttributeValue(2, FALSE, TRUE, FALSE): the only handles the child inherits.</summary>
    internal const nuint PROC_THREAD_ATTRIBUTE_HANDLE_LIST = 0x20002;

    /// <summary>ProcThreadAttributeValue(13, FALSE, TRUE, FALSE): jobs the child belongs to before it runs (Windows 10+).</summary>
    internal const nuint PROC_THREAD_ATTRIBUTE_JOB_LIST = 0x2000D;

    /// <summary>ProcThreadAttributeValue(7, FALSE, TRUE, FALSE): child image-loading policy.</summary>
    internal const nuint PROC_THREAD_ATTRIBUTE_MITIGATION_POLICY = 0x20007;

    internal const ulong BLOCK_NON_MICROSOFT_BINARIES_ALWAYS_ON = 1UL << 44;
    internal const ulong IMAGE_LOAD_NO_REMOTE_ALWAYS_ON = 1UL << 52;
    internal const ulong IMAGE_LOAD_PREFER_SYSTEM32_ALWAYS_ON = 1UL << 60;

    internal const uint WTD_UI_NONE = 2;
    internal const uint WTD_REVOKE_NONE = 0;
    internal const uint WTD_CHOICE_FILE = 1;
    internal const uint WTD_STATEACTION_VERIFY = 1;
    internal const uint WTD_STATEACTION_CLOSE = 2;
    internal const uint WTD_CACHE_ONLY_URL_RETRIEVAL = 0x1000;

    /// <summary>WINTRUST_ACTION_GENERIC_VERIFY_V2: Authenticode policy.</summary>
    internal static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    [StructLayout(LayoutKind.Sequential)]
    internal struct BY_HANDLE_FILE_INFORMATION
    {
        public uint dwFileAttributes;

        // Three FILETIME values as DWORD pairs: FILETIME is 4-byte aligned, unlike a long.
        public uint ftCreationTimeLow;
        public uint ftCreationTimeHigh;
        public uint ftLastAccessTimeLow;
        public uint ftLastAccessTimeHigh;
        public uint ftLastWriteTimeLow;
        public uint ftLastWriteTimeHigh;
        public uint dwVolumeSerialNumber;
        public uint nFileSizeHigh;
        public uint nFileSizeLow;
        public uint nNumberOfLinks;
        public uint nFileIndexHigh;
        public uint nFileIndexLow;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct WINTRUST_FILE_INFO
    {
        public uint cbStruct;
        public nint pcwszFilePath;
        public nint hFile;
        public nint pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct WINTRUST_DATA
    {
        public uint cbStruct;
        public nint pPolicyCallbackData;
        public nint pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public nint pFile;
        public uint dwStateAction;
        public nint hWVTStateData;
        public nint pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public nint pSignatureSettings;
    }

    /// <summary>The leading fields of CRYPT_PROVIDER_SGNR, up to the signer's certificate chain.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct CRYPT_PROVIDER_SGNR
    {
        public uint cbStruct;
        public uint sftVerifyAsOfLow;
        public uint sftVerifyAsOfHigh;
        public uint csCertChain;
        public nint pasCertChain;
    }

    /// <summary>The leading fields of CRYPT_PROVIDER_CERT, up to its certificate context.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct CRYPT_PROVIDER_CERT
    {
        public uint cbStruct;
        public nint pCert;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct JOBOBJECT_BASIC_ACCOUNTING_INFORMATION
    {
        public long TotalUserTime;
        public long TotalKernelTime;
        public long ThisPeriodTotalUserTime;
        public long ThisPeriodTotalKernelTime;
        public uint TotalPageFaultCount;
        public uint TotalProcesses;
        public uint ActiveProcesses;
        public uint TotalTerminatedProcesses;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SECURITY_ATTRIBUTES
    {
        public uint nLength;
        public nint lpSecurityDescriptor;
        [MarshalAs(UnmanagedType.Bool)]
        public bool bInheritHandle;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct STARTUPINFOW
    {
        public uint cb;
        public nint lpReserved;
        public nint lpDesktop;
        public nint lpTitle;
        public uint dwX;
        public uint dwY;
        public uint dwXSize;
        public uint dwYSize;
        public uint dwXCountChars;
        public uint dwYCountChars;
        public uint dwFillAttribute;
        public uint dwFlags;
        public ushort wShowWindow;
        public ushort cbReserved2;
        public nint lpReserved2;
        public nint hStdInput;
        public nint hStdOutput;
        public nint hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct STARTUPINFOEXW
    {
        public STARTUPINFOW StartupInfo;
        public nint lpAttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PROCESS_INFORMATION
    {
        public nint hProcess;
        public nint hThread;
        public uint dwProcessId;
        public uint dwThreadId;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", SetLastError = true)]
    internal static unsafe partial uint GetFinalPathNameByHandle(SafeFileHandle hFile, char* lpszFilePath, uint cchFilePath, uint dwFlags);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetFileInformationByHandle(SafeFileHandle hFile, out BY_HANDLE_FILE_INFORMATION lpFileInformation);

    [LibraryImport("wintrust.dll")]
    internal static unsafe partial int WinVerifyTrust(nint hwnd, Guid* pgActionID, WINTRUST_DATA* pWVTData);

    [LibraryImport("wintrust.dll")]
    internal static partial nint WTHelperProvDataFromStateData(nint hStateData);

    [LibraryImport("wintrust.dll")]
    internal static partial nint WTHelperGetProvSignerFromChain(nint pProvData, uint idxSigner, [MarshalAs(UnmanagedType.Bool)] bool fCounterSigner, uint idxCounterSigner);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateJobObjectW", SetLastError = true)]
    internal static partial SafeJobHandle CreateJobObject(nint lpJobAttributes, nint lpName);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static unsafe partial bool SetInformationJobObject(SafeJobHandle hJob, int jobObjectInformationClass, void* lpJobObjectInformation, uint cbJobObjectInformationLength);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static unsafe partial bool QueryInformationJobObject(SafeJobHandle hJob, int jobObjectInformationClass, void* lpJobObjectInformation, uint cbJobObjectInformationLength, nint lpReturnLength);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool TerminateJobObject(SafeJobHandle hJob, uint uExitCode);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CreatePipe(out SafeFileHandle hReadPipe, out SafeFileHandle hWritePipe, nint lpPipeAttributes, uint nSize);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetHandleInformation(SafeFileHandle hObject, uint dwMask, uint dwFlags);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static unsafe partial bool InitializeProcThreadAttributeList(void* lpAttributeList, uint dwAttributeCount, uint dwFlags, nuint* lpSize);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static unsafe partial bool UpdateProcThreadAttribute(void* lpAttributeList, uint dwFlags, nuint attribute, void* lpValue, nuint cbSize, nint lpPreviousValue, nint lpReturnSize);

    [LibraryImport("kernel32.dll")]
    internal static unsafe partial void DeleteProcThreadAttributeList(void* lpAttributeList);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateProcessW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static unsafe partial bool CreateProcess(char* lpApplicationName, char* lpCommandLine, nint lpProcessAttributes,
        nint lpThreadAttributes, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandles, uint dwCreationFlags, char* lpEnvironment,
        char* lpCurrentDirectory, STARTUPINFOEXW* lpStartupInfo, PROCESS_INFORMATION* lpProcessInformation);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseHandle(nint hObject);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetExitCodeProcess(SafeProcessHandle hProcess, out uint lpExitCode);
}

/// <summary>An owned job-object handle. Closing it kills every process still in the job.</summary>
internal sealed class SafeJobHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public SafeJobHandle()
        : base(ownsHandle: true)
    {
    }

    protected override bool ReleaseHandle() => CodexNativeMethods.CloseHandle(handle);
}

/// <summary>An owned thread handle from process creation.</summary>
internal sealed class SafeThreadHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public SafeThreadHandle(nint handle)
        : base(ownsHandle: true) => SetHandle(handle);

    protected override bool ReleaseHandle() => CodexNativeMethods.CloseHandle(handle);
}

/// <summary>An owned child-process handle retained until its exit status is read.</summary>
internal sealed class SafeProcessHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public SafeProcessHandle(nint handle)
        : base(ownsHandle: true) => SetHandle(handle);

    protected override bool ReleaseHandle() => CodexNativeMethods.CloseHandle(handle);
}
