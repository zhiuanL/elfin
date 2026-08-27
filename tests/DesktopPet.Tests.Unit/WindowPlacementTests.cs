using DesktopPet.Application.Windows;
using DesktopPet.Domain.Platform;

namespace DesktopPet.Tests.Unit;

public sealed class WindowPlacementTests
{
    private readonly WindowPlacementPolicy _policy = new();
    internal static DisplayArea[] Displays =>
    [
        new("primary", new(0, 0, 1920, 1080), new(0, 0, 1920, 1040), true),
        new("left", new(-1920, -300, 1920, 1080), new(-1920, -300, 1920, 1040), false)
    ];

    [Fact]
    public void DefaultUsesPrimaryWorkingAreaAndMargin()
    {
        var result = _policy.Resolve(null, new(220, 220), Displays);
        Assert.Equal(new PixelPoint(1676, 796), result.Origin);
        Assert.Equal("primary", result.DisplayId);
    }
    [Theory]
    [InlineData(-1800, -200)]
    [InlineData(-250, 100)]
    [InlineData(100, 200)]
    public void ValidPhysicalCoordinatesIncludingNegativesArePreserved(double x, double y)
    {
        var result = _policy.Resolve(new(new(x, y), null), new(220, 220), Displays);
        Assert.Equal(new PixelPoint(x, y), result.Origin);
    }
    [Theory]
    [InlineData(double.NaN, 0)]
    [InlineData(double.PositiveInfinity, 100)]
    [InlineData(0, double.NegativeInfinity)]
    [InlineData(double.MaxValue, 0)]
    public void InvalidCoordinatesUseSafeDefault(double x, double y) =>
        Assert.Equal(_policy.Resolve(null, new(220, 220), Displays),
            _policy.Resolve(new(new(x, y), null), new(220, 220), Displays));

    [Fact]
    public void OffscreenAndRemovedMonitorRecoverFullyInsideWorkingArea()
    {
        var result = _policy.Resolve(new(new(-4000, -1000), "removed"), new(220, 220), [Displays[0]]);
        Assert.Equal(new PixelPoint(0, 0), result.Origin);
        Assert.Equal("primary", result.DisplayId);
        Assert.Equal(new PixelPoint(1700, 820),
            _policy.Resolve(new(new(9999, 9999), "primary"), new(220, 220), Displays).Origin);
    }
    [Fact]
    public void GapBetweenDisplaysDoesNotCountAsVisibleDesktop()
    {
        DisplayArea[] areas = [Displays[0], new("far", new(2400, 0, 1920, 1080), new(2400, 0, 1920, 1040), false)];
        Assert.Equal(new PixelPoint(2400, 200), _policy.Resolve(new(new(2300, 200), null), new(220, 220), areas).Origin);
    }
    [Theory]
    [InlineData(96, 220)]
    [InlineData(120, 275)]
    [InlineData(144, 330)]
    [InlineData(192, 440)]
    public void DpiChangesPhysicalSizeWithoutScalingVirtualDesktopOrigin(uint dpi, double pixels)
    {
        var size = DpiMath.ToPixels(new(220, 220), DpiMath.FromDpi(dpi));
        Assert.Equal(new PixelSize(pixels, pixels), size);
        Assert.Equal(new PixelPoint(-1800, -200), _policy.Resolve(new(new(-1800, -200), "left"), size, Displays).Origin);
    }
    [Fact]
    public void OversizedWindowAnchorsToWorkAreaAndMissingDisplaysFailExplicitly()
    {
        Assert.Equal(new PixelPoint(0, 0), _policy.Resolve(null, new(4000, 3000), [Displays[0]]).Origin);
        Assert.Throws<InvalidOperationException>(() => _policy.Resolve(null, new(220, 220), []));
        Assert.Throws<ArgumentOutOfRangeException>(() => _policy.Resolve(null, new(0, 220), Displays));
        Assert.Throws<ArgumentOutOfRangeException>(() => DpiMath.FromDpi(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => DpiMath.ToPixels(new(double.NaN, 220), new(1, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => DpiMath.ToPixels(new(220, 220), new(0, 1)));
    }
}
