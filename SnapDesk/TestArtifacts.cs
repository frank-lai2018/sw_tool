namespace SnapDesk;

internal static class TestArtifacts
{
    // Full single-file extraction changes AppContext.BaseDirectory. Keep diagnostics beside the invoked executable.
    internal static string Directory=>Path.GetDirectoryName(Environment.ProcessPath!)??AppContext.BaseDirectory;
}
