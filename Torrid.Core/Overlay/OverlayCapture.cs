namespace Torrid.Core.Overlay;

public static class OverlayCapture
{
    /// WDA_EXCLUDEFROMCAPTURE (0x11) hides the window from screen sharing and recording; WDA_NONE (0) shows it.
    public static uint DisplayAffinity(bool hidden) => hidden ? 0x11u : 0u;
}
