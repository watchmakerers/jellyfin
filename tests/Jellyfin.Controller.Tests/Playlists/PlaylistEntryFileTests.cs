using System;
using System.Collections.Generic;
using System.IO;
using MediaBrowser.Controller.Playlists;
using Xunit;

namespace Jellyfin.Controller.Tests.Playlists;

public class PlaylistEntryFileTests
{
    private static readonly HashSet<string> _offline = new(StringComparer.Ordinal) { "/usb/b", "/usb/d", "/usb/e" };

    private static bool IsOffline(string path) => _offline.Contains(path);

    [Fact]
    public void MergeOfflineEntries_KeepsOfflineEntriesInPlace()
    {
        // b and d went offline, so the database only still links a, c and f.
        var merged = PlaylistEntryFile.MergeOfflineEntries(
            ["/a", "/c", "/f"],
            ["/a", "/usb/b", "/c", "/usb/d", "/usb/e", "/f"],
            IsOffline);

        Assert.Equal(["/a", "/usb/b", "/c", "/usb/d", "/usb/e", "/f"], merged);
    }

    [Fact]
    public void MergeOfflineEntries_OfflineFirstEntry_StaysFirst()
    {
        var merged = PlaylistEntryFile.MergeOfflineEntries(["/a"], ["/usb/b", "/a"], IsOffline);

        Assert.Equal(["/usb/b", "/a"], merged);
    }

    [Fact]
    public void MergeOfflineEntries_EverythingOffline_KeepsTheWholePlaylist()
    {
        var merged = PlaylistEntryFile.MergeOfflineEntries([], ["/usb/b", "/usb/d"], IsOffline);

        Assert.Equal(["/usb/b", "/usb/d"], merged);
    }

    [Fact]
    public void MergeOfflineEntries_RemovedByUser_StaysRemoved()
    {
        // /x still exists in the library, so it was removed from the playlist on purpose.
        var merged = PlaylistEntryFile.MergeOfflineEntries(["/a"], ["/a", "/x"], IsOffline);

        Assert.Equal(["/a"], merged);
    }

    [Fact]
    public void MergeOfflineEntries_FollowsTheCurrentOrder()
    {
        // The user moved /c to the front; the offline entry stays after the one it followed.
        var merged = PlaylistEntryFile.MergeOfflineEntries(["/c", "/a"], ["/a", "/usb/b", "/c"], IsOffline);

        Assert.Equal(["/c", "/a", "/usb/b"], merged);
    }

    [Fact]
    public void ReadEntryPaths_ReadsEntriesInOrder()
    {
        var file = Path.GetTempFileName();
        try
        {
            File.WriteAllText(
                file,
                """
                <?xml version="1.0" encoding="utf-8" standalone="yes"?>
                <Item>
                  <Title>Road Trip</Title>
                  <PlaylistItems>
                    <PlaylistItem><Path>/usb/b</Path></PlaylistItem>
                    <PlaylistItem><Path>/a</Path></PlaylistItem>
                  </PlaylistItems>
                </Item>
                """);

            Assert.Equal(["/usb/b", "/a"], PlaylistEntryFile.ReadEntryPaths(file));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void ReadEntryPaths_DamagedOrMissingFile_ReturnsNothing()
    {
        var file = Path.GetTempFileName();
        try
        {
            File.WriteAllText(file, "<Item><PlaylistItems><PlaylistItem><Path>/a");
            Assert.Empty(PlaylistEntryFile.ReadEntryPaths(file));
        }
        finally
        {
            File.Delete(file);
        }

        Assert.Empty(PlaylistEntryFile.ReadEntryPaths(file));
    }
}
