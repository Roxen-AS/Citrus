using System.Windows.Interop;
using System.Windows.Media;
using Citrus.Core;

namespace Citrus.Windows.Presentation;

internal readonly record struct CompactWindowSize(double Width,double Height,double MinWidth,double MinHeight);
internal readonly record struct WindowMonitorContext(ScreenRect WorkArea,double Scale);

/// <summary>Initial presentation leaves desktop space; later user sizing is never constrained.</summary>
internal static class CompactWindowSizing
{
    internal const double WidthFraction=.84,HeightFraction=.86;

    internal static CompactWindowSize Fit(double preferredWidth,double preferredHeight,double minWidth,double minHeight,double workWidth,double workHeight)
    {
        if(!double.IsFinite(workWidth)||workWidth<=0)throw new ArgumentOutOfRangeException(nameof(workWidth));
        if(!double.IsFinite(workHeight)||workHeight<=0)throw new ArgumentOutOfRangeException(nameof(workHeight));
        if(!double.IsFinite(minWidth)||minWidth<0)throw new ArgumentOutOfRangeException(nameof(minWidth));
        if(!double.IsFinite(minHeight)||minHeight<0)throw new ArgumentOutOfRangeException(nameof(minHeight));
        double budgetWidth=workWidth*WidthFraction,budgetHeight=workHeight*HeightFraction;
        minWidth=Math.Min(minWidth,budgetWidth);minHeight=Math.Min(minHeight,budgetHeight);
        double width=double.IsFinite(preferredWidth)&&preferredWidth>0?preferredWidth:budgetWidth;
        double height=double.IsFinite(preferredHeight)&&preferredHeight>0?preferredHeight:budgetHeight;
        return new(Math.Clamp(width,minWidth,budgetWidth),Math.Clamp(height,minHeight,budgetHeight),minWidth,minHeight);
    }

    internal static bool TryGetMonitor(Window window,out WindowMonitorContext context)
    {
        context=default;
        if(!NativeMethods.GetCursorPos(out var pointer))return false;
        // Owned editors follow their workspace, even when the pointer has moved to another monitor.
        if(window.Owner is {} owner&&new WindowInteropHelper(owner).Handle is var ownerHandle&&ownerHandle!=0&&
            BackdropNativeMethods.GetWindowRect(ownerHandle,out var ownerBounds))
            pointer=new NativeMethods.Point {X=ownerBounds.Left+(ownerBounds.Right-ownerBounds.Left)/2,Y=ownerBounds.Top+(ownerBounds.Bottom-ownerBounds.Top)/2};
        var work=NativeMethods.WorkArea(pointer);
        double scale=NativeMethods.MonitorScale(pointer);
        double visualScale=VisualTreeHelper.GetDpi(window).DpiScaleX;
        double nativeScale=Math.Max(96,NativeMethods.GetDpiForWindow(new WindowInteropHelper(window).Handle))/96d;
        // Test/embedding hosts can be system-DPI aware while WPF uses a different transform.
        if(Math.Abs(visualScale-nativeScale)>.001)scale=visualScale;
        context=new(work,scale);return true;
    }

    internal static void ApplyInitial(Window window)
    {
        if(!TryGetMonitor(window,out var monitor))return;
        var size=Fit(window.Width,window.Height,window.MinWidth,window.MinHeight,
            monitor.WorkArea.Width/monitor.Scale,monitor.WorkArea.Height/monitor.Scale);
        window.MinWidth=size.MinWidth;window.MinHeight=size.MinHeight;
        if(double.IsFinite(window.Width))window.Width=size.Width;
        if(double.IsFinite(window.Height))window.Height=size.Height;
    }
}
