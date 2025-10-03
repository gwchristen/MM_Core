using System;
using System.Globalization;
using System.Windows.Data;

namespace CmdRunnerPro
{
    public sealed class NullToBoolConverter : IValueConverter
    {
        public bool Invert { get; set; }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool has = value != null;
            return Invert ? !has : has;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}