using Tmcs.Devices;
using Tmcs.Devices.Network;
using Tmcs.Native;
using Tmcs.Performance;
using Tmcs.Performance.Views;
using Tmcs.Processes;
using Tmcs.Settings;

namespace Tmcs.Tests.Performance;

public sealed class PerformanceViewTests
{
    static uint GuiResources(uint flags) => User32.GetGuiResources(Kernel32.GetCurrentProcess(), flags);

    /// <summary>A view over the fake machine, laid out at Task Manager's tab size and 96 DPI, whatever the test host's DPI is.</summary>
    sealed class Rig : IDisposable
    {
        public readonly PerformanceModel Model = new(FakeDeviceSources.Create());
        public readonly PerformanceView View;

        public Rig()
        {
            Model.Sample(10_000_000, Array.Empty<RawProcess>());
            View = new PerformanceView(Model, AppSettings.CreateDefault());
            View.ApplyDpi(96);
            View.ClientSize = new Size(1278, 623);
        }

        /// <summary>The view, then the sidebar and the pane that shows, each at its place in the view.</summary>
        public void Paint(Graphics g)
        {
            View.PaintTo(g);
            var sidebar = View.Sidebar;
            g.TranslateTransform(sidebar.Location.X, sidebar.Location.Y);
            sidebar.PaintTo(g, sidebar.ClientRectangle);
            g.ResetTransform();
            var pane = View.VisiblePane;
            g.TranslateTransform(pane.Location.X, pane.Location.Y);
            pane.PaintTo(g);
            g.ResetTransform();
        }

        public void Dispose()
        {
            View.Dispose();
            Model.Dispose();
        }
    }

    /// <summary>A pane whose content is taller than Task Manager's, like the GPU pane's.</summary>
    sealed class TallPane(PaneFonts fonts) : PerformancePane(fonts)
    {
        public override int ContentHeight => 615;

        protected override void PaintBody(Graphics g) { }
    }

    [Fact]
    public void Text_widths_match_the_captures() => Sta.Run(() =>
    {
        using var fonts = new PaneFonts(96);

        Assert.Equal(53, PaneText.Width("Utilization", fonts.Label));
        Assert.Equal(100, PaneText.Width("Logical processors:", fonts.Label));
        Assert.Equal(79, PaneText.Width("4.42 GHz", fonts.Value));
        Assert.Equal(100, PaneText.Width("25:17:28:41", fonts.Value));
        Assert.Equal(140, PaneText.Width("38.0 GB (3.6 GB)", fonts.Value));
    });

    [Fact]
    public void Fonts_are_segoe_ui_in_pixels() => Sta.Run(() =>
    {
        using var fonts = new PaneFonts(96);

        Font[] all = [fonts.Title, fonts.Subtitle, fonts.Caption, fonts.Label, fonts.Value, fonts.Engine];
        Assert.Equal([28f, 16f, 11f, 12f, 20f, 13f], all.Select(f => f.Size));
        Assert.All(all, f => Assert.Equal(("Segoe UI", GraphicsUnit.Pixel, FontStyle.Regular), (f.Name, f.Unit, f.Style)));
    });

    [Fact]
    public void Layout_matches_task_manager_at_96_dpi() => Sta.Run(() =>
    {
        using var rig = new Rig();

        Assert.Equal(new Rectangle(4, 7, 214, 568), rig.View.SidebarSlot);
        Assert.Equal(rig.View.SidebarSlot, rig.View.Sidebar.Bounds);
        Assert.Equal(new Rectangle(226, 0, 1052, 575), rig.View.VisiblePane.Bounds);
        Assert.All(rig.View.Panes, p => Assert.Equal(new Rectangle(226, 0, 1052, 575), p.Bounds));

        using var bmp = new Bitmap(1278, 623);
        using (var g = Graphics.FromImage(bmp)) rig.View.PaintTo(g);
        Assert.Equal(0xD9D9D9, bmp.GetPixel(218, 17).ToArgb() & 0xFFFFFF);
        Assert.Equal(0xD9D9D9, bmp.GetPixel(218, 570).ToArgb() & 0xFFFFFF); // to height − 53
        Assert.Equal(0xFFFFFF, bmp.GetPixel(218, 16).ToArgb() & 0xFFFFFF);
        Assert.Equal(0xFFFFFF, bmp.GetPixel(218, 571).ToArgb() & 0xFFFFFF);
        Assert.Equal(0xFFFFFF, bmp.GetPixel(217, 300).ToArgb() & 0xFFFFFF);
        Assert.Equal(0xFFFFFF, bmp.GetPixel(219, 300).ToArgb() & 0xFFFFFF);
        Assert.Equal(0xFFFFFF, bmp.GetPixel(600, 610).ToArgb() & 0xFFFFFF); // the empty bottom band
    });

