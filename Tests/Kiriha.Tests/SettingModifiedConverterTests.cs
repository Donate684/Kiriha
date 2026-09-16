using System.Globalization;
using Kiriha.Mpv.UI.ViewModels.Player.Settings;
using Kiriha.Mpv.UI.Views.Converters;
using Xunit;

namespace Kiriha.Tests;

public class SettingModifiedConverterTests
{
    private readonly SettingModifiedConverter _converter = new();

    [Fact]
    public void Convert_Booleans_ComparesCorrectly()
    {
        // Equal to default
        Assert.False((bool)_converter.Convert(true, typeof(bool), "True", CultureInfo.InvariantCulture)!);
        Assert.False((bool)_converter.Convert(false, typeof(bool), "False", CultureInfo.InvariantCulture)!);

        // Modified from default
        Assert.True((bool)_converter.Convert(false, typeof(bool), "True", CultureInfo.InvariantCulture)!);
        Assert.True((bool)_converter.Convert(true, typeof(bool), "False", CultureInfo.InvariantCulture)!);
    }

    [Fact]
    public void Convert_Numbers_ComparesWithTolerance()
    {
        // Equal to default
        Assert.False((bool)_converter.Convert(1.0, typeof(bool), "1.0", CultureInfo.InvariantCulture)!);
        Assert.False((bool)_converter.Convert(1.5, typeof(bool), "1.5", CultureInfo.InvariantCulture)!);
        Assert.False((bool)_converter.Convert(80, typeof(bool), "80", CultureInfo.InvariantCulture)!);
        Assert.False((bool)_converter.Convert(4, typeof(bool), "4", CultureInfo.InvariantCulture)!);

        // Modified from default
        Assert.True((bool)_converter.Convert(1.2, typeof(bool), "1.0", CultureInfo.InvariantCulture)!);
        Assert.True((bool)_converter.Convert(2.0, typeof(bool), "1.5", CultureInfo.InvariantCulture)!);
        Assert.True((bool)_converter.Convert(100, typeof(bool), "80", CultureInfo.InvariantCulture)!);
        Assert.True((bool)_converter.Convert(9, typeof(bool), "4", CultureInfo.InvariantCulture)!);
    }

    [Fact]
    public void Convert_Strings_ComparesCaseInsensitively()
    {
        // Equal to default
        Assert.False((bool)_converter.Convert("png", typeof(bool), "png", CultureInfo.InvariantCulture)!);
        Assert.False((bool)_converter.Convert("PNG", typeof(bool), "png", CultureInfo.InvariantCulture)!);
        Assert.False((bool)_converter.Convert("Lato ExtraBold", typeof(bool), "Lato ExtraBold", CultureInfo.InvariantCulture)!);
        Assert.False((bool)_converter.Convert("#FFFFFF", typeof(bool), "#FFFFFF", CultureInfo.InvariantCulture)!);
        Assert.False((bool)_converter.Convert("", typeof(bool), "", CultureInfo.InvariantCulture)!);
        Assert.False((bool)_converter.Convert(null, typeof(bool), "", CultureInfo.InvariantCulture)!);

        // Modified from default
        Assert.True((bool)_converter.Convert("webp", typeof(bool), "png", CultureInfo.InvariantCulture)!);
        Assert.True((bool)_converter.Convert("Arial", typeof(bool), "Lato ExtraBold", CultureInfo.InvariantCulture)!);
        Assert.True((bool)_converter.Convert("#FF0000", typeof(bool), "#FFFFFF", CultureInfo.InvariantCulture)!);
        Assert.True((bool)_converter.Convert("C:\\Screenshots", typeof(bool), "", CultureInfo.InvariantCulture)!);
    }

    [Fact]
    public void Convert_OptionObjectsWithProperties_ExtractsValueProperly()
    {
        var defaultRes = new ScreenshotResolutionOption("Video", "video");
        var windowRes = new ScreenshotResolutionOption("Window", "window");

        Assert.False((bool)_converter.Convert(defaultRes, typeof(bool), "video", CultureInfo.InvariantCulture)!);
        Assert.True((bool)_converter.Convert(windowRes, typeof(bool), "video", CultureInfo.InvariantCulture)!);
    }
}
