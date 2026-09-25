using System;
using System.Globalization;
using System.Windows.Data;

namespace AutoSubtitles
{
    public class InfinityConverter : IValueConverter
    {
        // Перевод из double (C# код) в string (текст в TextBox)
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double d && double.IsPositiveInfinity(d))
            {
                return "inf";
            }
            return value?.ToString() ?? "inf";
        }

        // Перевод из string (ввод пользователя) в double (C# код)
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string text = value?.ToString()?.Trim().ToLower() ?? "inf";

            // Если пользователь написал inf или infinity — возвращаем бесконечность
            if (text == "inf" || text == "infinity")
            {
                return double.PositiveInfinity;
            }

            // Пытаемся распарсить как обычное число (с точкой или запятой)
            if (double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out double result))
            {
                return result;
            }
            if (double.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out double resultLocal))
            {
                return resultLocal;
            }

            // Возвращаем дефолт, если пользователь ввёл некорректный текст
            return double.PositiveInfinity;
        }
    }
}