    [Fact]
    public void One_pane_per_kind_and_only_the_selected_devices_shows() => Sta.Run(() =>
    {
        using var rig = new Rig();
        var view = rig.View;

        Assert.Equal(Enum.GetValues<DeviceKind>().Length, view.Panes.Count);
        Assert.Equal("cpu", view.Sidebar.SelectedKey);
        Assert.Same(rig.Model.Cpu, view.VisiblePane.Device);
        Assert.Single(view.Panes, p => p.Visible);
        Assert.All(view.Panes, p => Assert.True(p == view.VisiblePane || p.Device is null));

        var disk = rig.Model.Devices.Single(d => d.Key == "disk:4");
        var cpuPane = view.VisiblePane;
        Assert.True(view.Sidebar.SelectKey("disk:4"));

        Assert.Equal("disk:4", view.Sidebar.SelectedKey);
        Assert.Same(disk, view.VisiblePane.Device);
        Assert.True(view.VisiblePane.Visible);
        Assert.Single(view.Panes, p => p.Visible);
        Assert.Null(cpuPane.Device); // a hidden pane holds no device

        var otherDisk = rig.Model.Devices.Single(d => d.Key == "disk:0");
        var diskPane = view.VisiblePane;
        Assert.True(view.Sidebar.SelectKey("disk:0"));
        Assert.Same(diskPane, view.VisiblePane); // reused for every device of its kind
        Assert.Same(otherDisk, diskPane.Device);

        Assert.False(view.Sidebar.SelectKey("disk:9"));
        Assert.Equal("disk:0", view.Sidebar.SelectedKey);
    });

    [Fact]
    public void Refreshing_brings_the_visible_panes_scroll_range_up_to_date() => Sta.Run(() =>
    {
        // A Wi-Fi adapter with no connection details yet: no SSID or signal row, so its pane needs 288 px, and it is 290 tall.
        var sources = FakeDeviceSources.Create();
        var network = sources.FakeNetwork();
        network.Adapters = [new AdapterInfo(FakeDeviceSources.A, 1, "Wi-Fi 2", "Fake Wi-Fi adapter", null, true, "10.0.0.2", "fe80::1")];
        using var model = new PerformanceModel(sources);
        model.Sample(10_000_000, Array.Empty<RawProcess>());
        var view = new PerformanceView(model, AppSettings.CreateDefault()) { Bounds = new Rectangle(0, 0, 1278, 338) };
        using var form = new OffscreenForm { ClientSize = new Size(1278, 338) };
        form.Controls.Add(view);
        form.ShowOffscreen();
        view.ApplyDpi(96); // whatever the host's DPI is
        Assert.True(view.Sidebar.SelectFirst(DeviceKind.Network));
        var pane = view.VisiblePane;
        Application.DoEvents();
        Assert.Equal(290, pane.Height);
        Assert.False(pane.VerticalScroll.Visible);

        // The same device gains its SSID and signal rows, 303 px, with no rescan and no new selection.
        network.Wifi = new WifiInfo("frame_4BD93CDF", "802.11ax", 60);
        model.Sample(20_000_000, Array.Empty<RawProcess>());
        Application.DoEvents();
        Assert.Same(pane, view.VisiblePane);
        Assert.False(pane.VerticalScroll.Visible); // nothing has refreshed it yet

        view.RefreshVisible();
        Application.DoEvents();

        Assert.True(pane.VerticalScroll.Visible);
    });

    [Fact]
    public void Pane_geometry() => Sta.Run(() =>
    {
        using var fonts = new PaneFonts(96);
        using var form = new OffscreenForm { ClientSize = new Size(1052, 575) };
        var pane = new TitlePane(fonts) { Bounds = new Rectangle(0, 0, 1052, 575) };
        form.Controls.Add(pane);
        form.ShowOffscreen();

        Assert.False(pane.VerticalScroll.Visible);
        Assert.Equal(19, pane.ContentLeft);
        Assert.Equal(1028, pane.ContentRight);
        Assert.Equal(53, pane.CaptionY);
        Assert.Equal(69, pane.GraphTop);
        Assert.Equal(555, pane.InfoBottom);

        form.Controls.Remove(pane);
        pane.Dispose();
        var tall = new TallPane(fonts) { Bounds = new Rectangle(0, 0, 1052, 575) };
        form.Controls.Add(tall);
        Application.DoEvents();

        Assert.True(tall.VerticalScroll.Visible);
        Assert.False(tall.HorizontalScroll.Visible);
        Assert.Equal(1011, tall.ContentRight);
        Assert.Equal(595, tall.InfoBottom);
    });

