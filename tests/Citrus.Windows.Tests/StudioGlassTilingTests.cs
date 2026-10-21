using Citrus.Windows.Presentation;

namespace Citrus.Windows.Tests;

public sealed class StudioGlassTilingTests
{
    [Fact]
    public void A_surface_crossing_adjacent_monitors_uses_physical_coordinates_without_rescaling_tiles()
    {
        GlassMonitor[] monitors = [new(1, -1920, 0, 1920, 1080), new(2, 0, 0, 3840, 2160)];
        var tiles = StudioGlassTiling.Plan(-100, 20, 300, 200, monitors);
        Assert.Collection(tiles,
            tile => Assert.Equal(new GlassCaptureTile(monitors[0], 1820, 20, 0, 0, 100, 200), tile),
            tile => Assert.Equal(new GlassCaptureTile(monitors[1], 0, 20, 100, 0, 200, 200), tile));
    }

    [Fact]
    public void Padded_capture_keeps_exact_destination_offsets_near_outer_edges_and_desktop_gaps()
    {
        GlassMonitor[] monitors = [new(1, 0, 0, 100, 100), new(2, 150, 0, 100, 100)];
        var tiles = StudioGlassTiling.Plan(-20, -20, 300, 150, monitors);
        Assert.Collection(tiles,
            tile => Assert.Equal(new GlassCaptureTile(monitors[0], 0, 0, 20, 20, 100, 100), tile),
            tile => Assert.Equal(new GlassCaptureTile(monitors[1], 0, 0, 170, 20, 100, 100), tile));
    }

    [Fact]
    public void Vertically_offset_monitors_contribute_only_their_intersection()
    {
        GlassMonitor[] monitors = [new(1, 0, 0, 100, 100), new(2, 100, 50, 100, 100)];
        var tiles = StudioGlassTiling.Plan(75, 25, 100, 100, monitors);
        Assert.Collection(tiles,
            tile => Assert.Equal(new GlassCaptureTile(monitors[0], 75, 25, 0, 0, 25, 75), tile),
            tile => Assert.Equal(new GlassCaptureTile(monitors[1], 0, 0, 25, 25, 75, 75), tile));
    }

    [Fact]
    public void Monitors_outside_the_requested_capture_and_zero_sized_regions_are_ignored()
    {
        GlassMonitor[] monitors = [new(1, 0, 0, 100, 100)];
        Assert.Empty(StudioGlassTiling.Plan(100, 0, 20, 20, monitors));
        Assert.Throws<ArgumentOutOfRangeException>(() => StudioGlassTiling.Plan(0, 0, 0, 20, monitors));
    }
}
