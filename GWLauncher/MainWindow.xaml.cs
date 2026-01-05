﻿using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms; // FolderBrowserDialog
using MessageBox = System.Windows.MessageBox;
using System.Collections.Generic;
using System.Windows.Controls;
using System.Linq;
using System.Text.RegularExpressions;

namespace BravoGameLauncherGui
{
    public partial class MainWindow : Window
    {
        private readonly AppSettings _settings;
        private readonly GameBuildLauncher _launcher;

        // 서버에서 받은 전체 빌드 목록 (WIN 기준)
        private List<ServerBuildItem> _allBuilds = new();

        private class ServerBuildItem
        {
            public int    BuildNo   { get; set; }               // Jenkins build number
            public string FileName  { get; set; } = string.Empty;
            public string Config    { get; set; } = string.Empty;
            public string Version   { get; set; } = string.Empty;
            public int    CL        { get; set; }
            public string BuildDate { get; set; } = string.Empty; // yyyy-MM-dd
            public string BuildTime { get; set; } = string.Empty; // HH:mm:ss
            public string DS        { get; set; } = "x";           // O / X
            public DateTime SortKey { get; set; }                 // 내림차순 정렬용
        }

        public MainWindow()
        {
            InitializeComponent();

            _settings = AppSettings.Load();
            _launcher = new GameBuildLauncher(AppendLog, _settings.RootDownloadDir);

            TxtCachePath.Text = _launcher.RootDownloadDir;
            AppendLog("=== GW Launcher (GUI) ===");
            AppendLog($"캐시 루트 경로: {_launcher.RootDownloadDir}");
            AppendLog(string.Empty);

            CmbBuildType.SelectionChanged += (_, __) => RefreshBuildListUI();

            CbLocal.IsChecked = true;
            CbServer.IsChecked = false;

            Loaded += async (_, __) => await RefreshFromServerAsync();
        }

