using Kiriha.Core.Abstractions.Models.Entities;
using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Styling;
using Kiriha.Core;

namespace Kiriha.Views.Converters;

public class LocConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Enum e)
        {
            value = e.ToString();
        }

        string? param = parameter?.ToString();
        string? keyStr = value?.ToString();

        // Special wizard logic
        if (param == "language_step") return keyStr == "language";
        if (param == "theme_step") return keyStr == "theme";
        if (param == "mal_step") return keyStr == "mal_login";
        if (param == "shiki_step") return keyStr == "shiki_login";
        if (param == "scrobbler_step") return keyStr == "scrobbler";
        if (param == "system_step") return keyStr == "system_settings";
        if (param == "advanced_step") return keyStr == "advanced_localization";
        if (param == "theme_icon")
        {
            if (value is ThemeType tt)
            {
                return tt switch
                {
                    ThemeType.Light => "Sun",
                    ThemeType.Dark => "Moon",
                    _ => "Monitor"
                };
            }
            return keyStr?.ToLowerInvariant() switch
            {
                "???????" or "light" => "Sun",
                "??????" or "??????" or "dark" => "Moon",
                _ => "Monitor"
            };
        }
        if (param == "language_check") return keyStr == "language";

        // Button logic based on IsLastStep (value is bool)
        if (param == "btn_text")
        {
            bool isLast = (bool)(value ?? false);
            return UIUtils.GetLoc(isLast ? "wizard.finish" : "wizard.next");
        }
        if (param == "show_arrow") return !(bool)(value ?? false);
        if (param == "eye_logic") return (bool)value! ? "EyeOffOutline" : "EyeOutline";

        // Style logic
        if (param == "radius_logic") return (bool)value! ? new CornerRadius(8, 0, 0, 0) : new CornerRadius(0);
        if (param == "thickness_logic") return (bool)value! ? new Thickness(1, 1, 0, 0) : new Thickness(0);
        if (param == "shadow_logic") return (bool)value! ? BoxShadows.Parse("-2 0 12 0 #08000000") : new BoxShadows();

        // Formatting logic: param="format:l.Key"
        if (param != null && param.StartsWith("format:"))
        {
            string formatKey = param.Substring(7);
            if (Application.Current != null && Application.Current.Resources.TryGetResource(formatKey, ThemeVariant.Default, out var formatObj) && formatObj is string formatStr)
            {
                try
                {
                    if (value is AnimeEntity ai)
                        return string.Format(formatStr, ai.EpisodesAired, ai.TotalEpisodes);
                    return string.Format(formatStr, value);
                }
                catch { return value?.ToString(); }
            }
            return value?.ToString();
        }

        if (value is System.Collections.IEnumerable list && !(value is string))
        {
            var results = new System.Collections.Generic.List<string>();
            foreach (var item in list)
            {
                var translated = Convert(item, typeof(string), parameter, culture);
                if (translated != null) results.Add(translated.ToString()!);
            }
            return string.Join(", ", results);
        }

        if (value is string s || (value is Enum && (s = value.ToString()!) != null))
        {
            // Handle prefix if parameter is provided
            string keyToUse = (param != null && param != "Adult") ? s : (param == "Adult" ? $"Adult{s}" : s);

            return Kiriha.Localization.LocalizationStore.Translate(keyToUse);
        }

        if (value is Avalonia.Styling.ThemeVariant t)
        {
            string key = $"Theme{t.Key}";
            if (Application.Current != null && Application.Current.Resources.TryGetValue($"l.{key}", out var translated))
            {
                return translated;
            }
            return key;
        }

        return value?.ToString();
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value;
    }


}


