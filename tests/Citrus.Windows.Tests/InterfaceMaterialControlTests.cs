using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBox = System.Windows.Controls.TextBox;
using Orientation = System.Windows.Controls.Orientation;
using CheckBox = System.Windows.Controls.CheckBox;
using ScrollBar = System.Windows.Controls.Primitives.ScrollBar;
using Control = System.Windows.Controls.Control;
using SystemColors = System.Windows.SystemColors;
using Application = System.Windows.Application;

namespace Citrus.Windows.Tests;

[Collection("Desktop")]
public sealed class InterfaceMaterialControlTests(DesktopFixture desktop)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Checkbox_retains_toggle_semantics_at_large_text_and_high_contrast(bool highContrast) => desktop.RunAsync(async () =>
    {
        using var theme = new ThemeManager(Application.Current);
        var check = new CheckBox { Content = "Include this file", IsThreeState = true };
        check.SetResourceReference(Control.FontSizeProperty, "BodySize");
        var window = new Window { Content = check, Width = 420, Height = 140, ShowActivated = false };
        try
        {
            theme.ApplyPalette(ThemeChoice.Light, true, highContrast);
            theme.ApplyTypography(2);
            window.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var peer = UIElementAutomationPeer.CreatePeerForElement(check)!;
            var toggle = Assert.IsAssignableFrom<IToggleProvider>(peer.GetPattern(PatternInterface.Toggle));
            toggle.Toggle(); Assert.True(check.IsChecked);
            toggle.Toggle(); Assert.Null(check.IsChecked);
            toggle.Toggle(); Assert.False(check.IsChecked);
            Assert.True(check.ActualWidth >= check.DesiredSize.Width);
            Assert.True(check.ActualHeight >= check.DesiredSize.Height);
            Assert.NotNull(check.FocusVisualStyle);
            if (highContrast)
            {
                var box = (Border)check.Template.FindName("Box", check);
                Assert.Same(SystemColors.WindowBrush, box.Background);
                toggle.Toggle();
                Assert.Same(SystemColors.HighlightBrush, box.Background);
                var glyph = (System.Windows.Shapes.Path)check.Template.FindName("Check", check);
                Assert.Same(SystemColors.HighlightTextBrush, glyph.Stroke);
                Assert.Same(SystemColors.WindowBrush, Application.Current.FindResource("WheelHubBackground"));
                Assert.Same(SystemColors.WindowTextBrush, Application.Current.FindResource("PetalRim"));
            }
            bool? before = check.IsChecked;
            check.IsEnabled = false;
            Assert.Throws<System.Windows.Automation.ElementNotEnabledException>(() => toggle.Toggle());
            Assert.Equal(before, check.IsChecked);
        }
        finally { window.Close(); theme.ApplyTypography(1); theme.Apply(ThemeChoice.Light, true); }
    });

    [Theory]
    [InlineData(Orientation.Vertical)]
    [InlineData(Orientation.Horizontal)]
    public Task Scrollbar_keeps_line_page_and_thumb_interaction(Orientation orientation) => desktop.RunAsync(async () =>
    {
        var bar = new ScrollBar { Orientation = orientation, Minimum = 0, Maximum = 100, Value = 30, ViewportSize = 20, SmallChange = 2, LargeChange = 10 };
        var window = new Window { Content = bar, Width = orientation == Orientation.Vertical ? 100 : 420,
            Height = orientation == Orientation.Vertical ? 420 : 100, ShowActivated = false };
        try
        {
            window.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var track = (Track)bar.Template.FindName("PART_Track", bar);
            var increase = (RepeatButton)bar.Template.FindName("Increase", bar);
            var decrease = (RepeatButton)bar.Template.FindName("Decrease", bar);
            Execute(increase); Assert.Equal(32, bar.Value);
            Execute(decrease); Assert.Equal(30, bar.Value);
            Execute(track.IncreaseRepeatButton); Assert.Equal(40, bar.Value);
            Execute(track.DecreaseRepeatButton); Assert.Equal(30, bar.Value);
            bar.UpdateLayout();
            track.Thumb.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
            track.Thumb.RaiseEvent(new DragDeltaEventArgs(orientation == Orientation.Horizontal ? 20 : 0, orientation == Orientation.Vertical ? 20 : 0) { RoutedEvent = Thumb.DragDeltaEvent });
            track.Thumb.RaiseEvent(new DragCompletedEventArgs(20, 20, false) { RoutedEvent = Thumb.DragCompletedEvent });
            Assert.True(bar.Value > 30, "Dragging the thumb forward must increase the scroll value in either orientation.");
            Assert.InRange(bar.Value, bar.Minimum, bar.Maximum);
            Assert.Equal(orientation == Orientation.Vertical ? SystemParameters.VerticalScrollBarWidth : SystemParameters.HorizontalScrollBarHeight,
                orientation == Orientation.Vertical ? bar.ActualWidth : bar.ActualHeight);
            bar.IsEnabled = false;
            Assert.False(track.Thumb.IsEnabled);
            Assert.False(increase.IsEnabled);
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public Task Dark_combo_preserves_expand_select_and_edit_with_readable_paint(bool editable, bool highContrast) => desktop.RunAsync(async () =>
    {
        using var theme = new ThemeManager(Application.Current);
        var combo = new ComboBox { ItemsSource = new[] { "Annotate", "Crop" }, SelectedIndex = 0, IsEditable = editable };
        combo.SetResourceReference(Control.FontSizeProperty, "BodySize");
        var window = new Window { Content = combo, Width = 420, SizeToContent = SizeToContent.Height, ShowActivated = false };
        try
        {
            theme.ApplyPalette(ThemeChoice.Dark, true, highContrast);
            theme.ApplyTypography(2);
            window.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var frame = (Border)combo.Template.FindName("Frame", combo);
            Assert.Same(Application.Current.FindResource("ControlBackground"), frame.Background);
            Assert.Same(Application.Current.FindResource("ControlText"), combo.Foreground);
            var peer = UIElementAutomationPeer.CreatePeerForElement(combo)!;
            var expand = Assert.IsAssignableFrom<IExpandCollapseProvider>(peer.GetPattern(PatternInterface.ExpandCollapse));
            expand.Expand();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var popup = (Popup)combo.Template.FindName("PART_Popup", combo);
            Assert.True(popup.IsOpen);
            Assert.Same(Application.Current.FindResource("ControlBackground"), ((Border)popup.Child).Background);
            var item = (ComboBoxItem)combo.ItemContainerGenerator.ContainerFromIndex(1);
            var itemPeer = Assert.Single(peer.GetChildren(), child => child.GetName() == "Crop");
            Assert.IsAssignableFrom<ISelectionItemProvider>(itemPeer.GetPattern(PatternInterface.SelectionItem)).Select();
            Assert.Equal("Crop", combo.SelectedItem);
            Assert.Same(Application.Current.FindResource("AccentText"), item.Foreground);
            expand.Collapse(); Assert.False(popup.IsOpen);
            if (editable)
            {
                var editor = (TextBox)combo.Template.FindName("PART_EditableTextBox", combo);
                Assert.Equal(Visibility.Visible, editor.Visibility);
                Assert.Same(combo.Foreground, editor.Foreground);
                editor.Text = "Custom";
                Assert.Equal("Custom", combo.Text);
            }
            combo.IsEnabled = false;
            Assert.Throws<System.Windows.Automation.ElementNotEnabledException>(() => expand.Expand());
        }
        finally { window.Close(); theme.ApplyTypography(1); theme.Apply(ThemeChoice.Light, true); }
    });

    private static void Execute(RepeatButton button)
    {
        var command = Assert.IsAssignableFrom<RoutedCommand>(button.Command);
        Assert.True(command.CanExecute(button.CommandParameter, button.CommandTarget));
        command.Execute(button.CommandParameter, button.CommandTarget);
    }
}
