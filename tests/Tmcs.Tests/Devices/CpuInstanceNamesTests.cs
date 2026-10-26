using Tmcs.Devices.Cpu;

namespace Tmcs.Tests.Devices;

public class CpuInstanceNamesTests
{
    static Dictionary<string, int> Rank(params string[] names)
    {
        var result = new int[names.Length];
        CpuInstanceNames.Rank(names, result);
        return names.Zip(result).ToDictionary(p => p.First, p => p.Second);
    }

    [Fact]
    public void Single_group_in_string_sorted_order()
    {
        var map = Rank("0,0", "0,1", "0,10", "0,11", "0,2", "0,3", "0,4", "0,5", "0,6", "0,7", "0,8", "0,9", "0,_Total", "_Total");
        Assert.Equal(10, map["0,10"]);
        Assert.Equal(2, map["0,2"]);
        Assert.Equal(9, map["0,9"]);
        Assert.Equal(-1, map["0,_Total"]);
        Assert.Equal(-1, map["_Total"]);
    }

    [Fact]
    public void Two_groups_rank_by_group_then_number()
    {
        var map = Rank("0,0", "0,1", "1,0", "1,1", "0,_Total", "1,_Total", "_Total");
        Assert.Equal(2, map["1,0"]);
        Assert.Equal(3, map["1,1"]);
        Assert.Equal(-1, map["1,_Total"]);
        Assert.Equal(-1, map["_Total"]);
    }
}
