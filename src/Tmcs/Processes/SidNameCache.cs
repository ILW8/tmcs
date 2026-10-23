using Tmcs.Native;

namespace Tmcs.Processes;

/// <summary>
/// Maps SIDs to account names without repeating the lookup, which can be slow for domain accounts.
/// Unresolvable SIDs are cached too (as <c>null</c>), so a SID that cannot be resolved costs one lookup, not one per process.
/// The cache is bounded: when it is full it is emptied before the next insert. Not thread-safe.
/// </summary>
internal sealed unsafe class SidNameCache
{
    const int MaxSubAuthorities = 15;

    readonly Func<byte[], string?> _lookup;
    readonly int _capacity;
    readonly Dictionary<string, string?> _names = new();

    /// <param name="lookup">Resolves a SID that is not cached yet. Production code passes <see cref="LookupAccountName"/>.</param>
    /// <param name="capacity">The most entries kept at once.</param>
    public SidNameCache(Func<byte[], string?> lookup, int capacity = 256)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _lookup = lookup;
        _capacity = capacity;
    }

    public int Count => _names.Count;

    /// <returns>The account name for <paramref name="sid"/>, or <c>null</c> if it has none.</returns>
    public string? Resolve(byte[] sid)
    {
        var key = Convert.ToBase64String(sid);
        if (_names.TryGetValue(key, out var name)) return name;
        name = _lookup(sid);
        if (_names.Count == _capacity) _names.Clear();
        _names[key] = name;
        return name;
    }

    /// <summary>Resolves a binary SID with <c>LookupAccountSidW</c> on the local machine. Returns the short name only, without the domain.</summary>
    public static string? LookupAccountName(byte[] sid)
    {
        // A SID is revision, sub-authority count, a 6-byte authority, then 4 bytes per sub-authority.
        // Check the length so the API never reads past the end of a malformed array.
        if (sid.Length < 8 || sid[1] > MaxSubAuthorities || sid.Length != 8 + 4 * sid[1]) return null;

        fixed (byte* psid = sid)
        {
            // First call with empty buffers: it fails with ERROR_INSUFFICIENT_BUFFER and reports the sizes, in chars including the terminator.
            uint nameChars = 0, domainChars = 0;
            Advapi32.LookupAccountSidW(null, psid, null, ref nameChars, null, ref domainChars, out _);
            if (nameChars == 0) return null;

            var name = new char[nameChars];
            var domain = new char[Math.Max(domainChars, 1u)];
            fixed (char* pname = name)
            fixed (char* pdomain = domain)
            {
                nameChars = (uint)name.Length;
                domainChars = (uint)domain.Length;
                if (!Advapi32.LookupAccountSidW(null, psid, pname, ref nameChars, pdomain, ref domainChars, out _)) return null;
                return new string(pname, 0, (int)nameChars);   // on success the count excludes the terminator
            }
        }
    }
}
