namespace SnapDesk;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Contains("--self-test")) { SelfTest.Run(); return; }
        if (args.Contains("--recording-test")) { RecordingTests.Smoke(); return; }
        Application.ThreadException += (_, e) => MessageBox.Show(e.Exception.Message, "SnapDesk", MessageBoxButtons.OK, MessageBoxIcon.Error);
        Application.Run(new MainForm());
    }
}
