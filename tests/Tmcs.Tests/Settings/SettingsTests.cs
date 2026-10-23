using Tmcs.Details;
using Tmcs.Sampling;
using Tmcs.Settings;

namespace Tmcs.Tests.Settings;

public sealed class SettingsTests : IDisposable
{
    // The directory does not exist until a test creates it, which also proves Save creates missing directories.
    readonly string _dir = Path.Combine(Path.GetTempPath(), "tmcs-tests-" + Guid.NewGuid().ToString("N"));

    string FilePath => Path.Combine(_dir, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    static (DetailsColumn Id, bool Visible, int Width, int DisplayIndex)[] Snapshot(AppSettings s) =>
        s.DetailsColumns.Select(c => (c.Id, c.Visible, c.Width, c.DisplayIndex)).ToArray();

    [Fact]
    public void Default_matches_owner_layout()
    {
        var s = AppSettings.CreateDefault();

        Assert.Equal(DetailsColumns.All.Take(8).Select(c => c.Id), AppSettings.VisibleInDisplayOrder(s));
        Assert.Equal(Enum.GetValues<DetailsColumn>(), s.DetailsColumns.Select(c => c.Id));
        Assert.Equal(DetailsColumns.All.Select(c => c.DefaultWidth), s.DetailsColumns.Select(c => c.Width));
        Assert.Equal(DetailsColumns.All.Select(c => c.DefaultVisible), s.DetailsColumns.Select(c => c.Visible));
        Assert.Equal(Enumerable.Range(0, 11), s.DetailsColumns.Select(c => c.DisplayIndex));
        Assert.Equal(1294, s.WindowWidth);
        Assert.Equal(712, s.WindowHeight);
        Assert.Null(s.WindowX);
        Assert.Null(s.WindowY);
        Assert.False(s.Maximized);
        Assert.False(s.AlwaysOnTop);
        Assert.Equal(UpdateSpeed.Normal, s.UpdateSpeed);
        Assert.Equal(DetailsColumn.Name, s.SortColumn);
        Assert.False(s.SortDescending);
        Assert.Empty(s.HiddenAdapters);
    }

    [Fact]
    public void Default_path_is_tmcs_settings_json_under_appdata()
    {
        var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "tmcs", "settings.json");

        Assert.Equal(expected, SettingsStore.DefaultPath);
    }

    [Fact]
    public void Round_trips_through_json()
    {
        var s = AppSettings.CreateDefault();
        s.UpdateSpeed = UpdateSpeed.Low;
        s.WindowX = -1500;
        s.WindowY = 40;
        s.WindowWidth = 900;
        s.WindowHeight = 600;
        s.Maximized = true;
        s.AlwaysOnTop = true;
        s.SortColumn = DetailsColumn.Cpu;
        s.SortDescending = true;
        s.HiddenAdapters.Add("Ethernet 2");
        s.DetailsColumns.Single(c => c.Id == DetailsColumn.CommandLine).Visible = false;
        var threads = s.DetailsColumns.Single(c => c.Id == DetailsColumn.Threads);
        threads.Visible = true;
        threads.Width = 50;

        var store = new SettingsStore(FilePath);
        store.Save(s);
        var loaded = store.Load();

        Assert.False(File.Exists(FilePath + ".tmp"));
        Assert.Equal(UpdateSpeed.Low, loaded.UpdateSpeed);
        Assert.Equal(-1500, loaded.WindowX);
        Assert.Equal(40, loaded.WindowY);
        Assert.Equal(900, loaded.WindowWidth);
        Assert.Equal(600, loaded.WindowHeight);
        Assert.True(loaded.Maximized);
        Assert.True(loaded.AlwaysOnTop);
        Assert.Equal(DetailsColumn.Cpu, loaded.SortColumn);
        Assert.True(loaded.SortDescending);
        Assert.Equal(["Ethernet 2"], loaded.HiddenAdapters);
        Assert.Equal(AppSettings.VisibleInDisplayOrder(s), AppSettings.VisibleInDisplayOrder(loaded));
        Assert.DoesNotContain(DetailsColumn.CommandLine, AppSettings.VisibleInDisplayOrder(loaded));
        Assert.Contains(DetailsColumn.Threads, AppSettings.VisibleInDisplayOrder(loaded));
        Assert.Equal(8, AppSettings.VisibleInDisplayOrder(loaded).Count);
        Assert.Equal(50, loaded.DetailsColumns.Single(c => c.Id == DetailsColumn.Threads).Width);
        Assert.Equal(s.DetailsColumns.OrderBy(c => c.Id).Select(c => c.Width), loaded.DetailsColumns.OrderBy(c => c.Id).Select(c => c.Width));
    }

