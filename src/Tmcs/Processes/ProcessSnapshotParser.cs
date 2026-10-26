using System.Buffers.Binary;
using System.Text;

namespace Tmcs.Processes;

/// <summary>Decodes the x64 <c>SYSTEM_PROCESS_INFORMATION</c> chain returned by <c>NtQuerySystemInformation</c>.</summary>
internal static class ProcessSnapshotParser
{
    // SYSTEM_PROCESS_INFORMATION field offsets (x64).
    const int EntrySize = 0x100;
    const int OffNextEntryOffset = 0x00;
    const int OffNumberOfThreads = 0x04;
    const int OffWorkingSetPrivateSize = 0x08;
    const int OffCreateTime = 0x20;
    const int OffUserTime = 0x28;
    const int OffKernelTime = 0x30;
    const int OffImageNameLength = 0x38;
    const int OffImageNameBuffer = 0x40;
    const int OffBasePriority = 0x48;
    const int OffUniqueProcessId = 0x50;
    const int OffInheritedFromUniqueProcessId = 0x58;
    const int OffHandleCount = 0x60;
    const int OffSessionId = 0x64;
    const int OffWorkingSetSize = 0x90;
    const int OffPagefileUsage = 0xB8;

    // SYSTEM_THREAD_INFORMATION, laid out right after each process entry.
    const int ThreadEntrySize = 0x50;
    const int OffThreadState = 0x44;
    const int OffWaitReason = 0x48;
    const uint StateWaiting = 5;
    const uint WaitReasonSuspended = 5;

    const string IdleName = "System Idle Process";

    /// <summary>
    /// Clears <paramref name="into"/> and fills it with one entry per process in <paramref name="buffer"/>.
    /// </summary>
    /// <param name="buffer">The snapshot bytes, starting at the first process entry.</param>
    /// <param name="baseAddress">
    /// The address of <c>buffer[0]</c> when the snapshot was taken; <c>ImageName.Buffer</c> pointers are resolved against it.
    /// </param>
    /// <param name="into">Receives the parsed processes, in snapshot order.</param>
    public static void Parse(ReadOnlySpan<byte> buffer, nint baseAddress, List<RawProcess> into)
    {
        into.Clear();
        int offset = 0;
        while (true)
        {
            var entry = buffer.Slice(offset, EntrySize);
            int next = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(entry[OffNextEntryOffset..]));
            int threads = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(entry[OffNumberOfThreads..]));
            int pid = checked((int)BinaryPrimitives.ReadInt64LittleEndian(entry[OffUniqueProcessId..]));

            string name = ReadName(buffer, entry, baseAddress);
            if (pid == 0 && name.Length == 0) name = IdleName;

            into.Add(new RawProcess(
                Pid: pid,
                ParentPid: checked((int)BinaryPrimitives.ReadInt64LittleEndian(entry[OffInheritedFromUniqueProcessId..])),
                CreateTime: BinaryPrimitives.ReadInt64LittleEndian(entry[OffCreateTime..]),
                Name: name,
                KernelTime: BinaryPrimitives.ReadInt64LittleEndian(entry[OffKernelTime..]),
                UserTime: BinaryPrimitives.ReadInt64LittleEndian(entry[OffUserTime..]),
                PrivateWorkingSet: BinaryPrimitives.ReadInt64LittleEndian(entry[OffWorkingSetPrivateSize..]),
                CommitSize: BinaryPrimitives.ReadInt64LittleEndian(entry[OffPagefileUsage..]),
                Threads: threads,
                Handles: (int)BinaryPrimitives.ReadUInt32LittleEndian(entry[OffHandleCount..]),
                SessionId: (int)BinaryPrimitives.ReadUInt32LittleEndian(entry[OffSessionId..]),
                BasePriority: BinaryPrimitives.ReadInt32LittleEndian(entry[OffBasePriority..]),
                IsSuspended: AllThreadsSuspended(buffer.Slice(offset + EntrySize), threads),
                WorkingSet: BinaryPrimitives.ReadInt64LittleEndian(entry[OffWorkingSetSize..])));

            if (next == 0) return;
            offset += next;
        }
    }

    static string ReadName(ReadOnlySpan<byte> buffer, ReadOnlySpan<byte> entry, nint baseAddress)
    {
        int length = BinaryPrimitives.ReadUInt16LittleEndian(entry[OffImageNameLength..]);
        long pointer = BinaryPrimitives.ReadInt64LittleEndian(entry[OffImageNameBuffer..]);
        if (length == 0 || pointer == 0) return string.Empty;
        return Encoding.Unicode.GetString(buffer.Slice(checked((int)(pointer - baseAddress)), length));
    }

    static bool AllThreadsSuspended(ReadOnlySpan<byte> threadsStart, int count)
    {
        if (count == 0) return false;
        for (int t = 0; t < count; t++)
        {
            var thread = threadsStart.Slice(t * ThreadEntrySize, ThreadEntrySize);
            if (BinaryPrimitives.ReadUInt32LittleEndian(thread[OffThreadState..]) != StateWaiting ||
                BinaryPrimitives.ReadUInt32LittleEndian(thread[OffWaitReason..]) != WaitReasonSuspended)
                return false;
        }
        return true;
    }
}
