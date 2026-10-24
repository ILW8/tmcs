using System.Diagnostics;
using System.Runtime.InteropServices;
using Tmcs.Icons;
using Tmcs.Native;
using Xunit.Abstractions;

namespace Tmcs.Tests.Icons;

public partial class NativeIconTests(ITestOutputHelper output)
{
    private const uint ShgfiSysIconIndex = 0x4000;
    private const uint ShgfiSmallIcon = 0x1;

    /// <summary>How many exes the system image list test loads at most.</summary>
    private const int MaxPaths = 2000;

    private static string Cmd => Path.Combine(Environment.SystemDirectory, "cmd.exe");

    /// <summary><c>SHFILEINFOW</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct ShFileInfo
    {
        public nint Icon;
        public int IconIndex;
        public uint Attributes;
        public fixed char DisplayName[260];
        public fixed char TypeName[80];
    }

    /// <returns>With <see cref="ShgfiSysIconIndex"/>: the process's shell system image list, which the caller must not destroy.</returns>
    [LibraryImport("shell32.dll", EntryPoint = "SHGetFileInfoW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint ShGetFileInfo(string path, uint attributes, ref ShFileInfo info, uint size, uint flags);

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

    // SHGetFileInfo(SHGFI_ICON) adds every newly seen icon to the process's shell system image list, which never shrinks (review
    // I1). The loader must leave that list alone.
    [Fact]
    public void Shell_loader_does_not_grow_the_system_image_list() => Sta.Run(() =>
    {
        var info = new ShFileInfo();
        var systemList = ShGetFileInfo(Cmd, 0, ref info, (uint)Marshal.SizeOf<ShFileInfo>(), ShgfiSysIconIndex | ShgfiSmallIcon);
        Assert.NotEqual(0, systemList); // the shell's own list, never to be destroyed
        var before = ComCtl32.ImageList_GetImageCount(systemList); // taken after getting the list, which can add cmd.exe's icon
        Assert.True(before > 0, $"the system image list reports {before} images");

        var paths = Directory.EnumerateFiles(Environment.SystemDirectory, "*.exe").Take(MaxPaths).ToList();
        var loader = new ShellIconLoader();
        var loaded = 0;
        foreach (var path in paths)
        {
            var icon = loader.LoadSmallIcon(path);
            if (icon == 0) continue;
            loaded++;
            loader.DestroyIcon(icon);
        }

        var after = ComCtl32.ImageList_GetImageCount(systemList);
        output.WriteLine($"{paths.Count} paths, {loaded} icons; system image list {before} -> {after}");
        Assert.Equal(before, after);
        // About 28% of System32's exes have an icon of their own (190 of 683 on Windows 10 22H2); the rest show the generic icon.
        Assert.True(loaded >= paths.Count / 5, $"only {loaded} of {paths.Count} paths gave an icon");
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
            using var list = new NativeImageList(16);
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
        }
        finally { loader.DestroyIcon(icon); }
    });

    [Fact]
    public void Image_list_count_stays_flat_under_real_path_churn() => Sta.Run(() =>
    {
        const int capacity = 8;
        var loader = new ShellIconLoader();
        // Exes with an icon of their own: the others get the generic slot, and no slot of their own to churn.
        var paths = Directory.EnumerateFiles(Environment.SystemDirectory, "*.exe").Where(HasIcon).Take(30).ToList();
        Assert.Equal(30, paths.Count);

        bool HasIcon(string path)
        {
            var icon = loader.LoadSmallIcon(path);
            if (icon == 0) return false;
            loader.DestroyIcon(icon);
            return true;
        }

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
            Assert.Equal(1 + capacity + 1, store.SlotCount);   // every load succeeds: generic slot + full pool + the slot just freed by the last eviction
            Assert.Equal(capacity, store.PooledCount);
            Assert.True(gdiAfter <= gdiBefore + 5, $"GDI objects went from {gdiBefore} to {gdiAfter}");
        }
        finally { loader.DestroyIcon(generic); }
    });
}
