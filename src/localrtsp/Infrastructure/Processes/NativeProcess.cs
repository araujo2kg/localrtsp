using System.ComponentModel;
using System.Runtime.InteropServices;

using Microsoft.Win32.SafeHandles;

namespace localrtsp.Infrastructure.Processes
{
    /// <summary>
    /// Creates suspended Windows children, assigns kill-on-close ownership, then resumes them.
    /// </summary>
    internal static class NativeProcess
    {
        #region Constants

        private const uint _killOnJobClose = 0x2000;
        private const int _extendedLimitInformation = 9;
        private const int _inheritHandle = 1;
        private const int _useStandardHandles = 0x100;
        private const int _jobListAttribute = 0x0002000D;
        private const uint _createSuspended = 0x00000004;
        private const uint _extendedStartupInfoPresent = 0x00080000;
        private const uint _createNoWindow = 0x08000000;

        #endregion

        #region Fields

        private static readonly object _creationSync = new();

        #endregion

        #region Public Methods

        /// <summary>
        /// Starts a child process owned by this application.
        /// </summary>
        public static (Process Process, SafeFileHandle Job, StreamReader Output, StreamReader Error, StreamWriter Input) Start(
            string executable,
            IReadOnlyList<string> arguments)
        {
            lock (_creationSync)
            {
                return StartCore(executable, arguments);
            }
        }

        /// <summary>
        /// Escapes one argument for the Windows process command line.
        /// </summary>
        internal static string Quote(string value)
        {
            StringBuilder result = new("\"");
            int backslashCount = 0;

            foreach (char character in value)
            {
                if (character == '\\')
                {
                    backslashCount++;
                    continue;
                }

                result.Append('\\', character == '"' ? backslashCount * 2 + 1 : backslashCount);
                result.Append(character);
                backslashCount = 0;
            }

            result.Append('\\', backslashCount * 2);
            result.Append('"');

            return result.ToString();
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Creates the child process and transfers its native handles to managed owners.
        /// </summary>
        private static (Process Process, SafeFileHandle Job, StreamReader Output, StreamReader Error, StreamWriter Input) StartCore(
            string executable,
            IReadOnlyList<string> arguments)
        {
            if (!OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException("Windows x64 is required.");
            }

            IntPtr job = CreateJobObject(IntPtr.Zero, null);

            if (job == IntPtr.Zero)
            {
                throw new Win32Exception();
            }

            SafeFileHandle jobHandle = new(job, true);
            IntPtr outputRead = IntPtr.Zero;
            IntPtr outputWrite = IntPtr.Zero;
            IntPtr errorRead = IntPtr.Zero;
            IntPtr errorWrite = IntPtr.Zero;
            IntPtr inputRead = IntPtr.Zero;
            IntPtr inputWrite = IntPtr.Zero;
            ProcessInfo processInfo = default;
            IntPtr attributeList = IntPtr.Zero;
            IntPtr jobList = IntPtr.Zero;
            bool attributesInitialized = false;
            Process? ownedProcess = null;
            StreamReader? ownedOutput = null;
            StreamReader? ownedError = null;
            StreamWriter? ownedInput = null;

            try
            {
                ExtendedLimits limits = new()
                {
                    Basic = new BasicLimits
                    {
                        Flags = _killOnJobClose
                    }
                };

                if (!SetInformationJobObject(job, _extendedLimitInformation, ref limits, (uint)Marshal.SizeOf<ExtendedLimits>()))
                {
                    throw new Win32Exception();
                }

                SecurityAttributes attributes = new()
                {
                    Length = Marshal.SizeOf<SecurityAttributes>(),
                    Inherit = _inheritHandle
                };

                if (!CreatePipe(out outputRead, out outputWrite, ref attributes, 0)
                    || !CreatePipe(out errorRead, out errorWrite, ref attributes, 0)
                    || !CreatePipe(out inputRead, out inputWrite, ref attributes, 0))
                {
                    throw new Win32Exception();
                }

                if (!SetHandleInformation(outputRead, _inheritHandle, 0)
                    || !SetHandleInformation(errorRead, _inheritHandle, 0)
                    || !SetHandleInformation(inputWrite, _inheritHandle, 0))
                {
                    throw new Win32Exception();
                }

                IntPtr attributeSize = IntPtr.Zero;
                // The first call reports the required buffer size.
                InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref attributeSize);
                attributeList = Marshal.AllocHGlobal(attributeSize);

                if (!InitializeProcThreadAttributeList(attributeList, 1, 0, ref attributeSize))
                {
                    throw new Win32Exception();
                }

                attributesInitialized = true;

                jobList = Marshal.AllocHGlobal(IntPtr.Size);
                Marshal.WriteIntPtr(jobList, job);

                // PROC_THREAD_ATTRIBUTE_JOB_LIST assigns ownership atomically with process creation.
                if (!UpdateProcThreadAttribute(attributeList, 0, (IntPtr)_jobListAttribute, jobList, (UIntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero))
                {
                    throw new Win32Exception();
                }

                StartupInfoEx startup = new()
                {
                    Startup = new StartupInfo
                    {
                        Size = Marshal.SizeOf<StartupInfoEx>(),
                        Flags = _useStandardHandles,
                        Input = inputRead,
                        Output = outputWrite,
                        Error = errorWrite
                    },
                    AttributeList = attributeList
                };
                StringBuilder command = new(Quote(executable) + " " + string.Join(" ", arguments.Select(Quote)));

                if (!CreateProcess(executable, command, IntPtr.Zero, IntPtr.Zero, true, _createNoWindow | _extendedStartupInfoPresent | _createSuspended, IntPtr.Zero, Path.GetDirectoryName(executable), ref startup, out processInfo))
                {
                    throw new Win32Exception();
                }

                // The child is already owned, suspended, and cannot spawn descendants before resume.
                Process process = ownedProcess = Process.GetProcessById(processInfo.ProcessId);

                StreamReader output = ownedOutput = new(new FileStream(new SafeFileHandle(outputRead, true), FileAccess.Read));
                outputRead = IntPtr.Zero;

                StreamReader error = ownedError = new(new FileStream(new SafeFileHandle(errorRead, true), FileAccess.Read));
                errorRead = IntPtr.Zero;

                StreamWriter input = ownedInput = new(new FileStream(new SafeFileHandle(inputWrite, true), FileAccess.Write))
                {
                    AutoFlush = true
                };
                inputWrite = IntPtr.Zero;

                if (ResumeThread(processInfo.Thread) == uint.MaxValue)
                {
                    process.Dispose();
                    output.Dispose();
                    error.Dispose();
                    input.Dispose();
                    throw new Win32Exception();
                }

                return (process, jobHandle, output, error, input);
            }
            catch
            {
                if (processInfo.Process != IntPtr.Zero)
                {
                    TerminateProcess(processInfo.Process, 1);
                }

                jobHandle.Dispose();
                ownedInput?.Dispose();
                ownedOutput?.Dispose();
                ownedError?.Dispose();
                ownedProcess?.Dispose();
                throw;
            }
            finally
            {
                if (attributesInitialized)
                {
                    DeleteProcThreadAttributeList(attributeList);
                }

                if (attributeList != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(attributeList);
                }

                if (jobList != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(jobList);
                }

                foreach (IntPtr handle in new[]
                {
                    outputRead,
                    outputWrite,
                    errorRead,
                    errorWrite,
                    inputRead,
                    inputWrite,
                    processInfo.Thread,
                    processInfo.Process
                }

                )
                {
                    if (handle != IntPtr.Zero)
                    {
                        CloseHandle(handle);
                    }
                }
            }
        }

        #endregion

        #region Native Methods

        /// <summary>
        /// Creates the Windows job that owns child processes.
        /// </summary>
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateJobObject(IntPtr attributes, string? name);

        /// <summary>
        /// Applies process ownership limits to a Windows job.
        /// </summary>
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetInformationJobObject(IntPtr job, int infoClass, ref ExtendedLimits limits, uint size);

        /// <summary>
        /// Allocates or initializes process creation attributes.
        /// </summary>
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, int flags, ref IntPtr size);

