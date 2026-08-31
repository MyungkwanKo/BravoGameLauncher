using System;
using System.Windows;
using System.Windows.Controls;
// System.Drawing 이 global using 되어 있어 Brushes 가 모호해진다(CS0104). WPF 쪽을 별칭으로 고정.
using Brushes = System.Windows.Media.Brushes;

namespace BravoGameLauncherGui
{
    /// <summary>
    /// 크래시 로그 전송 전에 사용자가 크래시 상황을 입력하는 모달 창 (v30, #PJTGW-3099).
    /// 취소하면 zip 압축을 포함해 아무 작업도 하지 않고 종료한다.
    /// </summary>
    public partial class CrashReportInputWindow : Window
    {
        /// <summary>글자 수 카운터를 강조 색으로 바꾸기 시작하는 길이.</summary>
        private const int WarnLength = 90;

        /// <summary>확인을 눌렀을 때 사용자가 입력한 크래시 상황(앞뒤 공백 제거).</summary>
        public string ReportText { get; private set; } = string.Empty;

        public CrashReportInputWindow(string buildName)
        {
            InitializeComponent();

            TxtBuildInfo.Text = $"대상 빌드: {buildName}";
            TbReport.MaxLength = CrashLogReporter.MaxReportLength;

            UpdateState();

            Loaded += (_, __) => TbReport.Focus();
        }

        private void TbReport_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateState();
        }

        /// <summary>글자 수 표시와 확인 버튼 활성 상태를 갱신한다.</summary>
        private void UpdateState()
        {
            // InitializeComponent 도중 TextChanged가 먼저 불릴 수 있어 방어적으로 확인한다.
            if (TbReport == null || TxtCounter == null || BtnConfirm == null)
                return;

            string text = TbReport.Text ?? string.Empty;

            TxtCounter.Text = $"{text.Length} / {CrashLogReporter.MaxReportLength}";
            TxtCounter.Foreground = text.Length >= WarnLength ? Brushes.DarkOrange : Brushes.Gray;

            // 공백만 입력한 경우도 전송할 내용이 없는 것으로 보고 비활성 유지
            BtnConfirm.IsEnabled = !string.IsNullOrWhiteSpace(text);
        }

        private void BtnConfirm_Click(object sender, RoutedEventArgs e)
        {
            string text = (TbReport.Text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(text))
                return;

            ReportText = text;
            DialogResult = true;
        }
    }
}
