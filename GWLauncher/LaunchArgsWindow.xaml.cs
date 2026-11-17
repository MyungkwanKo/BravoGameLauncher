using System.Windows;

namespace BravoGameLauncherGui
{
    /// <summary>
    /// GameStarter 클라이언트/DS 실행 옵션 입력 모달 (v31).
    /// 확인: DialogResult=true 후 ClientArgs/DsArgs 로 값 전달, 닫기: 저장하지 않고 닫음.
    /// 입력값은 가공하지 않고 그대로 반환한다(줄바꿈 포함, 빈 값 허용).
    /// </summary>
    public partial class LaunchArgsWindow : Window
    {
        public string ClientArgs { get; private set; }
        public string DsArgs { get; private set; }

        public LaunchArgsWindow(string clientArgs, string dsArgs)
        {
            InitializeComponent();
            ClientArgs = clientArgs ?? "";
            DsArgs = dsArgs ?? "";
            TbClientArgs.Text = ClientArgs;
            TbDsArgs.Text = DsArgs;
            Loaded += (_, __) => { TbClientArgs.Focus(); TbClientArgs.CaretIndex = TbClientArgs.Text.Length; };
        }

        private void BtnConfirm_Click(object sender, RoutedEventArgs e)
        {
            ClientArgs = TbClientArgs.Text;
            DsArgs = TbDsArgs.Text;
            DialogResult = true;
        }
    }
}
