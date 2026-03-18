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
                return (byte)Math.Max(0, Math.Min(255, n));
            return (byte)0;
        }
    }
    /// <summary>
    /// int == int parameter -> Visible/true, else Collapsed/false.
    /// Used to show/hide views based on SelectedTabIndex.
    /// </summary>
    public class IntEqualConverter : IValueConverter
    {
        public static readonly IntEqualConverter Instance = new();

        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            if (value is int v && p != null && int.TryParse(p.ToString(), out int target))
            {
                if (t == typeof(Visibility))
                    return v == target ? Visibility.Visible : Visibility.Collapsed;
                return v == target;
            }
            return t == typeof(Visibility) ? Visibility.Collapsed : (object)false;
        }

        public object ConvertBack(object value, Type t, object p, CultureInfo c)
        {
            if (value is true && p != null && int.TryParse(p.ToString(), out int target))
                return target;
            return System.Windows.Data.Binding.DoNothing;
        }
    }
    /// <summary>bool -> "✓" or "–"</summary>
    public class BoolToCheckmarkConverter : IValueConverter
    {
        public static readonly BoolToCheckmarkConverter Instance = new();
        public object Convert(object value, Type t, object p, CultureInfo c)
            => value is true ? "✓" : "–";
        public object ConvertBack(object value, Type t, object p, CultureInfo c)
            => throw new NotImplementedException();
    }

    /// <summary>Enum == string parameter -> Visible/true, else Collapsed/false.</summary>
    public class EnumEqualConverter : IValueConverter
    {
        public static readonly EnumEqualConverter Instance = new();
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            if (value == null || p == null) return t == typeof(Visibility) ? Visibility.Collapsed : (object)false;
            bool equal = value.ToString() == p.ToString();
            if (t == typeof(Visibility)) return equal ? Visibility.Visible : Visibility.Collapsed;
            return equal;
        }
        public object ConvertBack(object value, Type t, object p, CultureInfo c)
        {
            if (value is true && p != null)
                return Enum.Parse(t, p.ToString()!);
            return System.Windows.Data.Binding.DoNothing;
        }
    }

}
