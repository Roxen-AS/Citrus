using System.Windows.Threading;
using Forms = System.Windows.Forms;
using WpfDragEventArgs = System.Windows.DragEventArgs;

namespace Citrus.Windows.Presentation;

/// <summary>One synchronous STA callback; never retain it or its data reader in worker work.</summary>
internal sealed class WheelDragInput
{
    private readonly Dispatcher dispatcher;
    private readonly WpfDragEventArgs? wpf;
    private readonly Forms.DragEventArgs? forms;
    private readonly FrameworkElement? surface;
    internal Point? SurfacePosition { get; }
    internal Point ScreenPosition
    {
        get
        {
            dispatcher.VerifyAccess();
            return forms is not null ? new Point(forms.X, forms.Y) :
                surface is not null && SurfacePosition is { } point ? surface.PointToScreen(point) : default;
        }
    }
    internal DragDropEffects AllowedEffects { get; }
    internal DragDropKeyStates KeyStates { get; }
    internal DragDropEffects Effects { get; set; }
    internal bool Handled { get; set; } = true;

    private WheelDragInput(Dispatcher dispatcher, WpfDragEventArgs? wpf = null,
        Forms.DragEventArgs? forms = null, FrameworkElement? surface = null, Point? local = null)
    {
        this.dispatcher = dispatcher; this.wpf = wpf; this.forms = forms; this.surface = surface;
        SurfacePosition = local;
        AllowedEffects = wpf?.AllowedEffects ?? (DragDropEffects)(int)(forms?.AllowedEffect ?? Forms.DragDropEffects.None);
        KeyStates = wpf?.KeyStates ?? (DragDropKeyStates)(forms?.KeyState ?? 0);
    }

    internal static WheelDragInput FromWpf(WpfDragEventArgs args, FrameworkElement surface, bool includePosition = true)
    {
        surface.Dispatcher.VerifyAccess();
        return new(surface.Dispatcher, wpf: args, surface: surface,
            local: includePosition ? args.GetPosition(surface) : null);
    }

    internal static WheelDragInput FromForms(Forms.DragEventArgs args, Dispatcher dispatcher)
    {
        dispatcher.VerifyAccess();
        return new(dispatcher, forms: args);
    }

    internal static WheelDragInput Leave(Dispatcher dispatcher)
        => new(dispatcher);

    internal string[]? ReadFiles()
    {
        dispatcher.VerifyAccess();
        if (wpf is not null)
            return wpf.Data.GetDataPresent(DataFormats.FileDrop, false)
                ? wpf.Data.GetData(DataFormats.FileDrop, false) as string[] : null;
        return forms?.Data?.GetDataPresent(Forms.DataFormats.FileDrop, false) == true
            ? forms.Data.GetData(Forms.DataFormats.FileDrop, false) as string[] : null;
    }
    internal void Complete()
    {
        dispatcher.VerifyAccess();
        if (wpf is not null) { wpf.Handled = Handled; wpf.Effects = Effects; }
        if (forms is not null) forms.Effect = (Forms.DragDropEffects)(int)Effects;
    }
}

internal enum WheelDragEventKind { Enter, Over, Drop, Leave }
