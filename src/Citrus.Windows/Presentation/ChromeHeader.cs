using System.Windows.Controls;
using System.Windows.Input;

namespace Citrus.Windows.Presentation;

/// <summary>Shared centered title and close affordance; title dragging never captures a child control.</summary>
public sealed class ChromeHeader : ContentControl
{
    private Button? close;
    public ChromeHeader() => SetResourceReference(StyleProperty, typeof(ChromeHeader));
    protected override void OnContentChanged(object oldContent, object newContent)
    {
        base.OnContentChanged(oldContent, newContent);
        ApplyTitleTypography();
    }
    private void ApplyTitleTypography()
    {
        if (Content is TextBlock title)
        {
            title.SetResourceReference(TextBlock.FontSizeProperty, "EditorTitleSize");
            title.FontWeight = FontWeights.SemiBold;
            title.TextAlignment = TextAlignment.Center;
        }
    }
    public override void OnApplyTemplate()
    {
        if (close is not null) close.Click -= CloseWindow;
        base.OnApplyTemplate();
        // BAML can apply a legacy title's local properties after assigning Content.
        // Normalize again once initialization is complete, retaining dynamic text scaling.
        ApplyTitleTypography();
        close = GetTemplateChild("PART_Close") as Button;
        if (close is not null) close.Click += CloseWindow;
    }
    private void CloseWindow(object sender, RoutedEventArgs e) => Window.GetWindow(this)?.Close();
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (e.Handled || Window.GetWindow(this) is not { } window) return;
        if (e.ClickCount == 2 && window.ResizeMode == ResizeMode.CanResize)
            window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        else if (e.ButtonState == MouseButtonState.Pressed) window.DragMove();
        e.Handled = true;
    }
}
