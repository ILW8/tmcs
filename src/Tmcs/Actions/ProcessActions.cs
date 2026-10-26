using System.Runtime.InteropServices;
using Tmcs.Native;
using Tmcs.Processes;

namespace Tmcs.Actions;

/// <summary>
/// The process actions, with the Win32 and NT APIs Task Manager uses (reference 3.2). Every handle opened here is closed before the
/// call returns, and every process is opened, then checked against the identity's creation time, before anything is done to it.
/// </summary>
internal sealed unsafe class ProcessActions : IProcessActions
{
    // PROCESS_TERMINATE | PROCESS_QUERY_LIMITED_INFORMATION (0x1001), as Task Manager opens a process to end it.
    const uint EndTaskAccess = Kernel32.ProcessTerminate | Kernel32.ProcessQueryLimitedInformation;

    // PROCESS_TERMINATE | PROCESS_DUP_HANDLE | PROCESS_QUERY_LIMITED_INFORMATION (0x1041), as Task Manager reopens it to close its handles.
    const uint HandleCloserAccess = Kernel32.ProcessTerminate | Kernel32.ProcessDupHandle | Kernel32.ProcessQueryLimitedInformation;

    // PROCESS_SET_INFORMATION | PROCESS_QUERY_LIMITED_INFORMATION (0x1200): SetPriorityClass needs the first, the identity check and
    // GetPriorityClass the second.
    const uint SetPriorityAccess = Kernel32.ProcessSetInformation | Kernel32.ProcessQueryLimitedInformation;

    // The same right as Set priority: SetProcessAffinityMask needs the first, the identity check the second. Reading the masks needs
    // only PROCESS_QUERY_LIMITED_INFORMATION.
    const uint SetAffinityAccess = SetPriorityAccess;

    const int EFail = unchecked((int)0x80004005);

    const int ErrorInvalidParameter = 87;

    // 37226 "The operation is not valid for this process.": a PID Task Manager refuses outright.
    static readonly ActionOutcome NotValidForThisProcess = new(ActionOutcome.EInvalidArg, ShowDialog: true);

    // The handle list is read into a buffer that grows until it fits. Past the maximum, the answer is treated as garbage.
    const uint FirstHandleBufferBytes = 16 * 1024;
    const uint MaxHandleBufferBytes = 64 << 20;

    readonly Lazy<bool> _elevated = new(ReadElevation);

    public bool IsElevated => _elevated.Value;

    public bool IsCritical(ProcessIdentity id)
    {
        // Some systems want the full query right for this class, so a denied open or query is tried once more with it.
        return ReadBreakOnTermination(id, Kernel32.ProcessQueryLimitedInformation)
               ?? ReadBreakOnTermination(id, Kernel32.ProcessQueryInformation)
               ?? false;
    }

    public ActionOutcome EndTask(ProcessIdentity id)
    {
        // Task Manager refuses the idle process, System and the other kernel-made PIDs before opening anything (reference 3.2 step 1).
        if (id.Pid is 0 or 1 or 4 or 5) return NotValidForThisProcess;

        var process = Kernel32.OpenProcess(EndTaskAccess, false, id.Pid);
        if (process == 0)
        {
            // ERROR_INVALID_PARAMETER is how OpenProcess says no process has this PID: it exited since the row was read.
            var openError = Marshal.GetLastPInvokeError();
            return openError == ErrorInvalidParameter ? ActionOutcome.Gone : new(LastErrorHResult(openError), ShowDialog: true);
        }
        try
        {
            // The PID may have been reused since the row was read. Never end another process.
            if (!IsSameProcess(process, id)) return ActionOutcome.Gone;

            if (Kernel32.TerminateProcess(process, 1)) return ActionOutcome.Success;

            var error = Marshal.GetLastPInvokeError();
            if (error != Kernel32.ErrorAccessDenied) return new(LastErrorHResult(error), ShowDialog: true);

            // Access denied after a good open means the process is already exiting, and may be stuck there on pending I/O.
            // Task Manager shows nothing and closes the process's handles in the background, which can let the exit finish (reference 3.2 step 5).
            QueueHandleCloser(id);
            return new(ActionOutcome.EAccessDenied, ShowDialog: false);
        }
        finally
        {
            Kernel32.CloseHandle(process);
        }
    }

