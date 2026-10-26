namespace Tmcs.Devices.Cpu;

internal static class CpuInstanceNames
{
    /// <summary>
    /// Maps each <c>Processor Information</c> instance name to its processor index: its rank by (group, number).
    /// <c>_Total</c>, <c>group,_Total</c> and anything else unparsable get -1. PDH sorts the names as strings
    /// (<c>0,10</c> before <c>0,2</c>), so the array position says nothing.
    /// </summary>
    public static void Rank(ReadOnlySpan<string> names, Span<int> processor)
    {
        var keys = new List<(int Group, int Number, int Position)>(names.Length);
        for (var i = 0; i < names.Length; i++)
        {
            processor[i] = -1;
            if (TryParse(names[i], out var group, out var number)) keys.Add((group, number, i));
        }
        keys.Sort((a, b) => a.Group != b.Group ? a.Group.CompareTo(b.Group) : a.Number.CompareTo(b.Number));
        for (var rank = 0; rank < keys.Count; rank++) processor[keys[rank].Position] = rank;
    }

    static bool TryParse(ReadOnlySpan<char> name, out int group, out int number)
    {
        group = number = 0;
        var comma = name.IndexOf(',');
        return comma > 0 && int.TryParse(name[..comma], out group) && int.TryParse(name[(comma + 1)..], out number);
    }
}
