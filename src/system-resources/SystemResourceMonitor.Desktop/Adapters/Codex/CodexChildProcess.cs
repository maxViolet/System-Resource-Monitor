using System.Collections;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using SystemResourceMonitor.AIResources.Core.Codex;
using static SystemResourceMonitor.Desktop.Adapters.Codex.CodexNativeMethods;

namespace SystemResourceMonitor.Desktop.Adapters.Codex;

/// <summary>
/// A Codex client created inside its owning job before any client code can run. Only the three standard-stream pipe
/// handles are inherited; the parent ends remain private to Desktop.
/// </summary>
internal sealed class CodexChildProcess : IDisposable
{
    private readonly SafeProcessHandle _process;

    private CodexChildProcess(Stream input, Stream output, Stream error, SafeProcessHandle process)
    {
        Input = input;
        Output = output;
        Error = error;
        _process = process;
    }

    public Stream Input { get; }
    public Stream Output { get; }
    public Stream Error { get; }

    public uint ExitCode
    {
        get
        {
            if (!GetExitCodeProcess(_process, out var code))
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Reading the child exit code failed.");
            return code;
        }
    }

    public static unsafe CodexChildProcess Start(
        string path,
        ProcessTreeJob job,
        IReadOnlyDictionary<string, string>? environment,
        IReadOnlyList<string>? arguments = null,
        ulong? mitigationPolicy = null)
    {
        SafeFileHandle? childInput = null;
        SafeFileHandle? parentInput = null;
        SafeFileHandle? parentOutput = null;
        SafeFileHandle? childOutput = null;
        SafeFileHandle? parentError = null;
        SafeFileHandle? childError = null;
        void* attributeList = null;
        var attributeListInitialized = false;
        PROCESS_INFORMATION processInformation = default;

        try
        {
            var security = new SECURITY_ATTRIBUTES
            {
                nLength = (uint)sizeof(SECURITY_ATTRIBUTES),
                bInheritHandle = true,
            };
            var pipeCreated = CreatePipe(out var inputRead, out var inputWrite, (nint)(&security), 0);
            childInput = inputRead;
            parentInput = inputWrite;
            if (!pipeCreated)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Creating the Codex stdin pipe failed.");
            }

            pipeCreated = CreatePipe(out var outputRead, out var outputWrite, (nint)(&security), 0);
            parentOutput = outputRead;
            childOutput = outputWrite;
            if (!pipeCreated)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Creating the Codex stdout pipe failed.");
            }

