using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace AntennaGuardian.App.Tests;

public sealed class OverlayMouseTests
{
    [Fact]
    public void ClickingOverlayTextDoesNotThrow()
    {
        RunOnSta(() =>
        {
            var status = new Run("OFFLINE");
            var identity = new Run("  Shack FLEX-6600");
            var text = new TextBlock();
            text.Inlines.Add(status);
            text.Inlines.Add(identity);
            var overlay = new Border { Child = text };

            Assert.False(MainWindow.IsInsideButton(status));
            Assert.False(MainWindow.IsInsideButton(identity));
            Assert.False(MainWindow.IsInsideButton(text));
            Assert.False(MainWindow.IsInsideButton(overlay));
        });
    }

    [Fact]
    public void ClickingNestedButtonTextIsRecognizedAsButton()
    {
        RunOnSta(() =>
        {
            var run = new Run("Settings");
            var span = new Span(run);
            var text = new TextBlock(span);
            var button = new Button { Content = text };
            button.ApplyTemplate();
            button.Measure(new Size(100, 40));
            button.Arrange(new Rect(0, 0, 100, 40));

            Assert.True(MainWindow.IsInsideButton(run));
            Assert.True(MainWindow.IsInsideButton(text));
            Assert.True(MainWindow.IsInsideButton(button));
        });
    }

    [Fact]
    public void DetachedSourcesAreNotButtons()
    {
        RunOnSta(() =>
        {
            Assert.False(MainWindow.IsInsideButton(null));
            Assert.False(MainWindow.IsInsideButton(new Run("Detached")));
            Assert.False(MainWindow.IsInsideButton(new DependencyObject()));
        });
    }

    private static void RunOnSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { error = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "WPF test timed out.");
        if (error is not null)
        {
            ExceptionDispatchInfo.Capture(error).Throw();
        }
    }
}
