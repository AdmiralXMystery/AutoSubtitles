using System;
using System.Globalization;
using System.Windows.Data;

namespace AutoSubtitles
{
    /// <summary>
    /// Преобразует долю (0.0 – 1.0) в GridLength со звёздной единицей.
    /// Используется для пропорционального распределения колонок индикатора прогресса.
    /// </summary>
    public class DoubleToStarGridLengthConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double d && d > 0)
                return new System.Windows.GridLength(d, System.Windows.GridUnitType.Star);

            return new System.Windows.GridLength(0);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}