            pipeCreated = CreatePipe(out var errorRead, out var errorWrite, (nint)(&security), 0);
            parentError = errorRead;
            childError = errorWrite;
            if (!pipeCreated)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Creating the Codex stderr pipe failed.");
            }

            ClearInheritance(parentInput);
            ClearInheritance(parentOutput);
            ClearInheritance(parentError);

            nuint attributeBytes = 0;
            var attributeCount = mitigationPolicy is null ? 2u : 3u;
            _ = InitializeProcThreadAttributeList(null, attributeCount, 0, &attributeBytes);
            if (attributeBytes == 0)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Sizing the Codex process attribute list failed.");
            }

            attributeList = NativeMemory.Alloc(attributeBytes);
            if (attributeList is null)
            {
                throw new OutOfMemoryException();
            }

            if (!InitializeProcThreadAttributeList(attributeList, attributeCount, 0, &attributeBytes))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Initializing the Codex process attribute list failed.");
            }
            attributeListInitialized = true;

            // The attribute list stores pointers, not copies, and CreateProcess reads them later: every attribute value
            // lives on this stack frame, because a pinned managed array could move before the launch.
            const int inheritedHandleCount = 3;
            var inheritedHandles = stackalloc nint[inheritedHandleCount];
            inheritedHandles[0] = childInput.DangerousGetHandle();
            inheritedHandles[1] = childOutput.DangerousGetHandle();
            inheritedHandles[2] = childError.DangerousGetHandle();
            var jobHandle = job.Handle.DangerousGetHandle();
            var policy = mitigationPolicy.GetValueOrDefault();
            if (!UpdateProcThreadAttribute(attributeList, 0, PROC_THREAD_ATTRIBUTE_HANDLE_LIST, inheritedHandles,
                    (nuint)(inheritedHandleCount * sizeof(nint)), 0, 0)
                || !UpdateProcThreadAttribute(attributeList, 0, PROC_THREAD_ATTRIBUTE_JOB_LIST, &jobHandle,
                    (nuint)sizeof(nint), 0, 0)
                || (mitigationPolicy is not null && !UpdateProcThreadAttribute(attributeList, 0,
                    PROC_THREAD_ATTRIBUTE_MITIGATION_POLICY, &policy, (nuint)sizeof(ulong), 0, 0)))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Configuring Codex process ownership failed.");
            }

            var startup = new STARTUPINFOEXW
            {
                StartupInfo =
                {
                    cb = (uint)sizeof(STARTUPINFOEXW),
                    dwFlags = STARTF_USESTDHANDLES,
                    hStdInput = childInput.DangerousGetHandle(),
                    hStdOutput = childOutput.DangerousGetHandle(),
                    hStdError = childError.DangerousGetHandle(),
                },
                lpAttributeList = (nint)attributeList,
            };
            var commandLine = BuildCommandLine(path, arguments ?? CodexContract.Arguments).ToCharArray();
            var environmentBlock = BuildEnvironmentBlock(environment);
            var workingDirectory = Path.GetTempPath();
            fixed (char* applicationName = path)
            fixed (char* mutableCommandLine = commandLine)
            fixed (char* childEnvironment = environmentBlock)
            fixed (char* currentDirectory = workingDirectory)
            {
                var flags = EXTENDED_STARTUPINFO_PRESENT | CREATE_NO_WINDOW;
                if (environmentBlock is not null)
                {
                    flags |= CREATE_UNICODE_ENVIRONMENT;
                }

                if (!CreateProcess(applicationName, mutableCommandLine, 0, 0, true, flags, childEnvironment,
                        currentDirectory, &startup, &processInformation))
                {
                    throw new Win32Exception(Marshal.GetLastPInvokeError(), "Starting the Codex client failed.");
                }
            }

            CloseHandle(processInformation.hThread);
            processInformation.hThread = 0;
            childInput.Dispose();
            childInput = null;
            childOutput.Dispose();
            childOutput = null;
            childError.Dispose();
            childError = null;

            var input = new FileStream(parentInput, FileAccess.Write, 4096, isAsync: false);
            parentInput = null;
            var output = new FileStream(parentOutput, FileAccess.Read, 4096, isAsync: false);
            parentOutput = null;
            var error = new FileStream(parentError, FileAccess.Read, 4096, isAsync: false);
            parentError = null;
            var process = new SafeProcessHandle(processInformation.hProcess);
            processInformation.hProcess = 0;
            return new CodexChildProcess(input, output, error, process);
        }
        finally
        {
            if (processInformation.hThread != 0)
            {
                CloseHandle(processInformation.hThread);
            }

            if (processInformation.hProcess != 0)
            {
                CloseHandle(processInformation.hProcess);
            }

            if (attributeList is not null)
            {
                if (attributeListInitialized)
                {
                    DeleteProcThreadAttributeList(attributeList);
                }

                NativeMemory.Free(attributeList);
            }

            childInput?.Dispose();
            parentInput?.Dispose();
            parentOutput?.Dispose();
            childOutput?.Dispose();
            parentError?.Dispose();
            childError?.Dispose();
        }
    }

    public void Dispose()
    {
        Input.Dispose();
        Output.Dispose();
        Error.Dispose();
        _process.Dispose();
    }

    private static void ClearInheritance(SafeFileHandle handle)
    {
        if (!SetHandleInformation(handle, HANDLE_FLAG_INHERIT, 0))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Restricting Codex pipe inheritance failed.");
        }
    }

    private static string BuildCommandLine(string path, IReadOnlyList<string> arguments)
    {
        var commandLine = new StringBuilder(path.Length + 64).Append('"').Append(path).Append('"');
        foreach (var argument in arguments)
        {
            // The qualified commands use only simple tokens or an empty --tools value.
            if (argument.Contains('"') || argument.Contains('\\'))
                throw new ArgumentException("Unsupported child argument character.", nameof(arguments));
            commandLine.Append(' ').Append('"').Append(argument).Append('"');
        }

        commandLine.Append('\0');
        return commandLine.ToString();
    }

    private static char[]? BuildEnvironmentBlock(IReadOnlyDictionary<string, string>? additions)
    {
        if (additions is null)
        {
            return null;
        }

        var variables = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            variables[(string)entry.Key] = (string?)entry.Value ?? string.Empty;
        }

        foreach (var (name, value) in additions)
        {
            variables[name] = value;
        }

        var block = new StringBuilder();
        foreach (var (name, value) in variables)
        {
            block.Append(name).Append('=').Append(value).Append('\0');
        }

        block.Append('\0');
        return block.ToString().ToCharArray();
    }
}
