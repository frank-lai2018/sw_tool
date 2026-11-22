namespace SnapDesk;

internal static class ScrollCapture
{
    // Return vertical displacement only for a distinctive, low-error overlap.
    // Ambiguous/animated/fixed content stops capture instead of silently duplicating it.
    internal static int FindShift(Bitmap previous, Bitmap next)
    {
        int height = Math.Min(previous.Height, next.Height), width = Math.Min(previous.Width, next.Width);
        if (height < 60 || width < 30) return -1;
        var scores = new List<(int shift, double error)>();
        for (int shift = 0; shift <= height * 3 / 4; shift++)
        {
            double error = 0; int count = 0;
            for (int y = 8; y < height - shift - 8; y += Math.Max(7, (height - shift) / 22))
                for (int x = width / 10; x < width * 9 / 10; x += Math.Max(7, width / 28))
                {
                    var a = previous.GetPixel(x, y + shift); var b = next.GetPixel(x, y);
                    error += Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B); count++;
                }
            if (count > 0) scores.Add((shift, error / count));
        }
        var best = scores.MinBy(s => s.error);
        if (best.error > 18) return -1;
        if (best.shift == 0) return 0;
        if (scores.Any(s => Math.Abs(s.shift - best.shift) > 4 && s.error <= best.error + 1.5)) return -1;
        return best.shift;
    }
    internal static Bitmap Stitch(IReadOnlyList<(Bitmap image, int offset)> frames)
    {
        int height = frames.Max(f => f.offset + f.image.Height), width = frames[0].image.Width;
        if ((long)width * height > 80_000_000) throw new InvalidOperationException("長截圖超過 8,000 萬像素上限。");
        var result = new Bitmap(width, height); using var g = Graphics.FromImage(result);
        foreach (var frame in frames) g.DrawImageUnscaled(frame.image, 0, frame.offset);
        return result;
    }
    internal static async Task<Bitmap> CaptureAsync(Rectangle area, Action<string>? report = null)
    {
        if (area.Height < 100) throw new InvalidOperationException("捲動截圖請選取至少 100 像素高的區域。");
        var frames = new List<(Bitmap image, int offset)>(); var oldPosition = Cursor.Position;
        try
        {
            // Move into the selected app and focus only that window, without clicking content.
            Cursor.Position = new Point(area.Left + area.Width / 2, area.Top + area.Height / 2);
            Native.SetForegroundWindow(Native.GetAncestor(Native.WindowFromPoint(Cursor.Position), 2));
            await Task.Delay(200);
            frames.Add((Native.Capture(area), 0));
            for (int i = 0; i < 20; i++)
            {
                if (Native.GetAsyncKeyState((int)Keys.Escape) < 0) break;
                Native.mouse_event(0x0800, 0, 0, -120, 0);
                await Task.Delay(650);
                if (Native.GetAsyncKeyState((int)Keys.Escape) < 0) break;
                var next = Native.Capture(area); int shift = FindShift(frames[^1].image, next);
                if (shift <= 0) { next.Dispose(); break; }
                int offset = frames[^1].offset + shift;
                if ((long)(offset + next.Height) * next.Width > 80_000_000) { next.Dispose(); break; }
                frames.Add((next, offset));
            }
            report?.Invoke(frames.Count == 1 ? "只擷取到一個畫面：內容未移動或無法可靠拼接。請檢查選取區域與目標程式。" : $"已拼接 {frames.Count} 個畫面。請檢查接縫；如需繼續可重新擷取更下方區域。");
            return Stitch(frames);
        }
        finally { Cursor.Position = oldPosition; foreach (var frame in frames) frame.image.Dispose(); }
    }
}
