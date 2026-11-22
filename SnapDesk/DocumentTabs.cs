namespace SnapDesk;

internal enum CloseScope { Current, All, Left, Right }

internal sealed class DocumentTabs : TabControl
{
    TabPage? pressedClose;
    TabPage? hoveredClose;
    ContextMenuStrip? tabMenu;
    public event Action<TabPage[]>? CloseRequested;

    public DocumentTabs()
    {
        DrawMode = TabDrawMode.OwnerDrawFixed;
        Padding = new Point(35, 9);
        ShowToolTips = true;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            // Buffer the native tab control and its child pages together so
            // switching pages does not expose an intermediate cleared surface.
            parameters.ExStyle |= 0x02000000; // WS_EX_COMPOSITED
            return parameters;
        }
    }

    internal Rectangle CloseBounds(int index)
    {
        if (index < 0 || index >= TabCount || TabPages[index].Tag is not EditorCanvas) return Rectangle.Empty;
        var tab = GetTabRect(index);
        int size = LogicalToDeviceUnits(18), margin = LogicalToDeviceUnits(6);
        return new Rectangle(tab.Right - size - margin, tab.Top + (tab.Height - size) / 2, size, size);
    }

    int TabAt(Point point)
    {
        for (int i = 0; i < TabCount; i++) if (GetTabRect(i).Contains(point)) return i;
        return -1;
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= TabCount) return;
        var page = TabPages[e.Index]; var rect = GetTabRect(e.Index);
        var selected = SelectedTab == page;
        using var background = new SolidBrush(selected ? Color.White : Color.FromArgb(237, 241, 246));
        e.Graphics.FillRectangle(background, rect);
        using var border = new Pen(Color.FromArgb(209, 218, 228));
        e.Graphics.DrawRectangle(border, rect.X, rect.Y, rect.Width - 1, rect.Height - 1);
        var close = CloseBounds(e.Index);
        var text = Rectangle.Inflate(rect, -LogicalToDeviceUnits(10), 0);
        if (!close.IsEmpty) text.Width = Math.Max(0, close.Left - text.Left - LogicalToDeviceUnits(5));
        TextRenderer.DrawText(e.Graphics, page.Text, Font, text, Color.FromArgb(28, 45, 64), TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        if (!close.IsEmpty)
        {
            bool hot = page == hoveredClose;
            if (hot) { using var highlight = new SolidBrush(Color.FromArgb(255, 225, 225)); e.Graphics.FillRectangle(highlight, close); }
            using var cross = new Pen(hot ? Color.Firebrick : Color.FromArgb(86, 105, 126), LogicalToDeviceUnits(1));
            int inset = LogicalToDeviceUnits(5);
            e.Graphics.DrawLine(cross, close.Left + inset, close.Top + inset, close.Right - inset, close.Bottom - inset);
            e.Graphics.DrawLine(cross, close.Right - inset, close.Top + inset, close.Left + inset, close.Bottom - inset);
        }
        if (Focused && selected) e.DrawFocusRectangle();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int index = TabAt(e.Location);
        SetHoveredClose(index >= 0 && CloseBounds(index).Contains(e.Location) ? TabPages[index] : null);
    }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); SetHoveredClose(null); }

    void SetHoveredClose(TabPage? page)
    {
        if (hoveredClose == page) return;
        var previous = hoveredClose; hoveredClose = page;
        InvalidateClose(previous); InvalidateClose(page);
    }
    void InvalidateClose(TabPage? page)
    {
        if (page == null) return;
        var bounds = CloseBounds(TabPages.IndexOf(page));
        if (!bounds.IsEmpty) Invalidate(bounds, false);
    }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        int index = TabAt(e.Location);
        pressedClose = e.Button == MouseButtons.Left && index >= 0 && CloseBounds(index).Contains(e.Location) ? TabPages[index] : null;
        base.OnMouseDown(e);
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        int index = TabAt(e.Location); var pressed = pressedClose; pressedClose = null;
        if (index < 0) return;
        var page = TabPages[index];
        if (e.Button == MouseButtons.Left && page == pressed && CloseBounds(index).Contains(e.Location))
        { CloseRequested?.Invoke([page]); return; }
        if (e.Button != MouseButtons.Right || page.Tag is not EditorCanvas) return;
        tabMenu?.Dispose(); tabMenu = CreateCloseMenu(page);
        tabMenu.Show(this, e.Location);
    }

    internal ContextMenuStrip CreateCloseMenu(TabPage page)
    {
        var menu = new ContextMenuStrip { Font = Font };
        foreach (var (label, scope) in new[] { ("關閉這張截圖", CloseScope.Current), ("全部關閉", CloseScope.All), ("關閉左邊所有截圖", CloseScope.Left), ("關閉右邊所有截圖", CloseScope.Right) })
        {
            var targets = GetCloseTargets(page, scope);
            var item = new ToolStripMenuItem(label) { Enabled = targets.Length > 0 };
            item.Click += (_, _) => CloseRequested?.Invoke(targets);
            menu.Items.Add(item);
        }
        return menu;
    }

    internal TabPage[] GetCloseTargets(TabPage anchor, CloseScope scope)
    {
        int index = TabPages.IndexOf(anchor);
        if (index < 0 || anchor.Tag is not EditorCanvas) return [];
        return TabPages.Cast<TabPage>().Where((page, i) => page.Tag is EditorCanvas && scope switch
        {
            CloseScope.Current => page == anchor,
            CloseScope.All => true,
            CloseScope.Left => i < index,
            CloseScope.Right => i > index,
            _ => false
        }).ToArray();
    }

    internal bool TryCloseDocuments(IEnumerable<TabPage> pages, Func<TabPage, bool> confirm)
    {
        var targets = pages.Distinct().Where(p => TabPages.Contains(p) && p.Tag is EditorCanvas).ToArray();
        var selected = SelectedTab; int selectedIndex = SelectedIndex;
        try
        {
            // Confirm/save all requested documents before removing any tab.
            // Cancel or a failed save leaves the entire batch open.
            foreach (var page in targets) { SelectedTab = page; if (!confirm(page)) return false; }
            foreach (var page in targets) { TabPages.Remove(page); page.Dispose(); }
            return true;
        }
        finally
        {
            if (selected != null && TabPages.Contains(selected)) SelectedTab = selected;
            else if (TabCount > 0) SelectedIndex = Math.Clamp(selectedIndex, 0, TabCount - 1);
            // Native tab layout/selection messages already repaint structural changes.
            if (hoveredClose != null && !TabPages.Contains(hoveredClose)) hoveredClose = null;
        }
    }

    protected override void Dispose(bool disposing) { if (disposing) tabMenu?.Dispose(); base.Dispose(disposing); }
}
