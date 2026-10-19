using Citrus.Core;

namespace Citrus.Core.Tests;

public sealed class WorkspaceSelectionTests
{
    [Fact]
    public void Preview_navigation_never_changes_explicit_batch_membership()
    {
        var selection = new WorkspaceSelection();
        selection.Add(["C:\\Photos\\one.png", "C:\\Photos\\two.png"]);
        selection.Items[0].IsIncluded = false;
        selection.Preview(selection.Items[0]);
        Assert.Equal(selection.Items[0], selection.Active);
        Assert.Equal(["C:\\Photos\\two.png"], selection.IncludedPaths);
        selection.Preview(selection.Items[1]);
        selection.Preview(null);
        Assert.Equal(["C:\\Photos\\two.png"], selection.IncludedPaths);
    }

    [Fact]
    public void Inputs_deduplicate_case_insensitively_without_resetting_inclusion_or_order()
    {
        var selection = new WorkspaceSelection();
        selection.Add(["C:\\Photos\\one.png", "C:\\Photos\\two.png"]);
        selection.Items[0].IsIncluded = false;
        selection.Add(["c:\\photos\\ONE.png", "C:\\Photos\\three.png"]);
        Assert.Equal(3, selection.Items.Count);
        Assert.False(selection.Items[0].IsIncluded);
        Assert.Equal(["C:\\Photos\\two.png", "C:\\Photos\\three.png"], selection.IncludedPaths);
    }

    [Fact]
    public void Removing_preview_selects_remaining_item_and_does_not_delete_file()
    {
        string file = Path.GetTempFileName();
        try
        {
            var selection = new WorkspaceSelection();
            selection.Add([file, "second.png"]);
            selection.Remove(selection.Active!);
            Assert.True(File.Exists(file));
            Assert.Equal("second.png", selection.Active?.Path);
            selection.Clear();
            Assert.Null(selection.Active);
            Assert.Empty(selection.IncludedPaths);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void Removing_active_item_survives_ui_clearing_selection_during_collection_change()
    {
        var selection = new WorkspaceSelection();
        selection.Add(["one.png", "two.png"]);
        ((System.Collections.Specialized.INotifyCollectionChanged)selection.Items).CollectionChanged += (_, _) => selection.Preview(null);
        selection.Remove(selection.Active!);
        Assert.Equal("two.png", selection.Active?.Path);
    }

    [Fact]
    public void Submission_snapshot_is_stable_when_preview_and_inclusion_change()
    {
        var selection = new WorkspaceSelection();
        selection.Add(["one.png", "two.png"]);
        var snapshot = selection.IncludedPaths;
        selection.SetAllIncluded(false);
        selection.Preview(selection.Items[1]);
        Assert.Equal(["one.png", "two.png"], snapshot);
        Assert.Empty(selection.IncludedPaths);
        selection.SetAllIncluded(true);
        Assert.Equal(snapshot, selection.IncludedPaths);
    }
}