    public ActionOutcome EndProcessTree(ProcessIdentity id, IReadOnlyList<ProcessRow> snapshot) => ProcessTree.EndEach(id, snapshot, EndTask);

    public PrioritySet SetPriority(ProcessIdentity id, ProcessPriority priority)
    {
        // The same refusal as End task: these PIDs are not processes that can be opened (reference 3.2 step 1).
        if (id.Pid is 0 or 1 or 4 or 5) return new(NotValidForThisProcess, false);

        var process = Kernel32.OpenProcess(SetPriorityAccess, false, id.Pid);
        if (process == 0)
        {
            var openError = Marshal.GetLastPInvokeError();
            return new(openError == ErrorInvalidParameter ? ActionOutcome.Gone : new(LastErrorHResult(openError), ShowDialog: true), false);
        }
        try
        {
            if (!IsSameProcess(process, id)) return new(ActionOutcome.Gone, false);

            var requested = ProcessPriorities.ToClass(priority);
            if (!Kernel32.SetPriorityClass(process, requested))
                return new(new(LastErrorHResult(Marshal.GetLastPInvokeError()), ShowDialog: true), false);

            // Realtime without SeIncreaseBasePriorityPrivilege succeeds but sets High, which only the read-back shows (reference 4).
            // A read-back that fails (0) contradicts nothing, so it counts as a match and no "set to High instead" notice shows.
            var readBack = Kernel32.GetPriorityClass(process);
            return new(ActionOutcome.Success, readBack == 0 || readBack == requested);
        }
        finally
        {
            Kernel32.CloseHandle(process);
        }
    }

    public AffinityInfo? GetAffinity(ProcessIdentity id)
    {
        if (id.Pid is 0 or 1 or 4 or 5) return null;

        var process = OpenSameProcess(id, Kernel32.ProcessQueryLimitedInformation);
        if (process == 0) return null;
        try
        {
            return Kernel32.GetProcessAffinityMask(process, out var processMask, out var systemMask) ? new AffinityInfo(processMask, systemMask) : null;
        }
        finally
        {
            Kernel32.CloseHandle(process);
        }
    }

    public ActionOutcome SetAffinity(ProcessIdentity id, nuint mask)
    {
        if (id.Pid is 0 or 1 or 4 or 5) return NotValidForThisProcess;

        var process = Kernel32.OpenProcess(SetAffinityAccess, false, id.Pid);
        if (process == 0)
        {
            var openError = Marshal.GetLastPInvokeError();
            return openError == ErrorInvalidParameter ? ActionOutcome.Gone : new(LastErrorHResult(openError), ShowDialog: true);
        }
        try
        {
            if (!IsSameProcess(process, id)) return ActionOutcome.Gone;

            return Kernel32.SetProcessAffinityMask(process, mask)
                ? ActionOutcome.Success
                : new(LastErrorHResult(Marshal.GetLastPInvokeError()), ShowDialog: true);
        }
        finally
        {
            Kernel32.CloseHandle(process);
        }
    }

    static bool ReadElevation()
    {
        if (!Advapi32.OpenProcessToken(Kernel32.GetCurrentProcess(), Advapi32.TokenQuery, out var token)) return false;
        try
        {
            uint elevated = 0;
            return Advapi32.GetTokenInformation(token, Advapi32.TokenElevation, &elevated, sizeof(uint), out _) && elevated != 0;
        }
        finally
        {
            Kernel32.CloseHandle(token);
        }
    }

    /// <returns>The flag, or null when the open or the query was denied with <paramref name="access"/>.</returns>
    static bool? ReadBreakOnTermination(ProcessIdentity id, uint access)
    {
        var process = Kernel32.OpenProcess(access, false, id.Pid);
        if (process == 0) return Marshal.GetLastPInvokeError() == Kernel32.ErrorAccessDenied ? null : false;
        try
        {
            if (!IsSameProcess(process, id)) return false;

            uint flag = 0;
            var status = NtDll.NtQueryInformationProcess(process, NtDll.ProcessBreakOnTermination, &flag, sizeof(uint), out _);
            if (status == NtDll.StatusAccessDenied) return null;
            return status >= 0 && flag != 0;
        }
        finally
        {
            Kernel32.CloseHandle(process);
        }
    }

