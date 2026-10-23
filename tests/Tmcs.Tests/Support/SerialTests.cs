// Some tests compare a process-wide count (GDI objects, handles) before and after a loop, so no other test may run meanwhile.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