    [Fact]
    public void Dpi_changes_recreate_fonts_and_keep_gdi_flat() => Sta.Run(() =>
    {
        using var rig = new Rig();
        var view = rig.View;
        using var bmp = new Bitmap(1278, 623);
        using var g = Graphics.FromImage(bmp);
        var at96 = PaneText.Width("Utilization", view.Fonts.Label);
        FillWinFormsFontCache();
        uint gdi = 0, user = 0;

        for (var round = 0; round < 50; round++)
        {
            view.ApplyDpi(96);
            rig.Paint(g);
            var before = view.Fonts;
            var oldTitle = before.Title;
            view.ApplyDpi(144);
            rig.Paint(g);

            Assert.NotSame(before, view.Fonts); // recreated on the change
            Assert.Throws<ArgumentException>(() => oldTitle.GetHeight()); // and the old ones disposed
            Assert.All(view.Panes, p => Assert.Same(view.Fonts, p.Fonts));
            Assert.Same(view.Fonts, view.Sidebar.Fonts);
            Assert.Equal(42f, view.Fonts.Title.Size);
            // Scaled once (about 1.5×), not twice (2.25×). GDI hints every glyph, so the ratio isn't exact: 53 px becomes 78 at 18 px.
            var at144 = PaneText.Width("Utilization", view.Fonts.Label);
            Assert.InRange(at144 / (double)at96, 1.35, 1.65);

            if (round == 0)
            {
                // Objects earlier tests left to the finalizer are released here, not inside the counted rounds.
                GC.Collect();
                GC.WaitForPendingFinalizers();
                gdi = GuiResources(0);
                user = GuiResources(1);
            }
        }

        Assert.Equal(gdi, GuiResources(0));
        Assert.Equal(user, GuiResources(1));

        var fonts = view.Fonts;
        view.ApplyDpi(144);
        Assert.Same(fonts, view.Fonts); // the same DPI again changes nothing
    });

    /// <summary>
    /// TextRenderer keeps an HFONT per Font instance in a cache of fixed size, so each round's new fonts add HFONTs until it is
    /// full, and from then on evict as many as they add. Filling it first leaves only tmcs's own objects in the counts.
    /// </summary>
    static void FillWinFormsFontCache()
    {
        for (var i = 0; i < 64; i++)
        {
            using var font = new Font("Segoe UI", 8 + i / 4f, FontStyle.Regular, GraphicsUnit.Pixel);
            TextRenderer.MeasureText("x", font);
        }
    }

    [Fact]
    public void Dpi_change_repaints_the_visible_pane() => Sta.Run(() =>
    {
        using var rig = new Rig();
        using var form = new OffscreenForm { ClientSize = new Size(1278, 623) };
        form.Controls.Add(rig.View);
        form.ShowOffscreen(); // the view applies the host's DPI as its window is created
        var pane = rig.View.VisiblePane;
        var repaints = pane.RepaintCount;

        rig.View.ApplyDpi(rig.View.Fonts.Dpi == 144 ? 96 : 144);

        Assert.True(pane.RepaintCount > repaints);
    });

    [Fact]
    public void Dpi_change_scales_the_layout() => Sta.Run(() =>
    {
        using var rig = new Rig();

        rig.View.ApplyDpi(144);

        Assert.Equal(new Rectangle(6, 11, 321, 623 - 83), rig.View.SidebarSlot);
        Assert.Equal(rig.View.SidebarSlot, rig.View.Sidebar.Bounds);
        Assert.Equal(new Size(0, 2 * 90 + 2 * 99 + 2 * 99 + 99), rig.View.Sidebar.AutoScrollMinSize); // the entries at 1.5×
        Assert.Equal(new Rectangle(339, 0, 1278 - 339, 623 - 72), rig.View.VisiblePane.Bounds);
    });

    [Fact]
    public void Painting_creates_no_gdi_or_user_objects() => Sta.Run(() =>
    {
        using var rig = new Rig();
        // A buffer on a memory DC, as OnPaint gets: text drawn on a bitmap's Graphics copies the bitmap for every run, which
        // took these paints from 1 s to 17 s once the sidebar's text was in them.
        using var bmp = new Bitmap(1, 1);
        using var target = Graphics.FromImage(bmp);
        using var context = new BufferedGraphicsContext();
        using var buffer = context.Allocate(target, new Rectangle(0, 0, 1278, 623));
        var g = buffer.Graphics;
        rig.Paint(g);
        // Objects earlier tests left to the finalizer are released here, not inside the counted paints.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        var gdi = GuiResources(0);
        var user = GuiResources(1);

        for (var i = 0; i < 1000; i++) rig.Paint(g);

        Assert.Equal(gdi, GuiResources(0));
        Assert.Equal(user, GuiResources(1));
    });

    [Fact]
    public void Painting_at_tiny_sizes_does_not_throw() => Sta.Run(() =>
    {
        using var rig = new Rig();
        using var bmp = new Bitmap(1, 1);
        using var g = Graphics.FromImage(bmp);
        Size[] sizes = [new(400, 300), new(100, 50), new(1, 1), new(0, 0)];

        foreach (var kind in Enum.GetValues<DeviceKind>())
        {
            Assert.True(rig.View.Sidebar.SelectFirst(kind));
            Assert.Equal(kind, rig.View.VisiblePane.Device!.Kind);
            foreach (var size in sizes)
            {
                rig.View.ClientSize = size;
                rig.View.Sidebar.ClientSize = size;
                foreach (var pane in rig.View.Panes) pane.ClientSize = size;

                rig.View.PaintTo(g);
                rig.View.Sidebar.PaintTo(g, rig.View.Sidebar.ClientRectangle);
                foreach (var pane in rig.View.Panes) pane.PaintTo(g);
            }
        }
    });
}
