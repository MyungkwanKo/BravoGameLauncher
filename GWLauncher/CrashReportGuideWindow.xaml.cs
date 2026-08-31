using System;
using System.IO;
using System.Windows;

namespace BravoGameLauncherGui
{
    /// <summary>
    /// 크래시 로그 zip을 클립보드에 올린 뒤, Slack에 붙여넣는 방법을 안내하는 창 (v30, #PJTGW-3099).
    /// 클립보드가 다른 내용으로 덮인 경우를 대비해 파일/메시지를 다시 복사할 수 있다.
    /// </summary>
    public partial class CrashReportGuideWindow : Window
    {
        private readonly string _zipPath;
        private readonly string _message;

        public CrashReportGuideWindow(string zipPath, string message, bool slackOpened)
        {
            InitializeComponent();

            _zipPath = zipPath ?? string.Empty;
            _message = message ?? string.Empty;

            TxtStatus.Text = slackOpened
                ? "zip을 클립보드에 복사하고 Slack 채널을 열었습니다."
                : "zip을 클립보드에 복사했습니다. Slack 채널을 자동으로 열지 못했으니 Slack에서 대상 채널을 직접 열어주세요.";

            TbMessage.Text = _message;
            TbZipPath.Text = _zipPath;
        }

        private void BtnOpenChannel_Click(object sender, RoutedEventArgs e)
        {
            // slack:// 딥링크로 앱이 열리지 않은 경우를 위한 수동 폴백(웹 클라이언트로 채널 열기)
            bool ok = CrashLogReporter.TryOpenChannelLink();
            TxtHint.Text = ok
                ? "웹 브라우저로 채널을 열었습니다."
                : "채널을 열지 못했습니다. Slack에서 대상 채널을 직접 열어주세요.";
        }

        private void BtnCopyFile_Click(object sender, RoutedEventArgs e)
        {
            bool ok = CrashLogReporter.TryCopyToClipboard(_zipPath, _message);
            TxtHint.Text = ok
                ? "zip 파일을 클립보드에 다시 복사했습니다. Slack 입력창에서 Ctrl+V 하세요."
                : "클립보드 복사에 실패했습니다. [폴더 열기]로 zip을 직접 끌어다 놓아주세요.";
        }

        private void BtnCopyMessage_Click(object sender, RoutedEventArgs e)
        {
            bool ok = CrashLogReporter.TryCopyTextToClipboard(_message);
            TxtHint.Text = ok
                ? "메시지를 클립보드에 복사했습니다. 파일을 먼저 첨부한 뒤 입력창에서 Ctrl+V 하세요."
                : "클립보드 복사에 실패했습니다. 위 메시지 내용을 직접 선택해 복사해주세요.";
        }

        private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            string? folder = null;

            try
            {
                folder = Path.GetDirectoryName(_zipPath);
            }
            catch
            {
                // 경로가 비정상인 경우 아래에서 실패 처리
            }

            bool ok = !string.IsNullOrWhiteSpace(folder) && CrashLogReporter.TryOpenFolder(folder!);
            if (!ok)
                TxtHint.Text = "폴더를 열지 못했습니다. 위 경로를 복사해 직접 이동해주세요.";
        }
    }
}
