using System.Runtime.InteropServices;

namespace Citrus.Windows;

/// <summary>Owned STA Shell move worker, including native cross-volume folder handling.</summary>
public static class ShellFileMover
{
    // Silent, rename on collision, no confirmations, no mkdir confirmation, no error UI, early failure.
    // The preflight rejects known conflicts; a racing conflict receives a new name, never replacement.
    internal const uint OperationFlags = 0x0004 | 0x0008 | 0x0010 | 0x0200 | 0x0400 | 0x00100000;
    internal static Task<IReadOnlyList<string>> MoveAsync(string[] paths, string destination, Action? beforePerform = null)
    {
        var finished = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var worker = new Thread(() =>
        {
            try { finished.SetResult(Move(paths, destination, beforePerform)); }
            catch (Exception error) { finished.SetException(error); }
        }) { IsBackground = false, Name = "Citrus Shell move" };
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
        return finished.Task;
    }
    private static IReadOnlyList<string> Move(string[] paths, string destination, Action? beforePerform)
    {
        FileActions.ValidateMove(paths, destination);
        var operation = (IFileOperation)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("3AD05575-8857-4850-9277-11B85BDB8E09"), true)!)!;
        var items = new List<IShellItem>();
        var sink = new MoveProgressSink(paths);
        uint cookie = 0;
        try
        {
            operation.SetOperationFlags(OperationFlags);
            operation.Advise(sink, out cookie);
            var target = CreateItem(Path.GetFullPath(destination));
            items.Add(target);
            foreach (string path in paths)
            {
                var source = CreateItem(path);
                items.Add(source);
                operation.MoveItem(source, target, null, null);
            }
            beforePerform?.Invoke();
            operation.PerformOperations();
            operation.GetAnyOperationsAborted(out bool aborted);
            if (aborted || sink.Failure is not null || sink.Completed.Count != paths.Length)
                throw new IOException("The Shell move did not finish every item. " + (sink.Failure ?? "It was canceled or an item was skipped.") + sink.Summary);
            return sink.Completed.ToArray();
        }
        catch (COMException error) { throw new IOException("Windows could not complete the move: " + error.Message + sink.Summary, error); }
        finally
        {
            try { if (cookie != 0) operation.Unadvise(cookie); }
            finally
            {
                foreach (var item in items) Marshal.FinalReleaseComObject(item);
                Marshal.FinalReleaseComObject(operation);
                GC.KeepAlive(sink);
            }
        }
    }
    private static IShellItem CreateItem(string path)
    {
        var iid = typeof(IShellItem).GUID;
        Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(path, 0, ref iid, out var item));
        return item;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(string path, nint bindContext, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItem item);

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IShellItem
    {
        void BindToHandler(nint bindContext, ref Guid bhid, ref Guid riid, out nint result);
        void GetParent(out IShellItem parent);
        void GetDisplayName(uint type, out nint name);
        void GetAttributes(uint mask, out uint attributes);
        void Compare(IShellItem other, uint hint, out int order);
    }
    [ComImport, Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOperation
    {
        void Advise(IFileOperationProgressSink sink, out uint cookie);
        void Unadvise(uint cookie);
        void SetOperationFlags(uint flags);
        void SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string message);
        void SetProgressDialog([MarshalAs(UnmanagedType.Interface)] object dialog);
        void SetProperties([MarshalAs(UnmanagedType.Interface)] object properties);
        void SetOwnerWindow(uint window);
        void ApplyPropertiesToItem(IShellItem item);
        void ApplyPropertiesToItems([MarshalAs(UnmanagedType.IUnknown)] object items);
        void RenameItem(IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string name, IFileOperationProgressSink? sink);
        void RenameItems([MarshalAs(UnmanagedType.IUnknown)] object items, [MarshalAs(UnmanagedType.LPWStr)] string name);
        void MoveItem(IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string? name, IFileOperationProgressSink? sink);
        void MoveItems([MarshalAs(UnmanagedType.IUnknown)] object items, IShellItem destination);
        void CopyItem(IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string? name, IFileOperationProgressSink? sink);
        void CopyItems([MarshalAs(UnmanagedType.IUnknown)] object items, IShellItem destination);
        void DeleteItem(IShellItem item, IFileOperationProgressSink? sink);
        void DeleteItems([MarshalAs(UnmanagedType.IUnknown)] object items);
        void NewItem(IShellItem destination, uint attributes, [MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.LPWStr)] string template, IFileOperationProgressSink? sink);
        void PerformOperations();
        void GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool aborted);
    }
    [ComVisible(true), Guid("04B0F1A7-9490-44BC-96E1-4296A31252E2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IFileOperationProgressSink
    {
        [PreserveSig] int StartOperations();
        [PreserveSig] int FinishOperations(int result);
        [PreserveSig] int PreRenameItem(uint flags, IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int PostRenameItem(uint flags, IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string name, int result, IShellItem? created);
        [PreserveSig] int PreMoveItem(uint flags, IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int PostMoveItem(uint flags, IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name, int result, IShellItem? created);
        [PreserveSig] int PreCopyItem(uint flags, IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int PostCopyItem(uint flags, IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name, int result, IShellItem? created);
        [PreserveSig] int PreDeleteItem(uint flags, IShellItem item);
        [PreserveSig] int PostDeleteItem(uint flags, IShellItem item, int result, IShellItem? created);
        [PreserveSig] int PreNewItem(uint flags, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int PostNewItem(uint flags, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.LPWStr)] string template, uint attributes, int result, IShellItem? created);
        [PreserveSig] int UpdateProgress(uint total, uint complete);
        [PreserveSig] int ResetTimer();
        [PreserveSig] int PauseTimer();
        [PreserveSig] int ResumeTimer();
    }
    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public sealed class MoveProgressSink(IEnumerable<string> paths) : IFileOperationProgressSink
    {
        private readonly HashSet<string> roots = new(paths, StringComparer.OrdinalIgnoreCase);
        internal List<string> Completed { get; } = [];
        internal string? Failure { get; private set; }
        internal string Summary => Completed.Count == 0 ? " No completed moves were reported." : " Completed destinations: " + string.Join("; ", Completed);
        public int PostMoveItem(uint flags, IShellItem item, IShellItem destination, string name, int result, IShellItem? created)
        {
            if (result < 0) Failure ??= Marshal.GetExceptionForHR(result)?.Message ?? $"HRESULT 0x{result:X8}";
            else if (created is not null)
            {
                nint displayName = 0;
                try
                {
                    item.GetDisplayName(0x80058000, out displayName);
                    string original = Marshal.PtrToStringUni(displayName) ?? "";
                    Marshal.FreeCoTaskMem(displayName); displayName = 0;
                    if (roots.Contains(original))
                    {
                        created.GetDisplayName(0x80058000, out displayName);
                        Completed.Add(Marshal.PtrToStringUni(displayName) ?? name);
                    }
                }
                catch (COMException error) { Failure ??= error.Message; }
                finally { if (displayName != 0) Marshal.FreeCoTaskMem(displayName); }
            }
            return 0;
        }
        public int StartOperations() => 0;
        public int FinishOperations(int result) { if (result < 0) Failure ??= Marshal.GetExceptionForHR(result)?.Message; return 0; }
        public int PreMoveItem(uint flags, IShellItem item, IShellItem destination, string name) => 0;
        public int PreRenameItem(uint flags, IShellItem item, string name) => 0;
        public int PostRenameItem(uint flags, IShellItem item, string name, int result, IShellItem? created) => 0;
        public int PreCopyItem(uint flags, IShellItem item, IShellItem destination, string name) => 0;
        public int PostCopyItem(uint flags, IShellItem item, IShellItem destination, string name, int result, IShellItem? created) => 0;
        public int PreDeleteItem(uint flags, IShellItem item) => 0;
        public int PostDeleteItem(uint flags, IShellItem item, int result, IShellItem? created) => 0;
        public int PreNewItem(uint flags, IShellItem destination, string name) => 0;
        public int PostNewItem(uint flags, IShellItem destination, string name, string template, uint attributes, int result, IShellItem? created) => 0;
        public int UpdateProgress(uint total, uint complete) => 0;
        public int ResetTimer() => 0;
        public int PauseTimer() => 0;
        public int ResumeTimer() => 0;
    }
}