    static bool IsSameProcess(nint process, ProcessIdentity id) =>
        Kernel32.GetProcessTimes(process, out var created, out _, out _, out _) && created == id.CreateTime;

    /// <summary><c>HRESULT_FROM_WIN32</c>, with E_FAIL for a failure that left no error code, so a failure never reads as success.</summary>
    static int LastErrorHResult(int error) => error == 0 ? EFail : ActionOutcome.FromWin32(error);

    /// <returns>The process opened with <paramref name="access"/>, or 0 when it can't be opened or is not <paramref name="id"/>.</returns>
    static nint OpenSameProcess(ProcessIdentity id, uint access)
    {
        var process = Kernel32.OpenProcess(access, false, id.Pid);
        if (process == 0 || IsSameProcess(process, id)) return process;
        Kernel32.CloseHandle(process);
        return 0;
    }

    // ---- The handle closer (reference 3.2 step 5) ----

    static void QueueHandleCloser(ProcessIdentity id)
    {
        // The identity travels to the pool thread in a native block, which the work item frees.
        var context = (ProcessIdentity*)NativeMemory.Alloc((nuint)sizeof(ProcessIdentity));
        *context = id;
        if (!Kernel32.QueueUserWorkItem(&HandleCloserWorkItem, context, Kernel32.WtExecuteLongFunction)) NativeMemory.Free(context);
    }

    [UnmanagedCallersOnly]
    static uint HandleCloserWorkItem(void* context)
    {
        try
        {
            var id = *(ProcessIdentity*)context;
            NativeMemory.Free(context);
            CloseHandlesOf(id);
        }
        catch
        {
            // An exception must not unwind into native code.
        }
        return 0;
    }

    /// <summary>
    /// Closes the handles of the process <paramref name="id"/>, in that process, the way Task Manager does for a process stuck exiting:
    /// every handle that is not a File, and a File only when it is local and a disk file or a pipe. One pass over that one process;
    /// no other process is read or touched. The queued work item runs this; tests call it directly. It never throws.
    /// </summary>
    internal static void CloseHandlesOf(ProcessIdentity id)
    {
        try
        {
            CloseHandlesCore(id);
        }
        catch (Exception)
        {
            // An unhandled exception on a pool thread would end tmcs. The native calls report failure by return value,
            // so this is only a last resort for something unforeseen.
        }
    }

    static void CloseHandlesCore(ProcessIdentity id)
    {
        var process = OpenSameProcess(id, HandleCloserAccess);
        if (process == 0) return;
        try
        {
            var handles = ReadHandles(process, out var status);

            // The process finished exiting first, so there is nothing left to close. This is done, not an error.
            if (status == NtDll.StatusProcessIsTerminating) return;

            if (status == NtDll.StatusAccessDenied)
            {
                // Some systems want the full query right for the handle list. Reopen with it and try once more.
                Kernel32.CloseHandle(process);
                process = OpenSameProcess(id, HandleCloserAccess | Kernel32.ProcessQueryInformation);
                if (process == 0) return;
                handles = ReadHandles(process, out _);
            }

            if (handles is null) return;
            CloseHandlesIn(process, handles);
        }
        finally
        {
            if (process != 0) Kernel32.CloseHandle(process);
        }
    }

    /// <returns>The process's handle entries, or null with the failing NTSTATUS in <paramref name="status"/>.</returns>
    static NtDll.ProcessHandleTableEntry[]? ReadHandles(nint process, out int status)
    {
        var size = FirstHandleBufferBytes;
        while (true)
        {
            var buffer = new byte[size];
            uint needed;
            fixed (byte* p = buffer)
            {
                status = NtDll.NtQueryInformationProcess(process, NtDll.ProcessHandleInformation, p, size, out needed);
                if (status >= 0)
                {
                    // Trust nothing: the entries must lie wholly inside the buffer.
                    var count = ((NtDll.ProcessHandleSnapshotHead*)p)->NumberOfHandles;
                    var room = (size - (uint)sizeof(NtDll.ProcessHandleSnapshotHead)) / (uint)sizeof(NtDll.ProcessHandleTableEntry);
                    if (count > room) return null;
                    return new ReadOnlySpan<NtDll.ProcessHandleTableEntry>(p + sizeof(NtDll.ProcessHandleSnapshotHead), (int)count).ToArray();
                }
            }
            if (status is not (NtDll.StatusInfoLengthMismatch or NtDll.StatusBufferOverflow or NtDll.StatusBufferTooSmall)) return null;

            // The list can grow between the two calls, so ask for some room beyond what was needed.
            var next = Math.Max(size * 2, needed + 4096);
            if (next > MaxHandleBufferBytes) return null;
            size = next;
        }
    }

