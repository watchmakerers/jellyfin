using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using Microsoft.Extensions.Logging;

namespace Emby.Server.Implementations.Playlists;

/// <summary>
/// Puts playlist entries back once their media is available again.
/// </summary>
/// <remarks>
/// When a drive or share goes offline and the library is scanned, its items are removed and with them
/// their playlist links. The playlist's playlist.xml keeps those entries (see
/// <see cref="PlaylistEntryFile"/>); once an item with the same path exists again, this re-links it at
/// its place in the playlist.
/// </remarks>
public sealed class PlaylistEntryRestorer : IDisposable
{
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<PlaylistEntryRestorer> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="PlaylistEntryRestorer"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="logger">The logger.</param>
    public PlaylistEntryRestorer(ILibraryManager libraryManager, ILogger<PlaylistEntryRestorer> logger)
    {
        _libraryManager = libraryManager;
        _logger = logger;
    }

    /// <summary>
    /// Re-links the entries of every playlist whose media has come back.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of entries restored.</returns>
    public async Task<int> RestoreAllAsync(CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var playlistIds = _libraryManager.GetItemIds(new InternalItemsQuery
            {
                IncludeItemTypes = [BaseItemKind.Playlist],
                Recursive = true
            });

            var restored = 0;
            foreach (var id in playlistIds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_libraryManager.GetItemById(id) is Playlist playlist)
                {
                    restored += await RestoreAsync(playlist, cancellationToken).ConfigureAwait(false);
                }
            }

            return restored;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<int> RestoreAsync(Playlist playlist, CancellationToken cancellationToken)
    {
        var xmlPath = PlaylistEntryFile.GetPath(playlist);
        if (xmlPath is null || !playlist.LinkedChildrenLoaded)
        {
            return 0;
        }

        var storedPaths = PlaylistEntryFile.ReadEntryPaths(xmlPath);
        if (storedPaths.Count == 0)
        {
            return 0;
        }

        var manual = playlist.LinkedChildren.Where(c => c.Type == LinkedChildType.Manual).ToList();
        var linkedIds = manual
            .Where(c => c.ItemId.HasValue)
            .Select(c => c.ItemId!.Value)
            .ToHashSet();

        // Every playlist edit rewrites playlist.xml, so an entry that is in the file and whose item
        // exists but that is not linked can only have lost its link when the item was removed.
        var available = new Dictionary<string, BaseItem>(StringComparer.Ordinal);
        foreach (var path in storedPaths.Distinct(StringComparer.Ordinal))
        {
            var item = _libraryManager.FindByPath(path, false);
            if (item is not null && !linkedIds.Contains(item.Id))
            {
                available[path] = item;
            }
        }

        if (available.Count == 0)
        {
            return 0;
        }

        // Rebuild the list in the file's order: linked entries stay as they are, returning ones are
        // linked again, entries still offline are skipped (the file keeps them).
        var unused = new List<LinkedChild>(manual);
        var rebuilt = new List<LinkedChild>(manual.Count + available.Count);
        var restored = 0;
        foreach (var path in storedPaths)
        {
            if (available.Remove(path, out var item))
            {
                rebuilt.Add(new LinkedChild { ItemId = item.Id, Type = LinkedChildType.Manual });
                restored++;
                continue;
            }

            var existing = unused.FirstOrDefault(c => string.Equals(ResolvePath(c), path, StringComparison.Ordinal));
            if (existing is not null)
            {
                unused.Remove(existing);
                rebuilt.Add(existing);
            }
        }

        // Anything the file doesn't know about (it is rewritten right after changes, so normally
        // nothing) keeps its place at the end; automatic links are left untouched.
        rebuilt.AddRange(unused);
        rebuilt.AddRange(playlist.LinkedChildren.Where(c => c.Type != LinkedChildType.Manual));

        playlist.LinkedChildren = [.. rebuilt];
        await playlist.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Restored {Count} entries of playlist {Name} whose media is available again",
            restored,
            playlist.Name);

        return restored;
    }

    /// <inheritdoc />
    public void Dispose() => _lock.Dispose();

    private string? ResolvePath(LinkedChild child)
        => child.ItemId.HasValue ? _libraryManager.GetItemById(child.ItemId.Value)?.Path : null;
}
