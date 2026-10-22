// Qualification-only adversarial hook. Normal RepoChecks execution never invokes it.
internal static class StartupHook
{
    public static void Initialize()
    {
        if (Environment.GetEnvironmentVariable("AGD_POISON_MARKER") is { } marker)
            File.WriteAllText(marker, "unexpected CLR startup-hook execution");
    }
}
