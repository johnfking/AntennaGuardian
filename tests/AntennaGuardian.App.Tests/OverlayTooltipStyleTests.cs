using System.Xml.Linq;

namespace AntennaGuardian.App.Tests;

public sealed class OverlayTooltipStyleTests
{
    private static readonly XNamespace Presentation =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml =
        "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void IconButtonTooltipsUseReadableTypography()
    {
        var app = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Xaml", "App.xaml"));
        var mainWindow = XDocument.Load(
            Path.Combine(AppContext.BaseDirectory, "Xaml", "MainWindow.xaml"));

        var tooltipStyle = app.Descendants(Presentation + "Style")
            .SingleOrDefault(style => (string?)style.Attribute("TargetType") == "ToolTip");
        Assert.NotNull(tooltipStyle);

        var tooltipFont = tooltipStyle!.Elements(Presentation + "Setter")
            .SingleOrDefault(setter => (string?)setter.Attribute("Property") == "FontFamily")?
            .Attribute("Value")?.Value;
        Assert.False(string.IsNullOrWhiteSpace(tooltipFont));
        Assert.NotEqual("Segoe Fluent Icons", tooltipFont);

        var iconButtonTooltips = mainWindow.Descendants(Presentation + "Button")
            .Where(button => (string?)button.Attribute("Style") == "{StaticResource IconButton}")
            .Select(button => (string?)button.Attribute("ToolTip") ?? string.Empty)
            .ToArray();
        Assert.Equal(["Settings", "Hide overlay"], iconButtonTooltips);
    }
}
