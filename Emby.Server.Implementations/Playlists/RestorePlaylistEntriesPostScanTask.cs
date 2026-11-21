using System;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;

namespace Emby.Server.Implementations.Playlists;

/// <summary>
/// After a library scan, re-links playlist entries whose media is available again.
/// </summary>
public class RestorePlaylistEntriesPostScanTask : ILibraryPostScanTask
{
    private readonly PlaylistEntryRestorer _restorer;

    /// <summary>
    /// Initializes a new instance of the <see cref="RestorePlaylistEntriesPostScanTask"/> class.
    /// </summary>
    /// <param name="restorer">The playlist entry restorer.</param>
    public RestorePlaylistEntriesPostScanTask(PlaylistEntryRestorer restorer)
    {
        _restorer = restorer;
    }

    /// <inheritdoc />
    public async Task Run(IProgress<double> progress, CancellationToken cancellationToken)
    {
        await _restorer.RestoreAllAsync(cancellationToken).ConfigureAwait(false);
        progress.Report(100);
    }
}
