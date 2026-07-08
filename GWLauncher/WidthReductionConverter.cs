using System;
using System.Globalization;
using System.Windows.Data;

namespace BravoGameLauncherGui
{
    // GW Sync 탭의 섹션 헤더(Expander.Header) 너비를 계산할 때 사용.
    // Expander 기본 컨트롤 템플릿은 헤더 ContentPresenter가 HorizontalAlignment="Left"로
    // 고정되어 있어 Stretch가 통하지 않으므로, 상위 StackPanel의 ActualWidth를 기준으로
    // Border/Padding 등 고정 여백만큼 뺀 값을 명시적으로 Width에 바인딩해 채운다.
    public class WidthReductionConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double width)
            {
                double reduction = 16;
                if (parameter != null && double.TryParse(parameter.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double parsed))
                {
                    reduction = parsed;
                }

                double result = width - reduction;
                return result > 0 ? result : 0;
            }

            return value ?? 0d;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
