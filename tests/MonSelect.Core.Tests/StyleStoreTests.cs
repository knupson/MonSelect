using MonSelect.Core.Windows;

namespace MonSelect.Core.Tests;

public class StyleStoreTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("monselect-style");

    private string Path => System.IO.Path.Combine(_dir.FullName, "borderless.json");

    public void Dispose() => _dir.Delete(recursive: true);

    [Fact]
    public void Remembers_and_returns_an_original_style()
    {
        var store = new StyleStore(Path);
        store.Remember(new BorderlessRecord(1234, 23340, 638000000000, 0x00CF0000));

        Assert.True(store.TryGet(1234, out var found));
        Assert.Equal(0x00CF0000u, found.OriginalStyle);
    }

    [Fact]
    public void Forget_returns_the_record_and_removes_it()
    {
        var store = new StyleStore(Path);
        store.Remember(new BorderlessRecord(1234, 23340, 1, 0x00CF0000));

        var removed = store.Forget(1234);

        Assert.NotNull(removed);
        Assert.Equal(0x00CF0000u, removed!.OriginalStyle);
        Assert.False(store.TryGet(1234, out _));
    }

    [Fact]
    public void Forgetting_an_unknown_handle_returns_null()
    {
        Assert.Null(new StyleStore(Path).Forget(999));
    }

    [Fact]
    public void Remembering_the_same_handle_twice_keeps_the_first_style()
    {
        // La segunda vez el style ya está mutilado; guardarlo perdería el original.
        var store = new StyleStore(Path);
        store.Remember(new BorderlessRecord(1234, 1, 1, 0x00CF0000));
        store.Remember(new BorderlessRecord(1234, 1, 1, 0x000F0000));

        store.TryGet(1234, out var found);
        Assert.Equal(0x00CF0000u, found.OriginalStyle);
    }

    [Fact]
    public void Survives_a_save_and_reload()
    {
        var first = new StyleStore(Path);
        first.Remember(new BorderlessRecord(1234, 23340, 638000000000, 0x00CF0000));
        first.Save();

        var second = new StyleStore(Path);
        second.Load();

        Assert.True(second.TryGet(1234, out var found));
        Assert.Equal(23340u, found.ProcessId);
        Assert.Equal(638000000000, found.ProcessStartTicks);
    }

    [Fact]
    public void Loading_a_missing_file_yields_an_empty_store()
    {
        var store = new StyleStore(Path);
        store.Load();

        Assert.Empty(store.All());
    }

    [Fact]
    public void Loading_a_corrupt_file_yields_an_empty_store_instead_of_throwing()
    {
        File.WriteAllText(Path, "{ esto no es json");

        var store = new StyleStore(Path);
        store.Load();

        Assert.Empty(store.All());
    }

    [Fact]
    public void Saving_when_directory_cannot_be_created_does_not_throw()
    {
        // Create a file where we want a directory, forcing directory creation to fail.
        var blockingFile = System.IO.Path.Combine(_dir.FullName, "blocker.txt");
        File.WriteAllText(blockingFile, "x");

        var blockedPath = System.IO.Path.Combine(blockingFile, "subdir", "store.json");
        var store = new StyleStore(blockedPath);
        store.Remember(new BorderlessRecord(1234, 23340, 638000000000, 0x00CF0000));

        // Should not throw despite the directory creation failure.
        store.Save();

        // Records should still be queryable in memory.
        Assert.True(store.TryGet(1234, out var found));
        Assert.Equal(0x00CF0000u, found.OriginalStyle);
    }

    [Fact]
    public void No_temporary_file_is_left_behind_after_successful_save()
    {
        var store = new StyleStore(Path);
        store.Remember(new BorderlessRecord(1234, 23340, 638000000000, 0x00CF0000));
        store.Save();

        // Verify the main file exists and the temp file does not.
        Assert.True(File.Exists(Path), "Main file should exist");
        Assert.False(File.Exists(Path + ".tmp"), "Temp file should not exist");

        // Verify directory contains exactly one file (the json, no .tmp).
        var files = Directory.GetFiles(_dir.FullName);
        Assert.Single(files);
        Assert.Equal(System.IO.Path.GetFileName(Path), System.IO.Path.GetFileName(files[0]));
    }
}
