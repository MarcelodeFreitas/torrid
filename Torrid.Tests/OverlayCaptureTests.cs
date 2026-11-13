using Torrid.Core.Config;
using Torrid.Core.Overlay;

namespace Torrid.Tests;

public class OverlayCaptureTests
{
    [Fact]
    public void DisplayAffinity_Hidden_IsExcludeFromCapture()
    {
        Assert.Equal(0x11u, OverlayCapture.DisplayAffinity(true));
    }

    [Fact]
    public void DisplayAffinity_NotHidden_IsNone()
    {
        Assert.Equal(0u, OverlayCapture.DisplayAffinity(false));
    }

    [Fact]
    public void AppConfig_DefaultsToHiddenFromCapture()
    {
        Assert.True(new AppConfig().OverlayHiddenFromCapture);
    }
}