    [Fact]
    public void Save_writes_indented_json_with_enums_as_names()
    {
        var s = AppSettings.CreateDefault();
        s.UpdateSpeed = UpdateSpeed.Low;

        new SettingsStore(FilePath).Save(s);
        var text = File.ReadAllText(FilePath);

        Assert.Contains("\"UpdateSpeed\": \"Low\"", text);
        Assert.Contains("\"SortColumn\": \"Name\"", text);
        Assert.Contains("\"Id\": \"ActivePrivateWorkingSet\"", text);
        Assert.Contains(Environment.NewLine + "  ", text);
    }

    [Fact]
    public void Save_replaces_an_existing_file()
    {
        var store = new SettingsStore(FilePath);
        var first = AppSettings.CreateDefault();
        first.UpdateSpeed = UpdateSpeed.High;
        store.Save(first);
        var second = AppSettings.CreateDefault();
        second.UpdateSpeed = UpdateSpeed.Paused;

        store.Save(second);

        Assert.Equal(UpdateSpeed.Paused, store.Load().UpdateSpeed);
        Assert.Equal([FilePath], Directory.GetFiles(_dir));
    }

    [Fact]
    public void Failed_save_throws_and_leaves_no_tmp_file()
    {
        // A directory where the file belongs makes the final move fail after the tmp file was written.
        Directory.CreateDirectory(FilePath);

        Assert.Throws<UnauthorizedAccessException>(() => new SettingsStore(FilePath).Save(AppSettings.CreateDefault()));

        Assert.False(File.Exists(FilePath + ".tmp"));
    }

    [Fact]
    public void Missing_file_yields_defaults()
    {
        var loaded = new SettingsStore(FilePath).Load();

        Assert.Equal(Snapshot(AppSettings.CreateDefault()), Snapshot(loaded));
        Assert.Equal(1294, loaded.WindowWidth);
        Assert.False(Directory.Exists(_dir));
    }

