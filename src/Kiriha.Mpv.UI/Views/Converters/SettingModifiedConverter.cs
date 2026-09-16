using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Kiriha.Mpv.UI.Views.Converters;

public class SettingModifiedConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null && parameter is null)
            return false;

        object? actualValue = value;
        if (value is not null)
        {
            var valProp = value.GetType().GetProperty("Value");
            if (valProp is not null)
            {
                actualValue = valProp.GetValue(value);
            }
        }

        if (actualValue is null && parameter is null)
            return false;

        // String null or empty checks
        string sVal = actualValue?.ToString()?.Trim() ?? string.Empty;
        string sParam = parameter?.ToString()?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(sVal) && string.IsNullOrEmpty(sParam))
            return false;

        // Boolean comparison
        if (actualValue is bool bVal && bool.TryParse(sParam, out bool bParam))
        {
            return bVal != bParam;
        }

        // Numeric comparison (double / int / float)
        if (double.TryParse(sVal, NumberStyles.Any, CultureInfo.InvariantCulture, out double dVal) &&
            double.TryParse(sParam, NumberStyles.Any, CultureInfo.InvariantCulture, out double dParam))
        {
            return Math.Abs(dVal - dParam) > 0.001;
        }

        // String / Enum comparison (case-insensitive)
        return !string.Equals(sVal, sParam, StringComparison.OrdinalIgnoreCase);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
