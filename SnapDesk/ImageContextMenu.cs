namespace SnapDesk;

internal sealed class ImageContextMenu : ContextMenuStrip
{
    internal ImageContextMenu(Action copy)
    {
        AccessibleName = "圖片右鍵選單";
        var item = new ToolStripMenuItem("複製圖片") { ShortcutKeyDisplayString = "Ctrl+C" };
        item.Click += (_, _) => copy();
        Items.Add(item);
    }
}
