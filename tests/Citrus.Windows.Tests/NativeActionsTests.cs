using System.Diagnostics;
using System.IO;
using System.Text;
using Citrus.Core;
using Microsoft.Win32;
using Clipboard = System.Windows.Clipboard;
using DataFormats = System.Windows.DataFormats;

namespace Citrus.Windows.Tests;

[Collection("Desktop")]
public sealed class NativeActionsTests(DesktopFixture desktop)
{
    [Fact]
    public Task Desktop_has_real_clipboard_and_hidden_window_capabilities() => desktop.RunAsync(() =>
    {
        var window = new System.Windows.Window();
        try
        {
            Assert.NotEqual(nint.Zero, new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle());
            Clipboard.SetText("Citrus Windows desktop preflight");
            Assert.Equal("Citrus Windows desktop preflight", Clipboard.GetText());
        }
        finally { Clipboard.Clear(); window.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public void Folder_catalogue_exposes_all_six_actions_and_no_conversion_modes()
    {
        using var files = new NativeFiles();
        var catalog = new OperationCatalog([new NativeActionCatalog()]);
        Assert.Equal(6, catalog.GetOperations([files.Root], OperationMode.Actions).Count);
        Assert.Empty(catalog.GetOperations([files.Root], OperationMode.Convert));
        Assert.Empty(catalog.GetOperations([files.Root], OperationMode.Tools));
    }

    [Fact]
    public Task Clipboard_copy_keeps_entire_physical_selection_and_copy_effect() => desktop.RunAsync(async () =>
    {
        using var files = new NativeFiles();
        string folder = Directory.CreateDirectory(Path.Combine(files.Root, "folder Ω")).FullName;
        var paths = new[] { files.File("a ' & Ω.txt", "original"), folder, files.File("b.txt", "second") };
        try
        {
            await new FileActions().ExecuteAsync("action.copy", paths);
            Assert.Equal(paths, Clipboard.GetFileDropList().Cast<string>());
            var effect = Assert.IsType<MemoryStream>(Clipboard.GetData("Preferred DropEffect"));
            Assert.Equal(new byte[] { 1, 0, 0, 0 }, effect.ToArray());
            Assert.Equal("original", File.ReadAllText(paths[0]));
            Assert.True(Directory.Exists(folder));
        }
        finally { Clipboard.Clear(); }
    });

    [Fact]
    public Task Paths_clipboard_uses_exact_CRLF_selection() => desktop.RunAsync(async () =>
    {
        using var files = new NativeFiles();
        string[] paths = [files.File("one Ω.txt"), files.File("two & '.txt")];
        try
        {
            await new FileActions().ExecuteAsync("action.paths", paths);
            Assert.Equal(string.Join("\r\n", paths), Clipboard.GetText());
        }
        finally { Clipboard.Clear(); }
    });

    [Fact]
    public Task Move_cancel_and_batch_conflict_leave_all_sources_and_destinations_intact() => desktop.RunAsync(async () =>
    {
        using var files = new NativeFiles();
        string[] paths = [files.File("one.txt", "one"), files.File("two.txt", "two")];
        await Assert.ThrowsAsync<OperationCanceledException>(() => new FileActions(pickDestination: () => null).ExecuteAsync("action.move", paths));
        Assert.All(paths, path => Assert.True(File.Exists(path)));
        string target = Directory.CreateDirectory(Path.Combine(files.Root, "target")).FullName;
        string existing = Path.Combine(target, "two.txt");
        File.WriteAllText(existing, "keep");
        await Assert.ThrowsAsync<IOException>(() => new FileActions(pickDestination: () => target).ExecuteAsync("action.move", paths));
        Assert.All(paths, path => Assert.True(File.Exists(path)));
        Assert.Equal("keep", File.ReadAllText(existing));
        Assert.False(File.Exists(Path.Combine(target, "one.txt")));
        File.Delete(existing);
        await new FileActions(pickDestination: () => target).ExecuteAsync("action.move", paths);
        Assert.All(paths, path => Assert.False(File.Exists(path)));
        Assert.Equal("one", File.ReadAllText(Path.Combine(target, "one.txt")));
        Assert.Equal("two", File.ReadAllText(existing));
    });

    [Fact]
    public Task Launch_arguments_are_structured_and_terminal_fallback_inherits_directory() => desktop.RunAsync(async () =>
    {
        using var files = new NativeFiles();
        string path = files.File("Ω ' & % !.txt");
        var starts = new List<ProcessStartInfo>();
        var actions = new FileActions(launch: starts.Add, terminalPath: () => @"C:\installed\wt.exe");
        await actions.ExecuteAsync("action.reveal", [path]);
        Assert.EndsWith("explorer.exe", starts[0].FileName, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new[] { "/select,", path }, starts[0].ArgumentList);
        Assert.False(starts[0].UseShellExecute);
        await actions.ExecuteAsync("action.terminal", [path, path]);
        Assert.Equal(new[] { "-d", files.Root }, starts[1].ArgumentList);
        Assert.Equal(files.Root, starts[1].WorkingDirectory);
        var fallback = FileActions.BuildTerminalStart(files.Root, null);
        Assert.Equal(files.Root, fallback.WorkingDirectory);
        Assert.DoesNotContain(fallback.ArgumentList, argument => argument.Contains(files.Root, StringComparison.Ordinal));
        Assert.False(fallback.UseShellExecute);
    });

    [Fact]
    public Task Shelf_holds_references_prunes_missing_and_builds_copy_drag_for_whole_selection() => desktop.RunAsync(() =>
    {
        using var files = new NativeFiles();
        var paths = new[] { files.File("one.txt"), files.File("two.txt") };
        using var shelf = new ShelfWindow();
        shelf.AddPaths(paths);
        Assert.Equal(paths, shelf.Paths);
        var data = FileActions.CreateCopyData(shelf.Paths);
        Assert.Equal(paths, Assert.IsType<string[]>(data.GetData(DataFormats.FileDrop)));
        File.Delete(paths[0]);
        shelf.RefreshReferences();
        Assert.Equal(new[] { paths[1] }, shelf.Paths);
        Assert.Equal("content", File.ReadAllText(paths[1]));
        return Task.CompletedTask;
    });

    [Fact]
    public void Settings_default_to_no_startup_and_round_trip_all_preferences()
    {
        using var files = new NativeFiles();
        var store = new SettingsStore(files.Root);
        Assert.Equal(new CitrusSettings(), store.Load());
        Assert.False(store.Load().LaunchAtSignIn);
        var chosen = new CitrusSettings { Theme = ThemeChoice.Light, RevealCompletedOutputs = true, GesturesEnabled = false,
            LaunchAtSignIn = true, ShiftMode = OperationMode.Actions, AltMode = OperationMode.Convert, ControlMode = OperationMode.Tools };
        store.Save(chosen);
        Assert.Equal(chosen, store.Load());
        Assert.Equal(OperationMode.Actions, chosen.Remap(OperationMode.Convert));
        Assert.Equal(OperationMode.Convert, chosen.Remap(OperationMode.Tools));
        Assert.Equal(OperationMode.Tools, chosen.Remap(OperationMode.Actions));
        Assert.Throws<ArgumentException>(() => store.Save(chosen with { ShiftMode = OperationMode.Tools }));
        File.WriteAllText(store.FilePath, "broken json");
        Assert.Equal(new CitrusSettings(), store.Load());
    }

    [Fact]
    public void Startup_registration_is_opt_in_and_preserves_unrelated_values()
    {
        string key = @"Software\Citrus.Tests\" + Guid.NewGuid().ToString("N");
        try
        {
            using (var registry = Registry.CurrentUser.CreateSubKey(key)) registry.SetValue("Other", "preserve");
            var startup = new StartupRegistration(key, "CitrusTest", @"C:\Program Files\Citrus\Citrus.exe");
            using (var registry = Registry.CurrentUser.OpenSubKey(key)) Assert.Null(registry!.GetValue("CitrusTest"));
            startup.Apply(true);
            using (var registry = Registry.CurrentUser.OpenSubKey(key)) Assert.Equal("\"C:\\Program Files\\Citrus\\Citrus.exe\" --background", registry!.GetValue("CitrusTest"));
            startup.Apply(false);
            using (var registry = Registry.CurrentUser.OpenSubKey(key)) { Assert.Null(registry!.GetValue("CitrusTest")); Assert.Equal("preserve", registry.GetValue("Other")); }
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(key, false); }
    }
}

internal sealed class NativeFiles : IDisposable
{
    internal string Root { get; } = Path.Combine(Path.GetTempPath(), "Citrus-native-" + Guid.NewGuid().ToString("N"));
    internal NativeFiles() => Directory.CreateDirectory(Root);
    internal string File(string name, string contents = "content") { string path = Path.Combine(Root, name); System.IO.File.WriteAllText(path, contents, Encoding.UTF8); return path; }
    public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
}
