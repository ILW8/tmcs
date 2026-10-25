using System.Runtime.InteropServices;
using Tmcs.Devices;
using Tmcs.Devices.Gpu;
using Tmcs.Native;
using Tmcs.Performance;
using Tmcs.Performance.Views;
using Tmcs.Processes;
using Tmcs.Settings;
using Tmcs.Ui;

namespace Tmcs.Tests.Performance;

// No test opens the menu or clicks: Track would show a real popup, and focusing a control activates its window.
public sealed partial class SidebarTests
{
    const long Second = 10_000_000;

    // Written out rather than built from the GUIDs, so a wrong key or settings format shows up.
    const string KeyA = "net:{aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa}";
    const string KeyB = "net:{bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb}";
    const string KeyC = "net:{cccccccc-cccc-cccc-cccc-cccccccccccc}";
    const string HiddenA = "{aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa}";
    const string HiddenB = "{bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb}";
    const string HiddenAbsent = "{dddddddd-dddd-dddd-dddd-dddddddddddd}";

    const uint WmKeyDown = 0x0100;
    const uint MfByPosition = 0x400;
    const uint MfGrayed = 0x1;
    const uint MfChecked = 0x8;
    const uint HideAdapter = 400;
    const uint ShowHiddenAdapters = 401;

    [LibraryImport("user32.dll")]
    private static partial int GetMenuItemCount(nint menu);