        /// <summary>
        /// Adds an attribute to the process creation list.
        /// </summary>
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool UpdateProcThreadAttribute(
            IntPtr list,
            uint flags,
            IntPtr attribute,
            IntPtr value,
            UIntPtr size,
            IntPtr previous,
            IntPtr returnedSize);

        /// <summary>
        /// Releases initialized process creation attributes.
        /// </summary>
        [DllImport("kernel32.dll")]
        private static extern void DeleteProcThreadAttributeList(IntPtr list);

        /// <summary>
        /// Creates a pipe for redirected process input or output.
        /// </summary>
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CreatePipe(out IntPtr read, out IntPtr write, ref SecurityAttributes attributes, uint size);

        /// <summary>
        /// Controls whether a native handle can be inherited.
        /// </summary>
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetHandleInformation(IntPtr handle, uint mask, uint flags);

        /// <summary>
        /// Creates the native child process with its ownership attributes.
        /// </summary>
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CreateProcess(
            string application,
            StringBuilder command,
            IntPtr processAttributes,
            IntPtr threadAttributes,
            bool inherit,
            uint flags,
            IntPtr environment,
            string? directory,
            ref StartupInfoEx startup,
            out ProcessInfo processInfo);

        /// <summary>
        /// Resumes the suspended child process thread.
        /// </summary>
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint ResumeThread(IntPtr thread);

        /// <summary>
        /// Terminates a child whose startup failed.
        /// </summary>
        [DllImport("kernel32.dll")]
        private static extern bool TerminateProcess(IntPtr process, uint code);

        /// <summary>
        /// Closes a native Windows handle.
        /// </summary>
        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        #endregion

        #region Nested Types

        [StructLayout(LayoutKind.Sequential)]
        private struct SecurityAttributes
        {
            public int Length;
            public IntPtr Descriptor;
            public int Inherit;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct StartupInfo
        {
            public int Size;
            public string? Reserved;
            public string? Desktop;
            public string? Title;
            public int X;
            public int Y;
            public int XSize;
            public int YSize;
            public int XChars;
            public int YChars;
            public int Fill;
            public int Flags;
            public short Show;
            public short ReservedSize;
            public IntPtr ReservedPointer;
            public IntPtr Input;
            public IntPtr Output;
            public IntPtr Error;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct StartupInfoEx
        {
            public StartupInfo Startup;
            public IntPtr AttributeList;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ProcessInfo
        {
            public IntPtr Process;
            public IntPtr Thread;
            public int ProcessId;
            public int ThreadId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BasicLimits
        {
            public long ProcessTime;
            public long JobTime;
            public uint Flags;
            public UIntPtr MinWorkingSet;
            public UIntPtr MaxWorkingSet;
            public uint ActiveProcesses;
            public UIntPtr Affinity;
            public uint Priority;
            public uint Scheduling;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IoCounters
        {
            public ulong ReadOps;
            public ulong WriteOps;
            public ulong OtherOps;
            public ulong ReadBytes;
            public ulong WriteBytes;
            public ulong OtherBytes;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ExtendedLimits
        {
            public BasicLimits Basic;
            public IoCounters Io;
            public UIntPtr ProcessMemory;
            public UIntPtr JobMemory;
            public UIntPtr PeakProcessMemory;
            public UIntPtr PeakJobMemory;
        }

        #endregion
    }
}