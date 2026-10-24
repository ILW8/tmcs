using Tmcs.Native;

namespace Tmcs.Processes;

/// <summary>
/// Reads the process list with <c>NtQuerySystemInformation(SystemProcessInformation)</c> into one reusable buffer.
/// The buffer grows when the API reports it is too small and never shrinks, so steady-state sampling allocates none.
/// </summary>
internal sealed unsafe class SystemProcessReader : IProcessSnapshotSource
{
    const int DefaultBufferSize = 512 * 1024;

    byte[] _buffer;

    /// <param name="initialBufferSize">Starting buffer size in bytes, at least 1, since growing doubles it. Tests lower it to exercise the growth path.</param>
    public SystemProcessReader(int initialBufferSize = DefaultBufferSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(initialBufferSize);
        _buffer = new byte[initialBufferSize];
    }

    public void Read(List<RawProcess> into)
    {
        while (true)
        {
            // The kernel writes ImageName.Buffer pointers into the buffer, so it must stay pinned until Parse has resolved them.
            fixed (byte* p = _buffer)
            {
                int status = NtDll.NtQuerySystemInformation(NtDll.SystemProcessInformation, p, _buffer.Length, out _);
                if (status >= 0)
                {
                    ProcessSnapshotParser.Parse(_buffer, (nint)p, into);
                    return;
                }
                if (status != NtDll.StatusInfoLengthMismatch)
                    throw new InvalidOperationException($"NtQuerySystemInformation(SystemProcessInformation) failed: NTSTATUS 0x{status:X8}.");
            }
            _buffer = new byte[checked(_buffer.Length * 2)];
        }
    }
}
