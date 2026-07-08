using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MessageBox = System.Windows.MessageBox;

namespace BravoGameLauncherGui
{
    /// <summary>
    /// Perforce 설정 섹션 - "조회" 버튼으로 여는 워크스페이스 선택 팝업.
    /// 선택된 P4User + 로컬 host 기준으로 필터링된 워크스페이스 목록을 보여주고,
    /// 사용자가 하나를 골라 확인을 누르면 <see cref="SelectedWorkspaceName"/>에 담아 반환한다.
    /// </summary>
    public partial class P4WorkspaceLookupWindow : Window
    {
        public string? SelectedWorkspaceName { get; private set; }

        private class WorkspaceRow
        {
            public string Client { get; set; } = string.Empty;
            public string Root { get; set; } = string.Empty;
        }

        public P4WorkspaceLookupWindow(List<(string Client, string Root, string Host)> workspaces, string p4user, string host)
        {
            InitializeComponent();

            TxtContext.Text = $"P4User: {p4user} · Host: {host}";

            var rows = workspaces
                .Select(w => new WorkspaceRow { Client = w.Client, Root = w.Root })
                .ToList();

            LbWorkspaces.ItemsSource = rows;

            if (rows.Count == 0)
            {
                LbWorkspaces.Visibility = Visibility.Collapsed;
                TxtEmpty.Visibility = Visibility.Visible;
            }
            else
            {
                LbWorkspaces.SelectedIndex = 0;
            }
        }

        private void BtnConfirm_Click(object sender, RoutedEventArgs e)
        {
            if (LbWorkspaces.SelectedItem is not WorkspaceRow row)
            {
                MessageBox.Show("워크스페이스를 선택하세요.", "선택 필요", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            SelectedWorkspaceName = row.Client;
            DialogResult = true;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void LbWorkspaces_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (LbWorkspaces.SelectedItem is WorkspaceRow row)
            {
                SelectedWorkspaceName = row.Client;
                DialogResult = true;
            }
        }
    }
}
