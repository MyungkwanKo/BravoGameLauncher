namespace CoopGameLauncher;

/// <summary>협업부서 배포용 Coop 런처 버전.</summary>
public static class LauncherVersionInfo
{
    public const int Version = 1;

    public static string VersionCode => $"v{Version}";

    public static string WindowTitle => $"GW Coop Launcher {VersionCode}";
}
