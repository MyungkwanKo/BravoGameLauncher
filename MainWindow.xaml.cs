using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms; // FolderBrowserDialog
using MessageBox = System.Windows.MessageBox;

namespace BravoGameLauncherGui
{
    public partial class MainWindow : Window
    {
        private readonly AppSettings _settings;
        private readonly GameBuildLauncher _launcher;

        public MainWindow()
        {
            InitializeComponent();

            // 설정 로드
            _settings = AppSettings.Load();

            // 런처 생성 (캐시 루트 경로를 설정에서 가져옴)
            _launcher = new GameBuildLauncher(AppendLog, _settings.RootDownloadDir);

            // UI 초기화
            TxtCachePath.Text = _launcher.RootDownloadDir;
            AppendLog("=== Bravo Game Launcher (GUI) ===");
            AppendLog($"캐시 루트 경로: {_launcher.RootDownloadDir}");
            AppendLog("");

            RefreshRecentFileNames();
        }

        /// <summary>
        /// ComboBox에 최근 파일명 리스트를 바인딩
        /// </summary>
        private void RefreshRecentFileNames()
        {
            CmbFileName.ItemsSource = null;
            CmbFileName.ItemsSource = _settings.RecentFileNames;

            if (_settings.RecentFileNames.Count > 0)
            {
                CmbFileName.Text = _settings.RecentFileNames[0];
            }
            else
            {
                // 초기 기본값 (원하는 예시 하나 넣어두기)
                CmbFileName.Text = "GW_v0.0.1_CL2229_Shipping_20251201220028.zip";
            }
        }

        /// <summary>
        /// 실행 버튼 클릭: ZIP 파일명 기준으로 빌드 실행
        /// </summary>
        private async void BtnRun_Click(object sender, RoutedEventArgs e)
        {
            BtnRun.IsEnabled = false;

            try
            {
                string fileName = (CmbFileName.Text ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    AppendLog("[WARN] 파일명을 입력하세요.");
                    return;
                }

                // 최근 목록 업데이트 & 저장
                _settings.AddRecentFileName(fileName);
                _settings.Save();
                RefreshRecentFileNames();

                // 실제 실행 로직 호출
                await _launcher.RunAsync(fileName);
            }
            catch (Exception ex)
            {
                AppendLog("[ERROR] 예외가 발생했습니다.");
                AppendLog(ex.Message);
            }
            finally
            {
                BtnRun.IsEnabled = true;
            }
        }

        /// <summary>
        /// 로그 출력 (UI 스레드에서 안전하게 호출)
        /// </summary>
        private void AppendLog(string message)
        {
            Dispatcher.Invoke(() =>
            {
                TxtLog.AppendText(message + Environment.NewLine);
                TxtLog.ScrollToEnd();
            });
        }

        // ================================
        // 메뉴: 캐시 경로 변경 / 캐시 삭제 / 종료
        // ================================

        private void MenuChangeCachePath_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "게임 빌드 캐시를 저장할 폴더를 선택하세요.",
                SelectedPath = _launcher.RootDownloadDir,
                ShowNewFolderButton = true
            };

            var result = dialog.ShowDialog();
            if (result != System.Windows.Forms.DialogResult.OK)
                return;

            string newPath = dialog.SelectedPath;
            if (string.IsNullOrWhiteSpace(newPath))
                return;

            // 설정 및 런처에 반영
            _settings.RootDownloadDir = newPath;
            _settings.Save();

            _launcher.ChangeRootDownloadDir(newPath);

            TxtCachePath.Text = newPath;
            AppendLog($"[INFO] 캐시 경로가 변경되었습니다 → {newPath}");
        }

        private void MenuClearCache_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                $"캐시 폴더 전체를 삭제합니다.\n\n경로: {_launcher.RootDownloadDir}\n\n계속하시겠습니까?",
                "캐시 삭제 확인",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                if (Directory.Exists(_launcher.RootDownloadDir))
                {
                    Directory.Delete(_launcher.RootDownloadDir, recursive: true);
                    AppendLog("[INFO] 캐시 폴더를 삭제했습니다.");
                }
                else
                {
                    AppendLog("[INFO] 삭제할 캐시 폴더가 없습니다.");
                }
            }
            catch (Exception ex)
            {
                AppendLog("[ERROR] 캐시 삭제 중 오류가 발생했습니다.");
                AppendLog(ex.Message);
            }
        }

        private void MenuExit_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
