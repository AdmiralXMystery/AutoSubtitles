using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace AutoSubtitles
{
    public class PythonServerLauncher : IDisposable
    {
        private Process? _process;
        private IntPtr _jobHandle = IntPtr.Zero;

        // ---- Job Object interop ----
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

        [DllImport("kernel32.dll")]
        private static extern bool SetInformationJobObject(IntPtr hJob, JobObjectInfoType infoType, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        private enum JobObjectInfoType { ExtendedLimitInformation = 9 }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IO_COUNTERS
        {
            public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount;
            public ulong ReadTransferCount, WriteTransferCount, OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }

        private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;
        private const uint JOB_OBJECT_LIMIT_SILENT_BREAKAWAY_OK = 0x00001000;

        private void AttachToJobObject(Process process)
        {
            if (_jobHandle == IntPtr.Zero)
                _jobHandle = CreateJobObject(IntPtr.Zero, null);

            if (_jobHandle == IntPtr.Zero) return;

            var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
            info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE | JOB_OBJECT_LIMIT_SILENT_BREAKAWAY_OK;

            int length = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
            IntPtr ptr = Marshal.AllocHGlobal(length);
            try
            {
                Marshal.StructureToPtr(info, ptr, false);
                SetInformationJobObject(_jobHandle, JobObjectInfoType.ExtendedLimitInformation, ptr, (uint)length);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }

            if (!AssignProcessToJobObject(_jobHandle, process.Handle))
            {
                int error = Marshal.GetLastWin32Error();
                Debug.WriteLine($"[WARNING] Failed to assign process to JobObject. Win32 Error: {error}");
            }

            AssignProcessToJobObject(_jobHandle, process.Handle);
        }

        public event Action<string?>? LogReceived;

        public bool Start(string serverExePath)
        {
            if (_process != null && !_process.HasExited)
                return true;

            KillOrphanServers(Path.GetFileNameWithoutExtension(serverExePath));

            if (!File.Exists(serverExePath))
            {
                LogReceived?.Invoke($"The server file was not found at the specified path: {serverExePath}");
                return false;
            }

            string? serverScriptDir = Path.GetDirectoryName(serverExePath);

            try
            {
                _process = new Process();

                _process.StartInfo.FileName = serverExePath;
                _process.StartInfo.WorkingDirectory = serverScriptDir ?? AppContext.BaseDirectory;

                string appDir = AppContext.BaseDirectory;
                string ffmpegPath = Path.Combine(appDir, "ffmpeg", "ffmpeg.exe");

                if (File.Exists(ffmpegPath))
                {
                    // Передаем путь через аргумент командной строки (в кавычках, на случай пробелов в путях)
                    _process.StartInfo.Arguments = $"--ffmpeg-path \"{ffmpegPath}\"";
                    LogReceived?.Invoke($"[Launcher] Found local FFmpeg at: {ffmpegPath}. Passing to Python server.");
                }
                else
                {
                    LogReceived?.Invoke($"[WARNING] Local FFmpeg not found at {ffmpegPath}. Server will try to use system default.");
                }

                _process.StartInfo.UseShellExecute = false;
                _process.StartInfo.CreateNoWindow = true;
                _process.StartInfo.RedirectStandardOutput = true;
                _process.StartInfo.RedirectStandardError = true;

                _process.StartInfo.StandardOutputEncoding = Encoding.UTF8;
                _process.StartInfo.StandardErrorEncoding = Encoding.UTF8;

                _process.OutputDataReceived += (_, e) => LogReceived?.Invoke(e.Data);
                _process.ErrorDataReceived += (_, e) => LogReceived?.Invoke(e.Data);

                _process.Start();
                AttachToJobObject(_process);
                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();

                return true;
            }
            catch (Exception ex)
            {
                LogReceived?.Invoke($"The server process failed to start: {ex.Message}");
                _process = null;
                return false;
            }
        }


        private static void KillOrphanServers(string processNameWithoutExt)
        {
            if (string.IsNullOrWhiteSpace(processNameWithoutExt)) return;

            Process[] existing;
            try { existing = Process.GetProcessesByName(processNameWithoutExt); }
            catch { return; }

            foreach (var p in existing)
            {
                try
                {
                    if (!p.HasExited)
                    {
                        p.Kill(entireProcessTree: true);
                        p.WaitForExit(3000);
                        Debug.WriteLine($"Killed orphan server PID={p.Id}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to kill orphan server PID={p.Id}: {ex.Message}");
                }
                finally
                {
                    p.Dispose();
                }
            }
        }

        public void Stop()
        {
            if (_process == null && _jobHandle == IntPtr.Zero) return;

            try
            {
                if (_process != null && !_process.HasExited)
                    _process.Kill(entireProcessTree: true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error while closing the server: {ex.Message}");
            }
            finally
            {
                _process.Dispose();
                _process = null;

                if (_jobHandle != IntPtr.Zero)
                {
                    CloseHandle(_jobHandle);
                    _jobHandle = IntPtr.Zero;
                }
            }
        }

        public void Dispose()
        {
            Stop();
            GC.SuppressFinalize(this);
        }
    }
}