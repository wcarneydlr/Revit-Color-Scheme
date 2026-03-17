using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace ColorSchemeAddin.Views
{
    public class BoolToVisibilityConverter : IValueConverter
    {
        public static readonly BoolToVisibilityConverter Instance = new();
        public object Convert(object value, Type t, object p, CultureInfo c)
            => value is true ? Visibility.Visible : Visibility.Collapsed;
        public object ConvertBack(object value, Type t, object p, CultureInfo c)
            => value is Visibility.Visible;
    }

    public class InverseBoolToVisibilityConverter : IValueConverter
    {
        public static readonly InverseBoolToVisibilityConverter Instance = new();
        public object Convert(object value, Type t, object p, CultureInfo c)
            => value is true ? Visibility.Collapsed : Visibility.Visible;
        public object ConvertBack(object value, Type t, object p, CultureInfo c)
            => value is Visibility.Collapsed;
    }

    /// <summary>Count > 0 -> Visible, Count == 0 -> Collapsed</summary>
    public class ZeroToCollapsedConverter : IValueConverter
    {
        public static readonly ZeroToCollapsedConverter Instance = new();
        public object Convert(object value, Type t, object p, CultureInfo c)
            => value is int n && n > 0 ? Visibility.Visible : Visibility.Collapsed;
        public object ConvertBack(object value, Type t, object p, CultureInfo c)
            => throw new NotImplementedException();
    }

    /// <summary>Count == 0 -> Visible (empty-state panels)</summary>
    public class ZeroToVisibleConverter : IValueConverter
    {
        public static readonly ZeroToVisibleConverter Instance = new();
        public object Convert(object value, Type t, object p, CultureInfo c)
            => value is int n && n == 0 ? Visibility.Visible : Visibility.Collapsed;
        public object ConvertBack(object value, Type t, object p, CultureInfo c)
            => throw new NotImplementedException();
    }

    public class NullToCollapsedConverter : IValueConverter
    {
        public static readonly NullToCollapsedConverter Instance = new();
        public object Convert(object value, Type t, object p, CultureInfo c)
            => value is string s && !string.IsNullOrWhiteSpace(s)
                ? Visibility.Visible : Visibility.Collapsed;
        public object ConvertBack(object value, Type t, object p, CultureInfo c)
            => throw new NotImplementedException();
    }

    public class ByteClampConverter : IValueConverter
    {
        public static readonly ByteClampConverter Instance = new();
        public object Convert(object value, Type t, object p, CultureInfo c)
            => value?.ToString() ?? "0";
        public object ConvertBack(object value, Type t, object p, CultureInfo c)
        {
            if (value is string s && int.TryParse(s, out int n))
                return (byte)Math.Clamp(n, 0, 255);
            return (byte)0;
        }
    }
}
