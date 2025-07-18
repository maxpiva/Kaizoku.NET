using System.ComponentModel;
using System.Globalization;
using System.Text.RegularExpressions;

namespace KaizokuBackend.Services.Import.KavitaParser;
#nullable enable
public static class EnumExtensions
{
    private static readonly Regex Regex = new Regex(@"\d+", RegexOptions.Compiled, TimeSpan.FromMilliseconds(500));


    public static float AsFloat(this string? value, float defaultValue = 0.0f)
    {
        return string.IsNullOrEmpty(value) ? defaultValue : float.Parse(value, CultureInfo.InvariantCulture);
    }

    public static string ToDescription<TEnum>(this TEnum value) where TEnum : struct
    {
        var fi = value.GetType().GetField(value.ToString() ?? string.Empty);

        if (fi == null)
        {
            return value.ToString() ?? "";
        }

        var attributes = (DescriptionAttribute[])fi.GetCustomAttributes(typeof(DescriptionAttribute), false);

        return attributes is { Length: > 0 } ? attributes[0].Description ?? "" : value.ToString() ?? "";
    }


}