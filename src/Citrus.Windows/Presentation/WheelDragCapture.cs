using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace Citrus.Windows.Presentation;

/// <summary>A scalar-alpha native OLE shield; it has no desktop-sized WPF bitmap.</summary>
internal sealed class WheelDragCapture
{
    private readonly Dispatcher dispatcher;
    private readonly CaptureForm form;
    private readonly WheelDragCapturePlatform platform;
    private Binding? binding;
    private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool closing, failed, retired, retirementQueued, disposalStarted, handleIdentityChanged;
    private int callbackDepth;
    private nint handle;
    private Task exclusionReleased = Task.CompletedTask;
    internal Window Owner { get; }
    internal nint Handle => handle;
    internal bool IsVisible => !form.IsDisposed && form.Visible;
    internal bool AllowDrop => !form.IsDisposed && form.AllowDrop;
    internal Task Completion => completion.Task;
    internal event Action? PlacementFailed;

    internal WheelDragCapture(Window owner, Action<WheelDragInput, bool> hover,
        Action<WheelDragInput> drop, Action<WheelDragInput> leave)
        : this(owner, hover, drop, leave, WheelDragCapturePlatform.Default) { }

    internal WheelDragCapture(Window owner, Action<WheelDragInput, bool> hover,
        Action<WheelDragInput> drop, Action<WheelDragInput> leave, WheelDragCapturePlatform platform)
        : this(owner, platform)
    {
        if (!TryBind(hover, drop, leave))
        {
            Release(Task.CompletedTask);
            throw new InvalidOperationException("The drag target no longer owns its prepared native handle.");
        }
    }

    // The hidden candidate owns no generation or payload until physical-file admission.
    internal WheelDragCapture(Window owner, WheelDragCapturePlatform platform)
    {
        Owner = owner; dispatcher = owner.Dispatcher;
        dispatcher.VerifyAccess();
        this.platform = platform;
        form = new CaptureForm(this);
        try
        {
            form.Configure();
            form.HandleDestroyed += (_, _) => handleIdentityChanged = true;
            form.DragEnter += (_, args) => Dispatch(WheelDragEventKind.Enter, WheelDragInput.FromForms(args, dispatcher));
            form.DragOver += (_, args) => Dispatch(WheelDragEventKind.Over, WheelDragInput.FromForms(args, dispatcher));
            form.DragDrop += (_, args) => Dispatch(WheelDragEventKind.Drop, WheelDragInput.FromForms(args, dispatcher));
            form.DragLeave += (_, _) => Dispatch(WheelDragEventKind.Leave, WheelDragInput.Leave(dispatcher));
            // Create/register while hidden, before the renderer acquires capture exclusion.
            handle = platform.CreateHandle(form);
        }
        catch
        {
            // No exclusion lease can own this unpublished HWND yet.
            form.Dispose();
            throw;
        }
    }

    internal bool TryBind(Action<WheelDragInput, bool> hover, Action<WheelDragInput> drop, Action<WheelDragInput> leave)
    {
        dispatcher.VerifyAccess();
        if (binding is not null || closing || failed || retired || !OwnsPreparedHandle) return false;
        binding = new(hover, drop, leave);
        return true;
    }

    private bool OwnsPreparedHandle => !handleIdentityChanged && !form.IsDisposed &&
        form.IsHandleCreated && handle != 0 && form.Handle == handle;

    internal void PrepareHiddenBounds()
    {
        dispatcher.VerifyAccess();
        if (binding is not null || closing || failed || retired || form.Visible || !OwnsPreparedHandle)
            throw new InvalidOperationException("Only an unadmitted hidden drag target can prepare its desktop bounds.");
        // SWP_NOACTIVATE does not show the window. Start still corrects current
        // physical desktop bounds and z-order both before and after Show.
        platform.Place(handle);
    }

    internal void Start()
    {
        dispatcher.VerifyAccess();
        if (closing || failed) return;
        if (binding is null) throw new InvalidOperationException("The drag target has not been admitted.");
        try
        {
            if (!OwnsPreparedHandle) throw new InvalidOperationException("The drag target native handle changed before admission.");
            CoverDesktop();
            form.Show(new NativeOwner(new WindowInteropHelper(Owner).Handle));
            CoverDesktop();
        }
        catch
        {
            // Fail closed before returning to the admission callback. The caller
            // stops glass and supplies its real exclusion lifetime through Release.
            failed = true;
            DisableInput();
            throw;
        }
    }

    internal void Dispatch(WheelDragEventKind kind, WheelDragInput input)
    {
        dispatcher.VerifyAccess();
        callbackDepth++;
        try
        {
            if (closing || failed || binding is not { } admitted)
            { input.Handled = true; input.Effects = DragDropEffects.None; return; }
            switch (kind)
            {
                case WheelDragEventKind.Enter: admitted.Hover(input, true); break;
                case WheelDragEventKind.Over: admitted.Hover(input, false); break;
                case WheelDragEventKind.Drop: admitted.Drop(input); break;
                case WheelDragEventKind.Leave: admitted.Leave(input); break;
            }
        }
        finally
        {
            input.Complete();
            if (--callbackDepth == 0 && (closing || failed)) QueueRetirement();
        }
    }