    [LibraryImport("user32.dll")]
    private static partial uint GetMenuState(nint menu, uint item, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "GetMenuStringW")]
    private static unsafe partial int GetMenuString(nint menu, uint item, char* buffer, int capacity, uint flags);

    static uint GuiResources(uint flags) => User32.GetGuiResources(Kernel32.GetCurrentProcess(), flags);

    /// <summary>The owner's machine, sampled once, in a view laid out at Task Manager's tab size and 96 DPI.</summary>
    sealed class Rig : IDisposable
    {
        public readonly DeviceSources Sources = FakeDeviceSources.CreateOwnerMachine();
        public readonly AppSettings Settings = AppSettings.CreateDefault();
        public readonly PerformanceModel Model;
        public readonly PerformanceView View;
        long _time;

        public Rig(params string[] hidden)
        {
            Settings.HiddenAdapters.AddRange(hidden);
            Model = new PerformanceModel(Sources);
            Sample();
            View = new PerformanceView(Model, Settings);
            View.ApplyDpi(96);
            View.ClientSize = new Size(1278, 623);
        }

        public Sidebar Sidebar => View.Sidebar;

        public Device Device(string key) => Model.Devices.Single(d => d.Key == key);

        public void Sample()
        {
            _time += Second;
            Model.Sample(_time, Array.Empty<RawProcess>());
        }

        /// <summary>Lists the owner's adapters without <paramref name="gone"/>; the next sample's rescan drops them.</summary>
        public void RemoveAdapters(params Guid[] gone)
        {
            var network = Sources.FakeNetwork();
            network.Adapters = [.. FakeDeviceSources.OwnerAdapters().Where(a => !gone.Contains(a.InterfaceGuid))];
            network.Changed = true;
        }

        /// <summary>The visible pane shows <paramref name="device"/>, and every other pane shows nothing.</summary>
        // ReSharper disable once ParameterOnlyUsedForPreconditionCheck.Local
        public void AssertShows(Device device)
        {
            Assert.Same(device, View.VisiblePane.Device);
            Assert.Same(View.Panes[(int)device.Kind], View.VisiblePane);
            // ReSharper disable once ParameterOnlyUsedForPreconditionCheck.Local
            Assert.Single(View.Panes, p => p.Visible);
            Assert.All(View.Panes, p => Assert.True(p == View.VisiblePane || p.Device is null));
        }

        public void Dispose()
        {
            View.Dispose();
            Model.Dispose();
        }
    }

    [Fact]
    public void Entries_stack_with_task_managers_heights() => Sta.Run(() =>
    {
        using var rig = new Rig();
        var sidebar = rig.Sidebar;

        Assert.Equal(rig.Model.Devices, sidebar.Entries);
        Assert.Equal(20, sidebar.Entries.Count);
        int[] tops = [0, 60, 120, 186, 252, 318, 384, .. Enumerable.Range(0, 12).Select(i => 450 + 66 * i), 1242];
        Assert.Equal(tops, Enumerable.Range(0, 20).Select(i => sidebar.EntryBounds(i).Y));
        Assert.All(Enumerable.Range(0, 20), i => Assert.Equal(0, sidebar.EntryBounds(i).X));
        Assert.Equal(new Size(194, 60), sidebar.EntryBounds(0).Size); // CPU
        Assert.Equal(new Size(194, 60), sidebar.EntryBounds(1).Size); // Memory
        Assert.Equal(new Size(194, 66), sidebar.EntryBounds(2).Size); // Disk 0
        Assert.Equal(new Size(204, 66), sidebar.EntryBounds(7).Size); // the first adapter
        Assert.Equal(new Size(204, 66), sidebar.EntryBounds(19).Size); // GPU 0
        Assert.Equal(new Size(0, 1308), sidebar.AutoScrollMinSize);
        Assert.Equal(rig.View.SidebarSlot, sidebar.Bounds);

        // A click hits the same rectangles: CPU's is 194 wide, an adapter's 204.
        Assert.Equal(0, sidebar.EntryAt(new Point(5, 59)));
        Assert.Equal(1, sidebar.EntryAt(new Point(5, 60)));
        Assert.Equal(-1, sidebar.EntryAt(new Point(194, 30)));
        Assert.Equal(7, sidebar.EntryAt(new Point(200, 460)));
        Assert.Equal(-1, sidebar.EntryAt(new Point(5, 1308)));
    });

    [Fact]
    public void Selected_entry_is_filled_and_mini_graphs_sit_at_10_10() => Sta.Run(() =>
    {
        using var rig = new Rig();
        var sidebar = rig.Sidebar;
        Assert.True(sidebar.SelectKey(KeyA)); // the first adapter: top 450, 204 wide
        using var bmp = new Bitmap(214, 568);
        using var g = Graphics.FromImage(bmp);

        sidebar.PaintTo(g, sidebar.ClientRectangle);

        int Px(int x, int y) => bmp.GetPixel(x, y).ToArgb() & 0xFFFFFF;
        Assert.Equal(0xCDE8FF, Px(0, 450));
        Assert.Equal(0xCDE8FF, Px(203, 515));
        Assert.Equal(0xFFFFFF, Px(204, 480));
        Assert.Equal(0xFFFFFF, Px(0, 516)); // the next adapter
        Assert.Equal(0xFFFFFF, Px(5, 5)); // CPU, no longer selected
        Assert.Equal(0xB66D2D, Px(10, 460)); // the network graph's top corner
        Assert.Equal(0xFFFFFF, Px(40, 480)); // its interior, which stays white

        // The mini graphs: 60 × (height − 20) at entry + (10, 10), in each device's palette, with no grid.
        Assert.Equal(0x3A94C7, Px(10, 10)); // CPU top corner
        Assert.Equal(0x1F85BF, Px(69, 49)); // CPU bottom corner: 60 × 40
        Assert.Equal(0xFFFFFF, Px(40, 14)); // no grid row
        Assert.Equal(0x9F3BBB, Px(10, 70)); // Memory
        Assert.Equal(0x58AC1A, Px(69, 175)); // Disk 0's bottom corner: 60 × 46
    });

    [Fact]
    public void Sub_lines_place_their_texts_as_task_manager_does() => Sta.Run(() =>
    {
        using var rig = new Rig(); // every rate 0: S: 0 R: 0 Kbps, as on capture 00's Tailscale entry
        rig.Sources.FakeCpu().Total = 14;
        rig.Sample();
        var sidebar = rig.Sidebar;
        Assert.True(sidebar.SelectKey("memory")); // so the CPU entry is white
        using var bmp = new Bitmap(214, 568);
        using var g = Graphics.FromImage(bmp);

        sidebar.PaintTo(g, sidebar.ClientRectangle);

        // Where each run of inked columns starts. Capture 00, less the entry's x of 12: S: at 81, 0 at 94, R: at 104, then 0 and
        // Kbps at 118 and 127. Each segment starts 1 px after the one before ends.
        Assert.Equal([81, 94, 104, 118, 127], RunStarts(bmp, 450 + 41, 15, 80, 200));
        // CPU: 14% at 81, then 4.50 GHz 5 px after its 22 px, at 108 (capture 00: window x 120).
        Assert.Equal(108, RunStarts(bmp, 26, 15, 80, 200).First(x => x > 100));
    });

    [Fact]
    public void Hidden_adapters_are_left_out_until_shown() => Sta.Run(() =>
    {
        using var rig = new Rig(HiddenB);
        var sidebar = rig.Sidebar;
        var b = rig.Device(KeyB);
        var raised = 0;
        rig.View.SettingsChanged += (_, _) => raised++;

        Assert.DoesNotContain(b, sidebar.Entries);
        Assert.Equal(19, sidebar.Entries.Count);
        Assert.Equal(1308 - 66, sidebar.AutoScrollMinSize.Height);

        sidebar.OnCommand(CommandIds.ShowHiddenAdapters, null);
        Assert.Equal(rig.Model.Devices, sidebar.Entries); // in its place, between A and C
        Assert.Equal(1308, sidebar.AutoScrollMinSize.Height);

        sidebar.OnCommand(CommandIds.ShowHiddenAdapters, null);
        Assert.DoesNotContain(b, sidebar.Entries);

        // Showing them is for the session only: no setting changes.
        Assert.Equal([HiddenB], rig.Settings.HiddenAdapters);
        Assert.Equal(0, raised);
    });

    [Fact]
    public void Hide_toggles_the_setting_and_raises_settings_changed() => Sta.Run(() =>
    {
        using var rig = new Rig();
        var sidebar = rig.Sidebar;
        var b = rig.Device(KeyB);
        var raised = 0;
        rig.View.SettingsChanged += (_, _) => raised++; // the view forwards the sidebar's event

        sidebar.OnCommand(CommandIds.HideAdapter, b);

        Assert.Equal([HiddenB], rig.Settings.HiddenAdapters);
        Assert.Equal(1, raised);
        Assert.DoesNotContain(b, sidebar.Entries);

        // Hidden and shown, it is checked, and the same item un-hides it.
        sidebar.OnCommand(CommandIds.ShowHiddenAdapters, null);
        Assert.Equal(MfChecked, State(sidebar.PrepareMenu(b), HideAdapter) & MfChecked);
        sidebar.OnCommand(CommandIds.HideAdapter, b);
        Assert.Empty(rig.Settings.HiddenAdapters);
        Assert.Equal(2, raised);
        Assert.Equal(0u, State(sidebar.PrepareMenu(b), HideAdapter) & MfChecked);
        sidebar.OnCommand(CommandIds.ShowHiddenAdapters, null);
        Assert.Contains(b, sidebar.Entries);

        // Only adapters hide.
        sidebar.OnCommand(CommandIds.HideAdapter, rig.Model.Cpu);
        sidebar.OnCommand(CommandIds.HideAdapter, null);
        Assert.Empty(rig.Settings.HiddenAdapters);
        Assert.Equal(2, raised);
        Assert.Equal(rig.Model.Devices, sidebar.Entries);
    });

    [Fact]
    public void Hiding_the_selected_adapter_selects_the_entry_above() => Sta.Run(() =>
    {
        using var rig = new Rig();
        var sidebar = rig.Sidebar;
        Assert.True(sidebar.SelectKey(KeyB));
        rig.AssertShows(rig.Device(KeyB));
        var changes = 0;
        sidebar.SelectionChanged += () => changes++;

        sidebar.OnCommand(CommandIds.HideAdapter, rig.Device(KeyB));

        Assert.Equal(KeyA, sidebar.SelectedKey);
        Assert.Equal(1, changes);
        rig.AssertShows(rig.Device(KeyA));

        // Above the first adapter is the last disk: the pane changes kind, and the network pane lets go of its adapter.
        sidebar.OnCommand(CommandIds.HideAdapter, rig.Device(KeyA));

        Assert.Equal("disk:4", sidebar.SelectedKey);
        Assert.Equal(2, changes);
        rig.AssertShows(rig.Device("disk:4"));
    });

    [Fact]
    public void Removed_selected_device_falls_back_on_the_same_sample() => Sta.Run(() =>
    {
        using var rig = new Rig();
        var sidebar = rig.Sidebar;
        Assert.True(sidebar.SelectKey(KeyB));
        var b = rig.Device(KeyB);

        rig.RemoveAdapters(FakeDeviceSources.B);
        rig.Sample();

        Assert.DoesNotContain(rig.Model.Devices, d => d.Key == KeyB); // dropped on this sample
        Assert.Equal(KeyA, sidebar.SelectedKey);
        rig.AssertShows(rig.Device(KeyA));
        Assert.DoesNotContain(b, sidebar.Entries);
        Assert.All(rig.View.Panes, p => Assert.NotSame(b, p.Device));

        // Two at once, the selected one first of its kind: the walk up passes over the other and lands on the last disk.
        Assert.True(sidebar.SelectKey(KeyC));
        rig.RemoveAdapters(FakeDeviceSources.A, FakeDeviceSources.B, FakeDeviceSources.C);
        rig.Sample();

        Assert.Equal("disk:4", sidebar.SelectedKey);
        rig.AssertShows(rig.Device("disk:4"));
        Assert.Equal(rig.Model.Devices, sidebar.Entries);
    });

    [Fact]
    public void Selection_follows_a_gpu_whose_luid_changed() => Sta.Run(() =>
    {
        using var rig = new Rig();
        var sidebar = rig.Sidebar;
        Assert.True(sidebar.SelectFirst(DeviceKind.Gpu));
        Assert.Equal("gpu:1d8c7", sidebar.SelectedKey);
        var old = rig.Device("gpu:1d8c7");

        rig.Sources.FakeGpu().Gpus = [FakeDeviceSources.Gpu(0x2000)]; // GPU 0, Fake GPU: the same title and name
        rig.Model.MarkDevicesChanged();
        rig.Sample();

        Assert.Equal("gpu:2000", sidebar.SelectedKey);
        rig.AssertShows(rig.Device("gpu:2000"));
        Assert.All(rig.View.Panes, p => Assert.NotSame(old, p.Device));
    });

    [Fact]
    public void Select_first_picks_the_first_listed_entry_of_a_kind() => Sta.Run(() =>
    {
        using var rig = new Rig(HiddenA);

        Assert.True(rig.Sidebar.SelectFirst(DeviceKind.Network));
        Assert.Equal(KeyB, rig.Sidebar.SelectedKey);
        Assert.True(rig.Sidebar.SelectFirst(DeviceKind.Disk));
        Assert.Equal("disk:0", rig.Sidebar.SelectedKey);
        rig.AssertShows(rig.Device("disk:0"));

        rig.Sources.FakeGpu().Gpus = [];
        rig.Model.MarkDevicesChanged();
        rig.Sample();
        Assert.False(rig.Sidebar.SelectFirst(DeviceKind.Gpu));
        Assert.Equal("disk:0", rig.Sidebar.SelectedKey);
    });

    [Fact]
    public void Dropped_devices_are_released_while_the_details_tab_shows() => Sta.Run(() =>
    {
        using var rig = new Rig();
        using var form = new OffscreenForm { ClientSize = new Size(1278, 650) };
        var tabs = new MainForm.TabStrip { Dock = DockStyle.Fill };
        var performance = new TabPage("Performance");
        var details = new TabPage("Details");
        rig.View.Dock = DockStyle.Fill;
        performance.Controls.Add(rig.View);
        tabs.TabPages.Add(performance);
        tabs.TabPages.Add(details);
        form.Controls.Add(tabs);
        form.ShowOffscreen();
        Assert.True(rig.Sidebar.SelectKey(KeyC));
        tabs.ShowPage(details);
        Application.DoEvents();
        Assert.False(rig.Sidebar.Visible);
        var c = rig.Device(KeyC);

        rig.RemoveAdapters(FakeDeviceSources.C);
        rig.Sample();

        Assert.DoesNotContain(c, rig.Sidebar.Entries);
        Assert.Equal(rig.Model.Devices, rig.Sidebar.Entries);
        Assert.Equal(KeyB, rig.Sidebar.SelectedKey);
        Assert.All(rig.View.Panes, p => Assert.NotSame(c, p.Device));
    });

    [Fact]
    public void Show_hidden_count_and_state() => Sta.Run(() =>
    {
        using (var rig = new Rig(HiddenB, HiddenAbsent)) // only B is listed
        {
            var sidebar = rig.Sidebar;

            var menu = sidebar.PrepareMenu(null);
            Assert.Equal(["&Show hidden adapters (1)"], Texts(menu));
            Assert.Equal(0u, State(menu, ShowHiddenAdapters) & (MfGrayed | MfChecked));

            sidebar.OnCommand(CommandIds.ShowHiddenAdapters, null);
            var adapterMenu = sidebar.PrepareMenu(rig.Device(KeyA));
            Assert.Equal(["&Hide", "", "&Show hidden adapters (1)"], Texts(adapterMenu));
            Assert.Equal(MfChecked, State(adapterMenu, ShowHiddenAdapters) & (MfGrayed | MfChecked));
            Assert.Equal(0u, State(adapterMenu, HideAdapter) & MfChecked); // A isn't hidden
            Assert.Equal(MfChecked, State(sidebar.PrepareMenu(rig.Device(KeyB)), HideAdapter) & MfChecked);

            // Any entry but an adapter, and the space below the entries, get the short menu.
            Assert.Equal(["&Show hidden adapters (1)"], Texts(sidebar.PrepareMenu(rig.Model.Cpu)));
        }

        using (var rig = new Rig())
        {
            var sidebar = rig.Sidebar;

            var menu = sidebar.PrepareMenu(null);
            Assert.Equal(["&Show hidden adapters (0)"], Texts(menu));
            Assert.Equal(MfGrayed, State(menu, ShowHiddenAdapters) & (MfGrayed | MfChecked));

            // Turned on, it stays enabled so it can be turned off.
            sidebar.OnCommand(CommandIds.ShowHiddenAdapters, null);
            Assert.Equal(MfChecked, State(sidebar.PrepareMenu(null), ShowHiddenAdapters) & (MfGrayed | MfChecked));
        }
    });

    [Fact]
    public void Up_and_down_move_the_selection() => Sta.Run(() =>
    {
        using var rig = new Rig();
        using var form = new OffscreenForm { ClientSize = new Size(1278, 623) };
        form.Controls.Add(rig.View);
        form.ShowOffscreen();
        var sidebar = rig.Sidebar;

        // Input keys: they reach the sidebar instead of moving the focus to another control.
        foreach (var key in new[] { Keys.Up, Keys.Down })
        {
            var m = Message.Create(sidebar.Handle, (int)WmKeyDown, (nint)key, 0);
            Assert.False(sidebar.PreProcessMessage(ref m));
        }

        Key(Keys.Up);
        Assert.Equal("cpu", sidebar.SelectedKey); // nothing above the first entry
        Key(Keys.Down);
        Assert.Equal("memory", sidebar.SelectedKey);
        rig.AssertShows(rig.Model.Memory);
        Key(Keys.Up);
        Assert.Equal("cpu", sidebar.SelectedKey);
        rig.AssertShows(rig.Model.Cpu);

        for (var i = 0; i < 19; i++) Key(Keys.Down);
        Assert.Equal("gpu:1d8c7", sidebar.SelectedKey);
        Assert.True(sidebar.VerticalScroll.Visible);
        AssertInView(19);
        Key(Keys.Down);
        Assert.Equal("gpu:1d8c7", sidebar.SelectedKey); // nothing below the last

        for (var i = 0; i < 19; i++) Key(Keys.Up);
        Assert.Equal("cpu", sidebar.SelectedKey);
        Assert.Equal(0, sidebar.AutoScrollPosition.Y);

        void Key(Keys key) => User32.SendMessage(sidebar.Handle, WmKeyDown, (nint)key, 0);

        void AssertInView(int index)
        {
            var entry = sidebar.EntryBounds(index);
            var top = -sidebar.AutoScrollPosition.Y;
            Assert.InRange(entry.Top, top, top + sidebar.ClientSize.Height - entry.Height);
        }
    });

    [Fact]
    public void Painting_creates_no_gdi_or_user_objects() => Sta.Run(() =>
    {
        using var rig = new Rig();
        // Values on every line, so the fills, the lines and every text run.
        rig.Sources.FakeCpu().Total = 37;
        rig.Sources.FakeGpu().Counters = new GpuCounters(14, 14, 0, 0, 0, 1L << 30, 1L << 28, 38);
        for (var i = 1; i <= 10; i++)
        {
            rig.Sources.FakeNetwork().Counters = new((ulong)i * 1_000_000, (ulong)i * 100_000, IsUp: true);
            rig.Sample();
        }

        var sidebar = rig.Sidebar;
        Assert.True(sidebar.SelectKey(KeyA));
        sidebar.Size = new Size(214, 1308); // every entry paints
        // A buffer on a memory DC, as OnPaint gets: text drawn on a bitmap's Graphics copies the bitmap for every run.
        using var bmp = new Bitmap(1, 1);
        using var target = Graphics.FromImage(bmp);
        using var context = new BufferedGraphicsContext();
        using var buffer = context.Allocate(target, sidebar.ClientRectangle);
        var g = buffer.Graphics;
        sidebar.PaintTo(g, sidebar.ClientRectangle);
        // Objects earlier tests left to the finalizer are released here, not inside the counted paints.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        var gdi = GuiResources(0);
        var user = GuiResources(1);

        for (var i = 0; i < 1000; i++) sidebar.PaintTo(g, sidebar.ClientRectangle);

        Assert.Equal(gdi, GuiResources(0));
        Assert.Equal(user, GuiResources(1));
    });

    static uint State(NativeMenu menu, uint id) => GetMenuState(menu.Handle, id, 0);

    /// <summary>
    /// The first column of each run of columns from <paramref name="left"/> to <paramref name="right"/> that holds a non-white
    /// pixel in rows <paramref name="top"/> to <paramref name="top"/> + <paramref name="height"/>, where runs are split by 2 or
    /// more white columns.
    /// </summary>
    static int[] RunStarts(Bitmap bmp, int top, int height, int left, int right)
    {
        var starts = new List<int>();
        var gap = 2;
        for (var x = left; x < right; x++)
        {
            var ink = false;
            for (var y = top; y < top + height && !ink; y++) ink = (bmp.GetPixel(x, y).ToArgb() & 0xFFFFFF) != 0xFFFFFF;
            if (ink && gap >= 2) starts.Add(x);
            gap = ink ? 0 : gap + 1;
        }

        return [.. starts];
    }

    static unsafe string[] Texts(NativeMenu menu)
    {
        var texts = new string[GetMenuItemCount(menu.Handle)];
        var buffer = stackalloc char[64];
        for (var i = 0; i < texts.Length; i++)
            texts[i] = new string(buffer, 0, GetMenuString(menu.Handle, (uint)i, buffer, 64, MfByPosition));
        return texts;
    }
}
