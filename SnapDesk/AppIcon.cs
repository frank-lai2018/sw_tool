namespace SnapDesk;

internal static class AppIcon
{
    internal static Icon Load()
    {
        using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream("SnapDesk.AppIcon")
            ?? throw new InvalidOperationException("找不到程式圖示資源。");
        using var source = new Icon(stream, new Size(32, 32));
        return (Icon)source.Clone();
    }
}