    [Fact]
    public void Corrupt_file_yields_defaults()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "{not json");

        var loaded = new SettingsStore(FilePath).Load();

        Assert.Equal(AppSettings.VisibleInDisplayOrder(AppSettings.CreateDefault()), AppSettings.VisibleInDisplayOrder(loaded));
        Assert.Equal(Snapshot(AppSettings.CreateDefault()), Snapshot(loaded));
        Assert.Equal(1294, loaded.WindowWidth);
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("""{"WindowWidth": "wide"}""")]
    [InlineData("""{"UpdateSpeed": "Ludicrous"}""")]
    public void Unusable_file_content_yields_defaults(string content)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, content);

        var loaded = new SettingsStore(FilePath).Load();

        Assert.Equal(Snapshot(AppSettings.CreateDefault()), Snapshot(loaded));
        Assert.Equal(1294, loaded.WindowWidth);
    }

    [Fact]
    public void Binary_garbage_yields_defaults()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllBytes(FilePath, [0xFF, 0xFE, 0x00, 0x80, 0x7B, 0xC3, 0x28]);

        var loaded = new SettingsStore(FilePath).Load();

        Assert.Equal(Snapshot(AppSettings.CreateDefault()), Snapshot(loaded));
    }

    [Fact]
    public void Unreadable_path_yields_defaults()
    {
        // A directory at the settings path makes the open fail with UnauthorizedAccessException.
        Directory.CreateDirectory(FilePath);

        var loaded = new SettingsStore(FilePath).Load();

        Assert.Equal(Snapshot(AppSettings.CreateDefault()), Snapshot(loaded));
    }

    [Fact]
    public void Locked_file_yields_defaults()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "{}");
        using var locked = new FileStream(FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var loaded = new SettingsStore(FilePath).Load();

        Assert.Equal(Snapshot(AppSettings.CreateDefault()), Snapshot(loaded));
    }

    [Fact]
    public void Partial_file_keeps_what_it_has_and_defaults_the_rest()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, """{ "UpdateSpeed": "High", "WindowX": 10 }""");

        var loaded = new SettingsStore(FilePath).Load();

        Assert.Equal(UpdateSpeed.High, loaded.UpdateSpeed);
        Assert.Equal(10, loaded.WindowX);
        Assert.Null(loaded.WindowY);
        Assert.Equal(1294, loaded.WindowWidth);
        Assert.Equal(Snapshot(AppSettings.CreateDefault()), Snapshot(loaded));
    }

    [Fact]
    public void File_from_an_older_version_gets_the_new_columns_appended_with_defaults()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, """
            { "DetailsColumns": [
                { "Id": "Pid", "Visible": true, "Width": 60, "DisplayIndex": 0 },
                { "Id": "Name", "Visible": true, "Width": 200, "DisplayIndex": 1 }
            ] }
            """);

        var loaded = new SettingsStore(FilePath).Load();

        Assert.Equal(11, loaded.DetailsColumns.Count);
        Assert.Equal(
            [DetailsColumn.Pid, DetailsColumn.Name, DetailsColumn.Status, DetailsColumn.UserName, DetailsColumn.Cpu, DetailsColumn.ActivePrivateWorkingSet, DetailsColumn.CommandLine, DetailsColumn.UacVirtualization],
            AppSettings.VisibleInDisplayOrder(loaded));
        Assert.Equal(60, loaded.DetailsColumns.Single(c => c.Id == DetailsColumn.Pid).Width);
        Assert.Equal(200, loaded.DetailsColumns.Single(c => c.Id == DetailsColumn.Name).Width);
        Assert.Equal(1033, loaded.DetailsColumns.Single(c => c.Id == DetailsColumn.CommandLine).Width);
        Assert.False(loaded.DetailsColumns.Single(c => c.Id == DetailsColumn.CommitSize).Visible);
    }

    [Fact]
    public void Normalize_repairs_column_list()
    {
        var s = new AppSettings
        {
            DetailsColumns =
            [
                new() { Id = DetailsColumn.Pid, Visible = true, Width = 49, DisplayIndex = 0 },
                new() { Id = DetailsColumn.Pid, Visible = true, Width = 500, DisplayIndex = 1 },
                new() { Id = DetailsColumn.Name, Visible = false, Width = 3, DisplayIndex = 2 },
                new() { Id = (DetailsColumn)99, Visible = true, Width = 100, DisplayIndex = 3 },
            ],
        };

        s.Normalize();

        Assert.Equal(11, s.DetailsColumns.Count);
        Assert.Equal(11, s.DetailsColumns.Select(c => c.Id).Distinct().Count());
        Assert.All(s.DetailsColumns, c => Assert.True(Enum.IsDefined(c.Id)));
        var name = s.DetailsColumns.Single(c => c.Id == DetailsColumn.Name);
        Assert.True(name.Visible);
        Assert.Equal(20, name.Width);
        Assert.Equal(49, s.DetailsColumns.Single(c => c.Id == DetailsColumn.Pid).Width);

        // A second pass changes nothing.
        var once = Snapshot(s);
        s.Normalize();
        Assert.Equal(once, Snapshot(s));
    }

    [Fact]
    public void Normalize_numbers_display_indices_densely_visible_first_in_current_order()
    {
        var s = new AppSettings
        {
            DetailsColumns =
            [
                new() { Id = DetailsColumn.Name, Visible = true, Width = 100, DisplayIndex = 30 },
                new() { Id = DetailsColumn.Pid, Visible = false, Width = 100, DisplayIndex = 5 },
                new() { Id = DetailsColumn.Status, Visible = true, Width = 100, DisplayIndex = 10 },
                new() { Id = DetailsColumn.Cpu, Visible = false, Width = 100, DisplayIndex = 1 },
                new() { Id = DetailsColumn.Handles, Visible = true, Width = 100, DisplayIndex = -4 },
            ],
        };

        s.Normalize();

        // Visible columns in their saved display order, then the missing visible-by-default columns in enum order;
        // hidden columns follow, in their saved order (Cpu 1 before Pid 5), then the missing hidden-by-default ones.
        Assert.Equal(
            [
                DetailsColumn.Handles, DetailsColumn.Status, DetailsColumn.Name,
                DetailsColumn.UserName, DetailsColumn.ActivePrivateWorkingSet, DetailsColumn.CommandLine, DetailsColumn.UacVirtualization,
                DetailsColumn.Cpu, DetailsColumn.Pid,
                DetailsColumn.Threads, DetailsColumn.CommitSize,
            ],
            s.DetailsColumns.Select(c => c.Id));
        Assert.Equal(Enumerable.Range(0, 11), s.DetailsColumns.Select(c => c.DisplayIndex));
        Assert.Equal(s.DetailsColumns.Take(7).Select(c => c.Id), AppSettings.VisibleInDisplayOrder(s));
    }

    [Fact]
    public void Normalize_clamps_every_width_to_at_least_20()
    {
        var s = AppSettings.CreateDefault();
        s.DetailsColumns[1].Width = 0;
        s.DetailsColumns[2].Width = -50;
        s.DetailsColumns[3].Width = 19;
        s.DetailsColumns[4].Width = 20;
        s.DetailsColumns[5].Width = 21;

        s.Normalize();

        Assert.Equal([20, 20, 20, 20, 21], s.DetailsColumns.Skip(1).Take(5).Select(c => c.Width));
        Assert.All(s.DetailsColumns, c => Assert.True(c.Width >= 20));
    }

    [Fact]
    public void Normalize_repairs_null_and_out_of_range_values()
    {
        var s = new AppSettings
        {
            DetailsColumns = null!,
            HiddenAdapters = null!,
            UpdateSpeed = (UpdateSpeed)42,
            SortColumn = (DetailsColumn)(-1),
        };

        s.Normalize();

        Assert.Equal(Snapshot(AppSettings.CreateDefault()), Snapshot(s));
        Assert.Empty(s.HiddenAdapters);
        Assert.Equal(UpdateSpeed.Normal, s.UpdateSpeed);
        Assert.Equal(DetailsColumn.Name, s.SortColumn);
    }

    [Fact]
    public void Normalize_drops_null_entries()
    {
        var s = new AppSettings { DetailsColumns = [null!, new() { Id = DetailsColumn.Pid, Visible = true, Width = 60 }, null!] };

        s.Normalize();

        Assert.Equal(11, s.DetailsColumns.Count);
        Assert.Equal(60, s.DetailsColumns.Single(c => c.Id == DetailsColumn.Pid).Width);
    }

    [Fact]
    public void Normalize_leaves_the_default_layout_unchanged()
    {
        var s = AppSettings.CreateDefault();
        var before = Snapshot(s);

        s.Normalize();

        Assert.Equal(before, Snapshot(s));
    }

    [Fact]
    public void VisibleInDisplayOrder_follows_display_index_not_list_order()
    {
        var s = AppSettings.CreateDefault();
        s.DetailsColumns.Single(c => c.Id == DetailsColumn.Name).DisplayIndex = 7;
        s.DetailsColumns.Single(c => c.Id == DetailsColumn.UacVirtualization).DisplayIndex = 0;
        s.DetailsColumns.Single(c => c.Id == DetailsColumn.Threads).Visible = false;

        var visible = AppSettings.VisibleInDisplayOrder(s);

        Assert.Equal(DetailsColumn.UacVirtualization, visible[0]);
        Assert.Equal(DetailsColumn.Name, visible[^1]);
        Assert.Equal(8, visible.Count);
    }

    [Fact]
    public void Clamp_moves_offscreen_window_onto_a_screen()
    {
        var screen = new Rectangle(0, 0, 1920, 1040);

        var clamped = WindowPlacement.Clamp(new Rectangle(5000, 5000, 1294, 712), [screen]);

        Assert.True(screen.Contains(clamped));
        Assert.Equal(new Rectangle(313, 164, 1294, 712), clamped);
    }

    [Fact]
    public void Clamp_leaves_an_onscreen_window_unchanged()
    {
        var screens = new[] { new Rectangle(0, 0, 1920, 1040) };
        var saved = new Rectangle(100, 80, 1294, 712);

        Assert.Equal(saved, WindowPlacement.Clamp(saved, screens));
    }

    [Fact]
    public void Clamp_leaves_a_window_on_a_second_monitor_unchanged_even_with_negative_coordinates()
    {
        var screens = new[] { new Rectangle(0, 0, 1920, 1040), new Rectangle(-1280, 0, 1280, 984) };
        var saved = new Rectangle(-1200, 100, 1000, 600);

        Assert.Equal(saved, WindowPlacement.Clamp(saved, screens));
    }

    [Fact]
    public void Clamp_moves_a_window_saved_on_a_now_missing_monitor_to_the_first_screen()
    {
        var screens = new[] { new Rectangle(0, 0, 1920, 1040) };

        var clamped = WindowPlacement.Clamp(new Rectangle(-1200, 100, 1000, 600), screens);

        Assert.Equal(new Rectangle(460, 220, 1000, 600), clamped);
    }

    [Fact]
    public void Clamp_shrinks_an_oversized_window_to_fit_the_first_screen()
    {
        var screen = new Rectangle(100, 50, 800, 600);

        var clamped = WindowPlacement.Clamp(new Rectangle(5000, 5000, 1294, 712), [screen]);

        Assert.Equal(screen, clamped);
    }

    [Fact]
    public void Clamp_shrinks_only_the_dimension_that_does_not_fit()
    {
        var screen = new Rectangle(0, 0, 1000, 800);

        var clamped = WindowPlacement.Clamp(new Rectangle(-9000, -9000, 1294, 400), [screen]);

        Assert.Equal(new Rectangle(0, 200, 1000, 400), clamped);
    }

    [Theory]
    [InlineData(1870, 500, true)]   // 50 px of the width stays on screen
    [InlineData(1871, 500, false)]  // 49 px
    [InlineData(500, 990, true)]    // 50 px of the height stays on screen
    [InlineData(500, 991, false)]   // 49 px
    [InlineData(-1244, 500, true)]  // 50 px visible past the left edge
    [InlineData(-1245, 500, false)] // 49 px
    [InlineData(500, -662, true)]   // 50 px visible past the top edge
    [InlineData(500, -663, false)]  // 49 px
    public void Clamp_requires_at_least_50_by_50_pixels_on_screen(int x, int y, bool unchanged)
    {
        var screens = new[] { new Rectangle(0, 0, 1920, 1040) };
        var saved = new Rectangle(x, y, 1294, 712);

        var clamped = WindowPlacement.Clamp(saved, screens);

        Assert.Equal(unchanged ? saved : new Rectangle(313, 164, 1294, 712), clamped);
    }

    [Fact]
    public void Clamp_does_not_sum_the_overlap_of_several_screens()
    {
        // Only 30 px of the width reaches either of two stacked screens, so no single screen shows a 50 x 50 area.
        var screens = new[] { new Rectangle(0, 0, 1920, 1040), new Rectangle(0, 1040, 1920, 1040) };
        var saved = new Rectangle(1890, 1000, 100, 100);

        var clamped = WindowPlacement.Clamp(saved, screens);

        Assert.Equal(new Rectangle(910, 470, 100, 100), clamped);
    }

    [Fact]
    public void Clamp_without_any_screen_returns_the_saved_rectangle()
    {
        var saved = new Rectangle(5000, 5000, 1294, 712);

        Assert.Equal(saved, WindowPlacement.Clamp(saved, []));
    }
}
