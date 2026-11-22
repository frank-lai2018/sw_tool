using System.Runtime.InteropServices;

namespace SnapDesk;

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; public Rectangle Bounds => Rectangle.FromLTRB(Left, Top, Right, Bottom); }
    [DllImport("user32.dll")] internal static extern bool RegisterHotKey(nint hWnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(nint hWnd, int id);
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetWindowDisplayAffinity(nint window, uint affinity);
    [DllImport("user32.dll")] internal static extern nint WindowFromPoint(Point point);
    [DllImport("user32.dll")] internal static extern nint GetAncestor(nint window, uint flags);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(nint window, out Rect rect);
    [DllImport("dwmapi.dll")] internal static extern int DwmGetWindowAttribute(nint window, int attr, out Rect rect, int size);
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] internal static extern void mouse_event(uint flags, uint dx, uint dy, int data, nuint info);

    internal static Rectangle WindowBounds(nint handle)
    {
        if (DwmGetWindowAttribute(handle, 9, out var r, Marshal.SizeOf<Rect>()) != 0) GetWindowRect(handle, out r);
        return Rectangle.Intersect(SystemInformation.VirtualScreen, r.Bounds);
    }
    internal static Bitmap Capture(Rectangle rect)
    {
        if (rect.Width < 1 || rect.Height < 1) throw new ArgumentException("截圖範圍不在螢幕內。");
        var bitmap = new Bitmap(rect.Width, rect.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try { using var g = Graphics.FromImage(bitmap); g.CopyFromScreen(rect.Location, Point.Empty, rect.Size, CopyPixelOperation.SourceCopy); return bitmap; }
        catch { bitmap.Dispose(); throw; }
    }
}
