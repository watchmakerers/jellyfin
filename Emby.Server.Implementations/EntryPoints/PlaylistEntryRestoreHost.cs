using System;
using System.Threading;
using System.Threading.Tasks;
using Emby.Server.Implementations.Playlists;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Emby.Server.Implementations.EntryPoints;

/// <summary>
/// Re-links playlist entries when the real-time monitor brings their media back. Full scans are
/// covered by <see cref="RestorePlaylistEntriesPostScanTask"/>; items the monitor adds don't run
/// post-scan tasks.
/// </summary>
public sealed class PlaylistEntryRestoreHost : IHostedService, IDisposable
{
    // Media coming back usually means a whole drive: wait for the additions to settle.
    private static readonly TimeSpan _settleDelay = TimeSpan.FromSeconds(30);

    private readonly ILibraryManager _libraryManager;
    private readonly PlaylistEntryRestorer _restorer;
    private readonly ILogger<PlaylistEntryRestoreHost> _logger;
    private readonly Timer _timer;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlaylistEntryRestoreHost"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="restorer">The playlist entry restorer.</param>
    /// <param name="logger">The logger.</param>
    public PlaylistEntryRestoreHost(
        ILibraryManager libraryManager,
        PlaylistEntryRestorer restorer,
        ILogger<PlaylistEntryRestoreHost> logger)
    {
        _libraryManager = libraryManager;
        _restorer = restorer;
        _logger = logger;
        _timer = new Timer(OnSettled, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded += OnItemAdded;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded -= OnItemAdded;
        _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose() => _timer.Dispose();

    private void OnItemAdded(object? sender, ItemChangeEventArgs e)
    {
        // Scans restore through the post-scan task; only react to the monitor's own additions.
        if (!e.Item.IsFolder && !_libraryManager.IsScanRunning)
        {
            _timer.Change(_settleDelay, Timeout.InfiniteTimeSpan);
        }
    }

    private async void OnSettled(object? state)
    {
        try
        {
            await _restorer.RestoreAllAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error restoring playlist entries");
        }
    }
}
