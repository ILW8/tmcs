using System.Diagnostics;
using Tmcs.Icons;
using Tmcs.Native;

namespace Tmcs.Tests.Icons;

public class NativeIconTests
{
    private static string Cmd => Path.Combine(Environment.SystemDirectory, "cmd.exe");

    [Fact]
    public void Shell_loader_loads_cmd_icon_and_returns_zero_for_missing_file() => Sta.Run(() =>
    {
        var loader = new ShellIconLoader();

        var icon = loader.LoadSmallIcon(Cmd);
        Assert.NotEqual(0, icon);
        loader.DestroyIcon(icon);

        var clock = Stopwatch.StartNew();
        Assert.Equal(0, loader.LoadSmallIcon(@"C:\nope\missing.exe"));
        Assert.True(clock.ElapsedMilliseconds < 500, $"a missing file took {clock.ElapsedMilliseconds} ms");
    });

    [Fact]
    public void Generic_icon_loads() => Sta.Run(() =>
    {
        var icon = ShellIconLoader.LoadGenericIcon();
        Assert.NotEqual(0, icon);
        new ShellIconLoader().DestroyIcon(icon);
    });

    [Fact]
    public void Native_image_list_adds_replaces_and_destroys_once() => Sta.Run(() =>
    {
        var loader = new ShellIconLoader();
        var icon = loader.LoadSmallIcon(Cmd);
        try
        {
            var list = new NativeImageList(16);
            var handle = list.Handle;
            Assert.NotEqual(0, handle);

            Assert.Equal(0, list.Add(icon));
            Assert.Equal(1, list.Add(icon));
            list.Replace(0, icon);
            Assert.Equal(2, list.Count);
            Assert.Equal(2, ComCtl32.ImageList_GetImageCount(handle));

            list.Dispose();
            list.Dispose();
            Assert.Equal(0, list.Handle);
            Assert.Equal(0, ComCtl32.ImageList_GetImageCount(handle));
        }
        finally { loader.DestroyIcon(icon); }
    });

    [Fact]
    public void Image_list_count_stays_flat_under_real_path_churn() => Sta.Run(() =>
    {
        const int capacity = 8;
        var paths = Directory.EnumerateFiles(Environment.SystemDirectory, "*.exe").Take(30).ToList();
        Assert.Equal(30, paths.Count);

        var loader = new ShellIconLoader();
        var generic = ShellIconLoader.LoadGenericIcon();
        try
        {
            using var list = new NativeImageList(16);
            var store = new IconStore(list, loader, generic, capacity);

            var gdiBefore = User32.GetGuiResources(Kernel32.GetCurrentProcess(), User32.GrGdiObjects);
            for (var round = 0; round < 20; round++)
            {
                foreach (var path in paths)
                {
                    store.Acquire(path);
                    store.Release(path);
                }
            }
            var gdiAfter = User32.GetGuiResources(Kernel32.GetCurrentProcess(), User32.GrGdiObjects);

            Assert.Equal(store.SlotCount, ComCtl32.ImageList_GetImageCount(list.Handle));
            Assert.InRange(store.SlotCount, 1, 1 + capacity + 1);
            Assert.Equal(capacity, store.PooledCount);
            Assert.True(gdiAfter <= gdiBefore + 5, $"GDI objects went from {gdiBefore} to {gdiAfter}");
        }
        finally { loader.DestroyIcon(generic); }
    });
}
