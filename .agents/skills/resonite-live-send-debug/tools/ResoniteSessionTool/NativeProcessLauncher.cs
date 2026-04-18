using System.ComponentModel;
using System.Runtime.InteropServices;

using Microsoft.Win32.SafeHandles;

namespace ResoniteSessionTool;

internal static class NativeProcessLauncher
{
    private const uint CreateNoWindow = 0x08000000;
    private const int StartfUseStdHandles = 0x00000100;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint CreateAlways = 2;
    private const uint FileAttributeNormal = 0x00000080;
    private static readonly nint InvalidHandleValue = new(-1);

    public static NativeLaunchedProcess Start(
        string filePath,
        IReadOnlyList<string> argumentList,
        string workingDirectory,
        string stdoutPath,
        string stderrPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("NativeProcessLauncher is only available on Windows.");
        }

        string resolvedFilePath = Path.GetFullPath(filePath);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(stdoutPath))!);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(stderrPath))!);

        SECURITY_ATTRIBUTES securityAttributes = new()
        {
            nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>(),
            bInheritHandle = true,
            lpSecurityDescriptor = nint.Zero,
        };

        nint stdoutHandle = CreateFileW(
            stdoutPath,
            GenericWrite,
            FileShareRead | FileShareWrite,
            ref securityAttributes,
            CreateAlways,
            FileAttributeNormal,
            nint.Zero);
        if (stdoutHandle == InvalidHandleValue)
        {
            throw new InvalidOperationException($"Failed to open redirected log file '{stdoutPath}'. {DescribeLastWin32Error()}");
        }

        nint stderrHandle = CreateFileW(
            stderrPath,
            GenericWrite,
            FileShareRead | FileShareWrite,
            ref securityAttributes,
            CreateAlways,
            FileAttributeNormal,
            nint.Zero);
        if (stderrHandle == InvalidHandleValue)
        {
            CloseHandle(stdoutHandle);
            throw new InvalidOperationException($"Failed to open redirected log file '{stderrPath}'. {DescribeLastWin32Error()}");
        }

        try
        {
            STARTUPINFO startupInfo = new()
            {
                cb = Marshal.SizeOf<STARTUPINFO>(),
                dwFlags = StartfUseStdHandles,
                hStdInput = GetStdHandle(-10),
                hStdOutput = stdoutHandle,
                hStdError = stderrHandle,
            };

            string commandLine = SessionToolPaths.ToWindowsCommandLine(resolvedFilePath, argumentList);
            bool created = CreateProcessW(
                resolvedFilePath,
                commandLine,
                nint.Zero,
                nint.Zero,
                true,
                CreateNoWindow,
                nint.Zero,
                workingDirectory,
                ref startupInfo,
                out PROCESS_INFORMATION processInformation);

            if (!created)
            {
                throw new InvalidOperationException($"Failed to start process '{resolvedFilePath}'. {DescribeLastWin32Error()}");
            }

            CloseHandle(processInformation.hThread);
            return new NativeLaunchedProcess(
                processInformation.dwProcessId,
                new SafeWaitHandle(processInformation.hProcess, ownsHandle: true));
        }
        finally
        {
            CloseHandle(stdoutHandle);
            CloseHandle(stderrHandle);
        }
    }

    private static string DescribeLastWin32Error()
    {
        return new Win32Exception(Marshal.GetLastWin32Error()).Message;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SECURITY_ATTRIBUTES
    {
        public int nLength;
        public nint lpSecurityDescriptor;
        [MarshalAs(UnmanagedType.Bool)]
        public bool bInheritHandle;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public nint lpReserved2;
        public nint hStdInput;
        public nint hStdOutput;
        public nint hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public nint hProcess;
        public nint hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern nint CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        ref SECURITY_ATTRIBUTES lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        nint hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessW(
        string lpApplicationName,
        string lpCommandLine,
        nint lpProcessAttributes,
        nint lpThreadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandles,
        uint dwCreationFlags,
        nint lpEnvironment,
        string lpCurrentDirectory,
        ref STARTUPINFO lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint hObject);

    [DllImport("kernel32.dll", SetLastError = false)]
    private static extern nint GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetExitCodeProcess(nint hProcess, out uint lpExitCode);

    internal sealed class NativeLaunchedProcess : IDisposable
    {
        private readonly SafeWaitHandle processHandle;

        public NativeLaunchedProcess(int processId, SafeWaitHandle processHandle)
        {
            ProcessId = processId;
            this.processHandle = processHandle;
        }

        public int ProcessId { get; }

        public bool TryGetExitCode(out uint exitCode)
        {
            return GetExitCodeProcess(processHandle.DangerousGetHandle(), out exitCode);
        }

        public void Dispose()
        {
            processHandle.Dispose();
        }
    }
}
