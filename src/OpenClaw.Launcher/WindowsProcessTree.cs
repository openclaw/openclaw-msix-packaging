using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OpenClaw.Launcher;

internal static class WindowsProcessTree
{
    private const uint SnapshotProcesses = 0x00000002;
    private static readonly IntPtr InvalidHandleValue = new(-1);

    public static IReadOnlyList<Process> OpenDescendants(int rootProcessId)
    {
        IntPtr snapshot = CreateToolhelp32Snapshot(SnapshotProcesses, 0);
        if (snapshot == InvalidHandleValue)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        try
        {
            var children = new Dictionary<int, List<int>>();
            var entry = new ProcessEntry32
            {
                Size = (uint)Marshal.SizeOf<ProcessEntry32>()
            };
            if (!Process32First(snapshot, ref entry))
            {
                int error = Marshal.GetLastWin32Error();
                if (error == 18)
                {
                    return [];
                }

                throw new Win32Exception(error);
            }

            do
            {
                int parentId = checked((int)entry.ParentProcessId);
                int processId = checked((int)entry.ProcessId);
                if (!children.TryGetValue(parentId, out List<int>? processIds))
                {
                    processIds = [];
                    children.Add(parentId, processIds);
                }

                processIds.Add(processId);
                entry.Size = (uint)Marshal.SizeOf<ProcessEntry32>();
            }
            while (Process32Next(snapshot, ref entry));

            var descendantIds = new HashSet<int>();
            AddDescendants(rootProcessId, children, descendantIds);
            var processes = new List<Process>(descendantIds.Count);
            foreach (int processId in descendantIds)
            {
                try
                {
                    processes.Add(Process.GetProcessById(processId));
                }
                catch (ArgumentException)
                {
                }
            }

            return processes;
        }
        finally
        {
            CloseHandle(snapshot);
        }
    }

    private static void AddDescendants(
        int parentId,
        IReadOnlyDictionary<int, List<int>> children,
        ISet<int> descendants)
    {
        if (!children.TryGetValue(parentId, out List<int>? processIds))
        {
            return;
        }

        foreach (int processId in processIds)
        {
            if (descendants.Add(processId))
            {
                AddDescendants(processId, children, descendants);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry32
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public IntPtr DefaultHeapId;
        public uint ModuleId;
        public uint Threads;
        public uint ParentProcessId;
        public int BasePriority;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string ExecutableFile;
    }

    [DllImport(
        "kernel32.dll",
        SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(
        uint flags,
        uint processId);

    [DllImport(
        "kernel32.dll",
        EntryPoint = "Process32FirstW",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32First(
        IntPtr snapshot,
        ref ProcessEntry32 entry);

    [DllImport(
        "kernel32.dll",
        EntryPoint = "Process32NextW",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32Next(
        IntPtr snapshot,
        ref ProcessEntry32 entry);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