    internal void Release(Task exclusionReleased)
    {
        dispatcher.VerifyAccess();
        if (closing) return;
        closing = true;
        this.exclusionReleased = exclusionReleased;
        DisableInput();
    }

    private void DisableInput()
    {
        form.Hide();
        // Closing the WPF owner must not destroy a retired affinity-bearing HWND.
        if (form.IsHandleCreated) NativeMethods.SetWindowLongPtr(form.Handle, -8, 0); // GWLP_HWNDPARENT, top-level owner.
        if (callbackDepth == 0) RetireAfterCallback();
        else QueueRetirement();
    }

    private void QueueRetirement()
    {
        if (retirementQueued || disposalStarted || retired && !closing || dispatcher.HasShutdownStarted) return;
        retirementQueued = true;
        dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            retirementQueued = false;
            RetireAfterCallback();
        }));
    }

    private void RetireAfterCallback()
    {
        if (callbackDepth != 0) { QueueRetirement(); return; }
        // Revoke after the current COM callback unwinds. Closed callbacks already
        // reject without reading their data; operation callbacks run at Background.
        if (!retired) { form.AllowDrop = false; retired = true; }
        if (closing && !disposalStarted)
        {
            disposalStarted = true;
            _ = DisposeAfterExclusion();
        }
    }

    private async Task DisposeAfterExclusion()
    {
        Exception? failure = null;
        try { await exclusionReleased.ConfigureAwait(false); }
        catch (Exception error) { failure = error; }
        try
        {
            void DisposeNative()
            {
                try { form.Dispose(); }
                finally { handle = 0; }
            }
            if (dispatcher.CheckAccess()) DisposeNative();
            else await dispatcher.InvokeAsync(DisposeNative, DispatcherPriority.Input);
            if (failure is null) completion.TrySetResult();
            else completion.TrySetException(failure);
        }
        catch (Exception error) { completion.TrySetException(error); }
    }

    private void CoverDesktop()
    {
        if (closing || failed || handle == 0) return;
        platform.Place(handle);
    }

    private void RefreshDesktop()
    {
        if (closing || failed) return;
        try { CoverDesktop(); }
        catch (Exception error) when (error is ExternalException or InvalidOperationException)
        {
            failed = true;
            DisableInput();
            PlacementFailed?.Invoke();
        }
    }

    private sealed class NativeOwner(nint handle) : Forms.IWin32Window { public nint Handle => handle; }

    private sealed record Binding(Action<WheelDragInput, bool> Hover, Action<WheelDragInput> Drop, Action<WheelDragInput> Leave);

    private sealed class CaptureForm : Forms.Form
    {
        private readonly WheelDragCapture owner;
        internal CaptureForm(WheelDragCapture owner) => this.owner = owner;
        internal void Configure()
        {
            Text = "Citrus drag capture";
            FormBorderStyle = Forms.FormBorderStyle.None;
            // Native TOOLWINDOW/APPWINDOW flags keep this out of the taskbar.
            // ShowInTaskbar=false would allocate an extra per-form owner HWND.
            StartPosition = Forms.FormStartPosition.Manual;
            AutoScaleMode = Forms.AutoScaleMode.None;
            BackColor = System.Drawing.Color.Black;
            Opacity = 1d / 255;
            TopMost = true; AllowDrop = true;
            ClientSize = new System.Drawing.Size(1, 1);
        }
        protected override bool ShowWithoutActivation => true;
        protected override Forms.CreateParams CreateParams
        {
            get
            {
                var value = base.CreateParams;
                value.ExStyle &= ~0x00040000; // No APPWINDOW taskbar entry or managed helper owner.
                value.ExStyle |= 0x08000000 | 0x00000080; // NOACTIVATE | TOOLWINDOW.
                return value;
            }
        }
        protected override void WndProc(ref Forms.Message message)
        {
            if (message.Msg == 0x21) { message.Result = 3; return; } // MA_NOACTIVATE.
            base.WndProc(ref message);
            if ((message.Msg is 0x02E0 or 0x007E) && !owner.closing && !owner.failed && Visible)
                owner.dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(owner.RefreshDesktop));
        }
    }
}

/// <summary>The two native placement operations, scoped to one target admission.</summary>
internal sealed class WheelDragCapturePlatform(Func<Forms.Form, nint> createHandle, Action<nint> place)
{
    internal Func<Forms.Form, nint> CreateHandle { get; } = createHandle;
    internal Action<nint> Place { get; } = place;
    internal static WheelDragCapturePlatform Default { get; } = new(form => form.Handle, handle =>
    {
        if (!NativeMethods.SetWindowPos(handle, new nint(-1), NativeMethods.GetSystemMetrics(76), NativeMethods.GetSystemMetrics(77),
            Math.Max(1, NativeMethods.GetSystemMetrics(78)), Math.Max(1, NativeMethods.GetSystemMetrics(79)), 0x10))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    });
}
