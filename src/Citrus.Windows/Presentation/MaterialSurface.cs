using System.Windows.Controls;
using System.Windows.Media;

namespace Citrus.Windows.Presentation;

internal enum MaterialRole { Chrome, WheelShell, Toast }

/// <summary>Original, static material edges. Native eligibility is supplied by this surface's HWND controller.</summary>
internal sealed class MaterialSurface : Decorator
{
    public static readonly DependencyProperty RoleProperty = DependencyProperty.Register(nameof(Role), typeof(MaterialRole), typeof(MaterialSurface), new FrameworkPropertyMetadata(MaterialRole.Chrome, FrameworkPropertyMetadataOptions.AffectsRender, RoleChanged));
    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(nameof(State), typeof(BackdropState), typeof(MaterialSurface), new FrameworkPropertyMetadata(BackdropState.Solid, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(nameof(CornerRadius), typeof(CornerRadius), typeof(MaterialSurface), new FrameworkPropertyMetadata(new CornerRadius(24), FrameworkPropertyMetadataOptions.AffectsRender));
    private static DependencyProperty BrushProperty(string name) => DependencyProperty.Register(name, typeof(Brush), typeof(MaterialSurface), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));
    private static readonly DependencyProperty TintProperty = BrushProperty("Tint"), SolidProperty = BrushProperty("Solid"), OutlineProperty = BrushProperty("Outline"), SolidOutlineProperty = BrushProperty("SolidOutline"), SpecularProperty = BrushProperty("Specular"), InnerProperty = BrushProperty("Inner");
    public MaterialRole Role { get => (MaterialRole)GetValue(RoleProperty); set => SetValue(RoleProperty, value); }
    public BackdropState State { get => (BackdropState)GetValue(StateProperty); set => SetValue(StateProperty, value); }
    public CornerRadius CornerRadius { get => (CornerRadius)GetValue(CornerRadiusProperty); set => SetValue(CornerRadiusProperty, value); }
    internal Brush EffectiveTint => (Brush)GetValue(State == BackdropState.ConfiguredNativeAcrylic ? TintProperty : SolidProperty);
    public MaterialSurface()
    {
        SetResourceReference(SolidProperty, "WindowBackground");
        SetResourceReference(SolidOutlineProperty, "BorderBrush");
        SetResourceReference(OutlineProperty, "MaterialOutline");
        SetResourceReference(SpecularProperty, "MaterialDepth");
        SetResourceReference(InnerProperty, "MaterialInnerEdge");
        UpdateRole();
    }
    private static void RoleChanged(DependencyObject owner, DependencyPropertyChangedEventArgs args) => ((MaterialSurface)owner).UpdateRole();
    private void UpdateRole() => SetResourceReference(TintProperty, Role == MaterialRole.WheelShell ? "MaterialShellTint" : "MaterialChromeTint");
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (ActualWidth <= 2 || ActualHeight <= 2) return;
        bool native = State == BackdropState.ConfiguredNativeAcrylic;
        Draw(dc, EffectiveTint, new Pen((Brush)GetValue(native ? OutlineProperty : SolidOutlineProperty), 1), .5);
        // Static edge lighting also belongs to the honest opaque fallback.
        if (SystemParameters.HighContrast) return;
        Draw(dc, null, new Pen((Brush)GetValue(SpecularProperty), 1), 1.5);
        Draw(dc, null, new Pen((Brush)GetValue(InnerProperty), .5), 2.5);
    }
    private void Draw(DrawingContext dc, Brush? fill, Pen pen, double inset)
    {
        var rect = new Rect(inset, inset, Math.Max(0, ActualWidth - inset * 2), Math.Max(0, ActualHeight - inset * 2));
        if (Role == MaterialRole.WheelShell) dc.DrawEllipse(fill, pen, new Point(ActualWidth / 2, ActualHeight / 2), rect.Width / 2, rect.Height / 2);
        else dc.DrawRoundedRectangle(fill, pen, rect, CornerRadius.TopLeft, CornerRadius.TopLeft);
    }
}
