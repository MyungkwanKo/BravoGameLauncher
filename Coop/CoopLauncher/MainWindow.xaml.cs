using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using BravoGameLauncherGui;
using MessageBox = System.Windows.MessageBox;

namespace CoopGameLauncher;

public partial class MainWindow : Window
{
    private readonly AppSettings _settings;
    private readonly GameBuildLauncher _launcher;
    private List<ServerBuildItem> _allBuilds = new();

    private class ServerBuildItem
    {
        public int BuildNo { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string DsFileName { get; set; } = string.Empty;
        public string Config { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public int CL { get; set; }
        public string BuildDate { get; set; } = string.Empty;
        public string BuildTime { get; set; } = string.Empty;
        public string DS { get; set; } = "x";
        public DateTime SortKey { get; set; }
    }

    public MainWindow()
    {
        InitializeComponent();

        _settings = AppSettings.Load();
        _launcher = new GameBuildLauncher(_ => { }, _settings.RootDownloadDir);

        TxtCachePath.Text = _launcher.RootDownloadDir;

        CmbBuildType.SelectionChanged += (_, __) => RefreshBuildListUI();
        Loaded += async (_, __) =>
        {
            try { await RefreshFromServerAsync(); }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"초기 빌드 목록 로드에 실패했습니다.\n\n{ex.Message}",
                    "오류",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        };
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
            .Where(b => string.Equals(selectedType, "All", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(b.Config, selectedType, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(b => b.SortKey)
            .ToList();

        if (filtered.Count == 0)
        {
            LvBuilds.ItemsSource = null;
            MessageBox.Show(
                $"'{selectedType}' 타입 빌드가 없습니다.",
                "빌드 목록",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        LvBuilds.ItemsSource = filtered;
        LvBuilds.SelectedIndex = 0;
    }

    private string GetSelectedBuildType()
    {
        if (CmbBuildType?.SelectedItem is ComboBoxItem cbi &&
            cbi.Content is string content &&
            !string.IsNullOrWhiteSpace(content))
        {
            return content;
        }
        return "All";
    }

    private async void BtnRun_Click(object sender, RoutedEventArgs e)
    {
        await RunSelectedBuildAsync();
    }

    private void MenuChangeCachePath_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "게임 빌드 캐시를 저장할 폴더를 선택하세요.",
            SelectedPath = _launcher.RootDownloadDir,
            ShowNewFolderButton = true
        };

        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

        string newPath = dialog.SelectedPath;
        if (string.IsNullOrWhiteSpace(newPath)) return;

        _settings.RootDownloadDir = newPath;
        _settings.Save();

        _launcher.ChangeRootDownloadDir(newPath);

        TxtCachePath.Text = newPath;
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
                Directory.Delete(_launcher.RootDownloadDir, recursive: true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"캐시 삭제 중 오류가 발생했습니다.\n\n{ex.Message}",
                "오류",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void MenuExit_Click(object sender, RoutedEventArgs e) => Close();

    private void MenuOpenLauncherFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string? dir = Path.GetDirectoryName(Environment.ProcessPath);
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
                dir = AppContext.BaseDirectory;
            if (string.IsNullOrWhiteSpace(dir))
            {
                MessageBox.Show("런처 실행 경로를 확인할 수 없습니다.", "경로 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            Process.Start(new ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"탐색기 열기 실패: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnOpenCachePath_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string path = _launcher.RootDownloadDir;
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"탐색기 열기 실패: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void BtnRefreshFromServer_Click(object sender, RoutedEventArgs e)
    {
        await RefreshFromServerAsync();
    }

    private async Task RefreshFromServerAsync()
    {
        if (BtnRefreshFromServer != null)
            BtnRefreshFromServer.IsEnabled = false;

        try
        {
            var result = await BuildListService.FetchBuildListAsync();
            if (result == null || result.Platforms == null || result.Platforms.Count == 0)
            {
                _allBuilds.Clear();
                RefreshBuildListUI();
                MessageBox.Show("서버에서 빌드 정보를 가져오지 못했습니다.", "빌드 목록", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var winBuilds = result.Platforms.TryGetValue("WIN", out var win) ? win.Builds : new List<BuildItem>();
            var dsBuilds = result.Platforms.TryGetValue("DS", out var ds) ? ds.Builds : new List<BuildItem>();

            if (winBuilds == null || winBuilds.Count == 0)
            {
                _allBuilds.Clear();
                RefreshBuildListUI();
                MessageBox.Show("서버 WIN 빌드 목록이 비어 있습니다.", "빌드 목록", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dsByBuildNo = dsBuilds
                .Where(b => b.JenkinsBuildNumber > 0 && !string.IsNullOrWhiteSpace(b.FileName))
                .GroupBy(b => b.JenkinsBuildNumber)
                .ToDictionary(g => g.Key, g => g.First().FileName);

            var dsNameSet = new HashSet<string>(
                dsBuilds
                    .Where(b => !string.IsNullOrWhiteSpace(b.FileName))
                    .Select(b => Path.GetFileNameWithoutExtension(b.FileName)),
                StringComparer.OrdinalIgnoreCase);

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

                var baseName = Path.GetFileNameWithoutExtension(item.FileName);
                var expectedDsBaseName = baseName + "_DS";
                bool hasDs = (item.JenkinsBuildNumber > 0 && dsByBuildNo.ContainsKey(item.JenkinsBuildNumber))
                          || dsNameSet.Contains(expectedDsBaseName);
                string matchedDsFileName = (item.JenkinsBuildNumber > 0 && dsByBuildNo.TryGetValue(item.JenkinsBuildNumber, out var byNo))
                    ? byNo
                    : (dsNameSet.Contains(expectedDsBaseName) ? expectedDsBaseName + ".zip" : string.Empty);

                _allBuilds.Add(new ServerBuildItem
                {
                    BuildNo = item.JenkinsBuildNumber,
                    FileName = item.FileName,
                    DsFileName = matchedDsFileName,
                    Config = !string.IsNullOrWhiteSpace(item.Config) ? item.Config : GetBuildConfigFromFileName(item.FileName),
                    Version = !string.IsNullOrWhiteSpace(item.Version) ? item.Version : (parse.Version ?? string.Empty),
                    CL = item.Cl != 0 ? item.Cl : parse.CL,
                    BuildDate = dt == DateTime.MinValue ? "" : dt.ToString("yyyy-MM-dd"),
                    BuildTime = dt == DateTime.MinValue ? "" : dt.ToString("HH:mm:ss"),
                    DS = hasDs ? "O" : "X",
                    SortKey = dt
                });
            }

            RefreshBuildListUI();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"빌드 목록을 불러오지 못했습니다.\n\n{ex.Message}",
                "오류",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            if (BtnRefreshFromServer != null)
                BtnRefreshFromServer.IsEnabled = true;
        }
    }

    private static (string Version, int CL, DateTime? Timestamp) ParseBuildInfoFromFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return (string.Empty, 0, null);

        var pattern = @"^GW_v(?<ver>\d+\.\d+\.\d+)_CL(?<cl>\d+)_.*_(?<ts>\d{14})(?:_DS)?(?:\.zip)?$";
        var m = Regex.Match(fileName, pattern, RegexOptions.IgnoreCase);
        if (!m.Success)
            return (string.Empty, 0, null);

        string ver = m.Groups["ver"].Value;
        int cl = int.TryParse(m.Groups["cl"].Value, out var clVal) ? clVal : 0;

        string ts = m.Groups["ts"].Value;
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

    private async Task RunSelectedBuildAsync()
    {
        if (LvBuilds.SelectedItem is not ServerBuildItem selected)
        {
            MessageBox.Show("실행할 빌드를 목록에서 선택하세요.", "선택 필요", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        bool runClient = CbRunClient.IsChecked == true;
        bool runDS = CbRunDS.IsChecked == true;

        if (!runClient && !runDS)
        {
            MessageBox.Show("클라이언트 또는 DS 중 하나 이상을 선택한 뒤 실행해주세요.", "실행 대상 선택", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        BtnRun.IsEnabled = false;

        const string clientArgs = "";
        string dsArgs = GameBuildLauncher.DefaultDedicatedServerArgs;

        try
        {
            bool useWindowed = CbWindowed.IsChecked == true;
            string winZip = selected.FileName;
            string dsZip = !string.IsNullOrWhiteSpace(selected.DsFileName)
                ? selected.DsFileName
                : Path.GetFileNameWithoutExtension(winZip) + "_DS.zip";

            if (runClient && runDS)
            {
                await _launcher.RunLocalWithDedicatedServerAsync(winZip, dsZip, useWindowed, ReportGameStarterProgress, clientArgs, dsArgs);
            }
            else if (runClient)
            {
                await _launcher.RunLocalClientOnlyAsync(winZip, useWindowed, ReportGameStarterProgress, clientArgs);
            }
            else
            {
                await _launcher.RunDedicatedServerAsync(dsZip, ReportGameStarterProgress, dsArgs);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"게임 실행 중 오류가 발생했습니다.\n\n{ex.Message}",
                "실행 오류",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            BtnRun.IsEnabled = true;
            SetGameStarterProgress(false, 0, null);
        }
    }

    private void ReportGameStarterProgress(double percent, string? message)
    {
        Dispatcher.Invoke(() =>
        {
            if (PbGameStarter != null)
            {
                PbGameStarter.IsIndeterminate = double.IsNaN(percent) || percent < 0;
                if (!PbGameStarter.IsIndeterminate)
                    PbGameStarter.Value = Math.Min(100, Math.Max(0, percent));
            }
            if (TxtGameStarterProgress != null)
                TxtGameStarterProgress.Text = message ?? "";
        });
    }

    private void SetGameStarterProgress(bool indeterminate, double value, string? text)
    {
        Dispatcher.Invoke(() =>
        {
            if (PbGameStarter != null)
            {
                PbGameStarter.IsIndeterminate = indeterminate;
                PbGameStarter.Value = value;
            }
            if (TxtGameStarterProgress != null)
                TxtGameStarterProgress.Text = text ?? "";
        });
    }
}