        private void RefreshBuildListUI()
        {
            if (_allBuilds == null || _allBuilds.Count == 0)
            {
                LvBuilds.ItemsSource = null;
                return;
            }

            string selectedType = GetSelectedBuildType();

            var filtered = _allBuilds
                .Where(b => string.Equals(b.Config, selectedType, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(b => b.SortKey)
                .ToList();

            if (filtered.Count == 0)
            {
                LvBuilds.ItemsSource = null;
                AppendLog($"[WARN] 서버 빌드 리스트 중 '{selectedType}' 타입 빌드가 없습니다.");
                return;
            }

            LvBuilds.ItemsSource = filtered;
            LvBuilds.SelectedIndex = 0;

            AppendLog($"[INFO] '{selectedType}' 타입 빌드 {filtered.Count}개 표시.");
        }

        private string GetSelectedBuildType()
        {
            if (CmbBuildType?.SelectedItem is ComboBoxItem cbi &&
                cbi.Content is string content &&
                !string.IsNullOrWhiteSpace(content))
            {
                return content;
            }
            return "Development";
        }

        private void CbLocal_Click(object sender, RoutedEventArgs e)
        {
            if (CbLocal.IsChecked == true) CbServer.IsChecked = false;
            else if (CbServer.IsChecked != true) CbLocal.IsChecked = true;
        }

        private void CbServer_Click(object sender, RoutedEventArgs e)
        {
            if (CbServer.IsChecked == true) CbLocal.IsChecked = false;
            else if (CbLocal.IsChecked != true) CbServer.IsChecked = true;
        }

        private async void BtnRun_Click(object sender, RoutedEventArgs e)
        {
            BtnRun.IsEnabled = false;

            try
            {
                if (LvBuilds.ItemsSource == null)
                {
                    AppendLog("[WARN] 실행할 빌드가 없습니다. 빌드목록을 먼저 새로고침하세요.");
                    return;
                }

                if (LvBuilds.SelectedItem is not ServerBuildItem selected)
                {
                    AppendLog("[WARN] 실행할 빌드를 체크하세요.");
                    return;
                }

                bool isServer = (CbServer.IsChecked == true);
                string ipAddress = isServer ? "100.66.7.43" : "localhost";
                bool useWindowed = CbWindowed.IsChecked == true;

                string clientZip = selected.FileName;

                _settings.AddRecentFileName(clientZip);
                _settings.Save();

                // 1️⃣ Server 실행: 기존대로 Client만 실행
                if (isServer)
                {
                    await _launcher.RunAsync(clientZip, ipAddress, useWindowed);
                    return;
                }

                // 2️⃣ Local 실행
                if (selected.DS == "O")
                {
                    string baseName = Path.GetFileNameWithoutExtension(clientZip);
                    string dsZip = baseName + "_DS.zip";

                    // ✅ 병렬 준비 + DS 먼저 실행 + Client 실행 (한 방에)
                    await _launcher.RunLocalWithDedicatedServerAsync(clientZip, dsZip, ipAddress, useWindowed);
                }
                else
                {
                    // DS 없으면 Client만 실행
                    await _launcher.RunAsync(clientZip, ipAddress, useWindowed);
                }
            }
            catch (Exception ex)
            {
                AppendLog("[ERROR] 실행 중 예외 발생");
                AppendLog(ex.Message);
            }
            finally
            {
                BtnRun.IsEnabled = true;
            }
        }


        private void AppendLog(string message)
        {
            Dispatcher.Invoke(() =>
            {
                TxtLog.AppendText(message + Environment.NewLine);
                TxtLog.ScrollToEnd();
            });
        }

        private void MenuChangeCachePath_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "게임 빌드 캐시를 저장할 폴더를 선택하세요.",
                SelectedPath = _launcher.RootDownloadDir,
                ShowNewFolderButton = true
            };

            var result = dialog.ShowDialog();
            if (result != System.Windows.Forms.DialogResult.OK) return;

            string newPath = dialog.SelectedPath;
            if (string.IsNullOrWhiteSpace(newPath)) return;

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

            if (result != MessageBoxResult.Yes) return;

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

        private void MenuExit_Click(object sender, RoutedEventArgs e) => Close();

        private async void BtnRefreshFromServer_Click(object sender, RoutedEventArgs e)
        {
            await RefreshFromServerAsync();
        }

        // ====== (1번 기능 핵심) 서버 목록 로드 + DS 존재 여부 계산 ======
        private async Task RefreshFromServerAsync()
        {
            if (BtnRefreshFromServer != null)
                BtnRefreshFromServer.IsEnabled = false;

            AppendLog("[INFO] 서버에서 빌드 리스트를 가져오는 중...");

            try
            {
                var result = await BuildListService.FetchBuildListAsync();
                if (result == null || result.Platforms == null || result.Platforms.Count == 0)
                {
                    AppendLog("[WARN] 서버에서 가져온 빌드 정보가 없습니다.");
                    _allBuilds.Clear();
                    RefreshBuildListUI();
                    return;
                }

                // 클라이언트(WIN) / DS 목록 추출
                var winBuilds = result.Platforms.TryGetValue("WIN", out var win) ? win.Builds : new List<BuildItem>();
                var dsBuilds  = result.Platforms.TryGetValue("DS", out var ds)  ? ds.Builds  : new List<BuildItem>();

                if (winBuilds == null || winBuilds.Count == 0)
                {
                    AppendLog("[WARN] 서버 WIN 빌드 리스트가 비어 있습니다.");
                    _allBuilds.Clear();
                    RefreshBuildListUI();
                    return;
                }

                // DS 존재 여부 빠른 조회용 (파일명 기반)
                // 규칙: <클라이언트빌드명(확장자제거)> + "_DS" 가 DS 플랫폼에 존재하면 O
                var dsNameSet = new HashSet<string>(
                    dsBuilds
                        .Where(b => !string.IsNullOrWhiteSpace(b.FileName))
                        .Select(b => Path.GetFileNameWithoutExtension(b.FileName)),
                    StringComparer.OrdinalIgnoreCase
                );

                // WIN: buildTime 기준 내림차순
                winBuilds.Sort((a, b) =>
                {
                    var ta = a.BuildTime ?? DateTime.MinValue;
                    var tb = b.BuildTime ?? DateTime.MinValue;
                    return tb.CompareTo(ta);
                });

                _allBuilds = new List<ServerBuildItem>();

                foreach (var item in winBuilds)
                {
                    if (string.IsNullOrWhiteSpace(item.FileName))
                        continue;

                    var parse = ParseBuildInfoFromFileName(item.FileName);
                    var dt = item.BuildTime ?? parse.Timestamp ?? DateTime.MinValue;

                    // DS 매칭 (파일명 기반)
                    var baseName = Path.GetFileNameWithoutExtension(item.FileName);
                    var expectedDsBaseName = baseName + "_DS";
                    bool hasDs = dsNameSet.Contains(expectedDsBaseName);

                    _allBuilds.Add(new ServerBuildItem
                    {
                        BuildNo   = item.JenkinsBuildNumber,
                        FileName  = item.FileName,
                        Config    = !string.IsNullOrWhiteSpace(item.Config) ? item.Config : GetBuildConfigFromFileName(item.FileName),
                        Version   = !string.IsNullOrWhiteSpace(item.Version) ? item.Version : (parse.Version ?? string.Empty),
                        CL        = item.Cl != 0 ? item.Cl : parse.CL,
                        BuildDate = dt == DateTime.MinValue ? "" : dt.ToString("yyyy-MM-dd"),
                        BuildTime = dt == DateTime.MinValue ? "" : dt.ToString("HH:mm:ss"),
                        DS        = hasDs ? "O" : "X",
                        SortKey   = dt
                    });
                }

                AppendLog($"[INFO] 서버 빌드 리스트 {_allBuilds.Count}개 로드 완료.");
                RefreshBuildListUI();
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

        // ====== 파일명 파싱 유틸 (기존 유지 + DS/확장자 유연화) ======
        private static (string Version, int CL, DateTime? Timestamp) ParseBuildInfoFromFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return (string.Empty, 0, null);

            // 예:
            //  - GW_v0.0.1_CL2301_Shipping_20251205123010.zip
            //  - GW_v0.0.1_CL2301_Shipping_20251205123010_DS.zip
            //  - (확장자 없는 케이스도 대비) ..._20251205123010_DS
            var pattern = @"^GW_v(?<ver>\d+\.\d+\.\d+)_CL(?<cl>\d+)_.*_(?<ts>\d{14})(?:_DS)?(?:\.zip)?$";
            var m = Regex.Match(fileName, pattern, RegexOptions.IgnoreCase);
            if (!m.Success)
                return (string.Empty, 0, null);

            string ver = m.Groups["ver"].Value;
            int cl = int.TryParse(m.Groups["cl"].Value, out var clVal) ? clVal : 0;

            string ts = m.Groups["ts"].Value; // yyyyMMddHHmmss
            if (DateTime.TryParseExact(ts, "yyyyMMddHHmmss", null,
                    System.Globalization.DateTimeStyles.None, out var dt))
            {
                return (ver, cl, dt);
            }

            return (ver, cl, null);
        }

        private static string GetBuildConfigFromFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return "Unknown";

            if (fileName.IndexOf("_Shipping_", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Shipping";

            if (fileName.IndexOf("_Development_", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Development";

            return "Unknown";
        }
    }
}
