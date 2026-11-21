using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;

namespace MediaBrowser.Controller.Playlists;

/// <summary>
/// Reads the entries of a Jellyfin-managed playlist from its playlist.xml.
/// </summary>
/// <remarks>
/// The database only links a playlist to items that exist. When the media behind an entry goes
/// offline (an unplugged drive, an unmounted share), the item is removed from the library and its
/// link goes with it. playlist.xml lists entries by path, so it is what remembers them until the
/// media comes back.
/// </remarks>
public static class PlaylistEntryFile
{
    /// <summary>
    /// The file name of a Jellyfin-managed playlist's metadata.
    /// </summary>
    public const string FileName = "playlist.xml";

    /// <summary>
    /// Gets the playlist.xml path for a Jellyfin-managed (folder) playlist.
    /// </summary>
    /// <param name="playlist">The playlist.</param>
    /// <returns>The path, or <c>null</c> if the playlist is itself a playlist file such as an .m3u.</returns>
    public static string? GetPath(Playlist playlist)
    {
        ArgumentNullException.ThrowIfNull(playlist);
        if (string.IsNullOrEmpty(playlist.Path) || Playlist.IsPlaylistFile(playlist.Path))
        {
            return null;
        }

        return Path.Combine(playlist.Path, FileName);
    }

    /// <summary>
    /// Reads the entry paths from a playlist.xml, in order.
    /// </summary>
    /// <param name="xmlPath">The playlist.xml path.</param>
    /// <returns>The entry paths; empty if the file is missing or unreadable.</returns>
    public static IReadOnlyList<string> ReadEntryPaths(string xmlPath)
    {
        var paths = new List<string>();
        if (!File.Exists(xmlPath))
        {
            return paths;
        }

        var settings = new XmlReaderSettings
        {
            IgnoreComments = true,
            IgnoreWhitespace = true,
            IgnoreProcessingInstructions = true,
            DtdProcessing = DtdProcessing.Prohibit
        };

        try
        {
            using var reader = XmlReader.Create(xmlPath, settings);
            var inEntry = false;
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element)
                {
                    continue;
                }

                if (string.Equals(reader.Name, "PlaylistItem", StringComparison.Ordinal))
                {
                    inEntry = true;
                }
                else if (inEntry && string.Equals(reader.Name, "Path", StringComparison.Ordinal))
                {
                    inEntry = false;
                    var value = reader.ReadElementContentAsString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        paths.Add(value);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is XmlException or IOException or UnauthorizedAccessException)
        {
            // A damaged or unreadable file means there is nothing we can safely keep.
            return [];
        }

        return paths;
    }

    /// <summary>
    /// Merges entries that are offline back into a list of current entry paths, each at the
    /// position it had in the previous list.
    /// </summary>
    /// <param name="current">The current entries, in order.</param>
    /// <param name="previous">The entries as last written, in order.</param>
    /// <param name="isOffline">Whether an entry missing from <paramref name="current"/> is offline (its
    /// media is gone) rather than removed by the user.</param>
    /// <returns>The merged entries.</returns>
    public static List<string> MergeOfflineEntries(
        IReadOnlyList<string> current,
        IReadOnlyList<string> previous,
        Func<string, bool> isOffline)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(isOffline);

        var output = new List<string>(current);
        var present = new HashSet<string>(current, StringComparer.Ordinal);
        var insertAt = 0;
        foreach (var path in previous)
        {
            var index = output.IndexOf(path);
            if (index >= 0)
            {
                // Offline entries that followed this one go right after it.
                insertAt = index + 1;
            }
            else if (!present.Contains(path) && isOffline(path))
            {
                output.Insert(insertAt, path);
                present.Add(path);
                insertAt++;
            }
        }

        return output;
    }
}