    static void CloseHandlesIn(nint process, NtDll.ProcessHandleTableEntry[] handles)
    {
        // Types are known by index. The File index is found by reading type names until one is "File"; a handle whose type can't
        // be read before then is skipped. Once it is found, any other index is not a File.
        uint? fileType = null;
        var notFileTypes = new HashSet<uint>();
        foreach (var entry in handles)
        {
            bool isFile;
            if (fileType is { } known) isFile = entry.ObjectTypeIndex == known;
            else if (notFileTypes.Contains(entry.ObjectTypeIndex)) isFile = false;
            else
            {
                var named = IsFileType(process, entry.HandleValue);
                if (named is not { } read) continue;
                isFile = read;
                if (isFile) fileType = entry.ObjectTypeIndex;
                else notFileTypes.Add(entry.ObjectTypeIndex);
            }

            if (isFile && !IsLocalDiskOrPipe(process, entry.HandleValue)) continue;
            CloseInProcess(process, entry.HandleValue);
        }
    }

    /// <returns>Whether the handle's type is File, or null when it can't be read.</returns>
    static bool? IsFileType(nint process, nint handle)
    {
        if (!Kernel32.DuplicateHandle(process, handle, Kernel32.GetCurrentProcess(), out var copy, 0, false, Kernel32.DuplicateSameAccess))
            return null;
        try
        {
            // The block is a UNICODE_STRING whose characters follow it. Only the type is asked: unlike the name, it never blocks.
            const int blockBytes = 1024;
            var block = stackalloc long[blockBytes / sizeof(long)];
            if (NtDll.NtQueryObject(copy, NtDll.ObjectTypeInformation, block, blockBytes, out _) < 0) return null;

            var name = (NtDll.UnicodeString*)block;
            var text = (byte*)name->Buffer;
            if (text < (byte*)block + sizeof(NtDll.UnicodeString) || text + name->Length > (byte*)block + blockBytes) return null;
            return new ReadOnlySpan<char>(name->Buffer, name->Length / sizeof(char)).SequenceEqual("File");
        }
        finally
        {
            Kernel32.CloseHandle(copy);
        }
    }

    /// <summary>
    /// Task Manager's File filter: closable only when the file is on a local device and is a disk file or a pipe. Remote files,
    /// character devices such as the console, and handles whose queries fail stay open.
    /// </summary>
    static bool IsLocalDiskOrPipe(nint process, nint handle)
    {
        if (!Kernel32.DuplicateHandle(process, handle, Kernel32.GetCurrentProcess(), out var copy, 0, false, Kernel32.DuplicateSameAccess))
            return false;
        try
        {
            byte isRemote = 1;
            if (NtDll.NtQueryInformationFile(copy, out _, &isRemote, sizeof(byte), NtDll.FileIsRemoteDeviceInformation) < 0 || isRemote != 0)
                return false;
            return Kernel32.GetFileType(copy) is Kernel32.FileTypeDisk or Kernel32.FileTypePipe;
        }
        finally
        {
            Kernel32.CloseHandle(copy);
        }
    }

    /// <summary>Closes <paramref name="handle"/> in <paramref name="process"/>. <c>DUPLICATE_CLOSE_SOURCE</c> closes it even when the copy fails.</summary>
    static void CloseInProcess(nint process, nint handle)
    {
        if (Kernel32.DuplicateHandle(process, handle, Kernel32.GetCurrentProcess(), out var copy, 0, false, Kernel32.DuplicateCloseSource))
            Kernel32.CloseHandle(copy);
    }
}
