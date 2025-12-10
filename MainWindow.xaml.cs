using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms; // FolderBrowserDialog
using MessageBox = System.Windows.MessageBox;
using System.Collections.Generic;
using System.Windows.Controls;
using System.Linq;

namespace BravoGameLauncherGui
{
    public partial class MainWindow : Window
    {
        private readonly AppSettings _settings;
        private readonly GameBuildLauncher _launcher;

        // 서버에서 받은 빌드 전체 목록 (파일명 + Config)
        private List<ServerBuildItem> _serverBuilds = new();

        private class ServerBuildItem
        {
            public string FileName { get; set; } = string.Empty;
            public string Config   { get; set; } = string.Empty;
        }

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
            AppendLog(string.Empty);

            // 빌드 타입 변경 시 목록 갱신
            if (CmbBuildType != null)
                CmbBuildType.SelectionChanged += CmbBuildType_SelectionChanged;

            // 초기에는 목록 비워두기
            RefreshComboItems();

            // 창이 로드되면 자동으로 서버 목록 새로고침
            Loaded += async (_, __) => await RefreshFromServerAsync();
        }

        /// <summary>
        /// ComboBox에 서버 빌드 리스트를 바인딩 (빌드 타입 필터 포함)
        /// </summary>
        private void RefreshComboItems()
        {
            // 서버에서 아직 아무 것도 못 받아온 경우
            if (_serverBuilds == null || _serverBuilds.Count == 0)
            {
                CmbFileName.ItemsSource = null;
                CmbFileName.Text = string.Empty;
                return;
            }

            // 선택된 빌드 타입 (기본값: Development)
            string selectedType = "Development";

            if (CmbBuildType?.SelectedItem is ComboBoxItem cbi &&
                cbi.Content is string content &&
                !string.IsNullOrWhiteSpace(content))
            {
                selectedType = content;
            }

            // 선택한 타입만 필터링
            var items = _serverBuilds
                .Where(b => string.Equals(
                    b.Config,
                    selectedType,
                    StringComparison.OrdinalIgnoreCase))
                .Select(b => b.FileName)
                .ToList();

            // 선택한 타입의 빌드가 하나도 없는 경우
            if (items.Count == 0)
            {
                CmbFileName.ItemsSource = null;
                CmbFileName.Text        = string.Empty;

                AppendLog($"[WARN] 서버 빌드 리스트 중 '{selectedType}' 타입 빌드가 없습니다.");
                return;
            }

            // 콤보박스에 목록 반영
            CmbFileName.ItemsSource = items;

            // 기본 선택은 최신 하나
            CmbFileName.Text = items[0];

            AppendLog($"[INFO] '{selectedType}' 타입 빌드 {items.Count}개 표시.");
        }

        private void CmbBuildType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            RefreshComboItems();
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
                RefreshComboItems();

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

        private async void BtnRefreshFromServer_Click(object sender, RoutedEventArgs e)
        {
            await RefreshFromServerAsync();
        }

        private async Task RefreshFromServerAsync()
        {
            // 버튼에서 호출될 수도 있고, Loaded에서 자동 호출될 수도 있으니
            // 버튼이 null일 가능성도 고려해서 null 체크
            if (BtnRefreshFromServer != null)
                BtnRefreshFromServer.IsEnabled = false;

            AppendLog("[INFO] 서버에서 빌드 리스트를 가져오는 중...");

            try
            {
                var result = await BuildListService.FetchBuildListAsync();
                if (result == null || result.Builds == null || result.Builds.Count == 0)
                {
                    AppendLog("[WARN] 서버에서 가져온 빌드 정보가 없습니다.");
                    _serverBuilds.Clear();
                    RefreshComboItems();
                    return;
                }

                // buildTime 기준으로 내림차순 정렬
                result.Builds.Sort((a, b) =>
                {
                    var ta = a.BuildTime ?? DateTime.MinValue;
                    var tb = b.BuildTime ?? DateTime.MinValue;
                    return tb.CompareTo(ta);
                });

                // 서버 전체 빌드 목록 저장 (파일명 + Config)
                _serverBuilds = new List<ServerBuildItem>();

                foreach (var item in result.Builds)
                {
                    if (string.IsNullOrWhiteSpace(item.FileName))
                        continue;

                    // 파일명에서 Config(Development/Shipping) 파싱
                    var config = GetBuildConfigFromFileName(item.FileName);

                    _serverBuilds.Add(new ServerBuildItem
                    {
                        FileName = item.FileName,
                        Config   = config
                    });
                }

                AppendLog($"[INFO] 서버 빌드 리스트 {_serverBuilds.Count}개 로드 완료.");
                RefreshComboItems();
            }
            catch (Exception ex)
            {
                AppendLog("[ERROR] 서버 빌드 리스트 로드 실패.");
                AppendLog(ex.Message);
            }
            finally
            {
                if (BtnRefreshFromServer != null)
                    BtnRefreshFromServer.IsEnabled = true;
            }
        }

        private static string GetBuildConfigFromFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return "Unknown";

            // 예: GW_v0.0.1_CL2301_Shipping_20251205123010.zip
            if (fileName.IndexOf("_Shipping_", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Shipping";

            if (fileName.IndexOf("_Development_", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Development";

            return "Unknown";
        }
    }
}
