using System;

namespace BravoGameLauncherGui
{
    /// <summary>
    /// 런처 버전 정보 (서버 패치/업데이트 체크용 단일 소스)
    /// </summary>
    public static class LauncherVersionInfo
    {
        /// <summary>
        /// 런처 정수 버전. v1, v2, v3... 의 숫자 부분. 버전 업데이트 시 여기만 수정
        /// </summary>
        public const int Version = 20;

        /// <summary>
        /// 화면에 표시할 코드 형태 (예: "v1").
        /// </summary>
        public static string VersionCode => $"v{Version}";

        /// <summary>
        /// 윈도우 타이틀에 사용할 전체 문자열 (예: "GW Launcher v1").
        /// </summary>
        public static string WindowTitle => $"GW Launcher {VersionCode}";

        /// <summary>
        /// 서버와 버전 비교할 때 사용할 값.
        /// 나중에 launcher.json 등에 1, 2, 3 으로 넣고 비교하면 됨.
        /// </summary>
        public static int VersionForServer => Version;
    }
}
