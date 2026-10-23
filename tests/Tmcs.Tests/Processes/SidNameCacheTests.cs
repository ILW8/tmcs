using Tmcs.Processes;

namespace Tmcs.Tests.Processes;

public class SidNameCacheTests
{
    [Fact] public void Resolves_once_per_sid()
    { var calls = 0; var c = new SidNameCache(_ => { calls++; return "bob"; });
      Assert.Equal("bob", c.Resolve([1, 2, 3])); Assert.Equal("bob", c.Resolve([1, 2, 3]));
      Assert.Equal(1, calls); Assert.Equal(1, c.Count); }

    [Fact] public void Unresolvable_sid_is_cached_too()
    { var calls = 0; var c = new SidNameCache(_ => { calls++; return null; });
      Assert.Null(c.Resolve([9])); Assert.Null(c.Resolve([9])); Assert.Equal(1, calls); }

    [Fact] public void Clears_when_capacity_reached()
    { var c = new SidNameCache(s => "n" + s[0], capacity: 2);
      c.Resolve([1]); c.Resolve([2]); Assert.Equal(2, c.Count);
      Assert.Equal("n3", c.Resolve([3])); Assert.Equal(1, c.Count); }

    [Fact] public void LookupAccountName_resolves_the_well_known_system_sid_to_its_short_name()
    { byte[] localSystem = [1, 1, 0, 0, 0, 0, 0, 5, 18, 0, 0, 0]; // S-1-5-18
      Assert.Equal("SYSTEM", SidNameCache.LookupAccountName(localSystem)); }

    [Fact] public void LookupAccountName_returns_null_for_garbage()
    { Assert.Null(SidNameCache.LookupAccountName([0xFF, 0xFF, 0xFF])); }
}
