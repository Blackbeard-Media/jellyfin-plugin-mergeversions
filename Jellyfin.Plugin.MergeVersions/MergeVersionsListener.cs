using System;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MergeVersions
{
    public class MergeVersionsListener
    {
        private readonly ILibraryManager _libraryManager;
        private readonly MergeVersionsManager _mergeVersionsManager;
        private readonly ILogger<MergeVersionsListener> _logger;

        private CancellationTokenSource _episodeCancellationTokenSource = new CancellationTokenSource();
        private const int episodeDelaySeconds = 180;
        private CancellationTokenSource _movieCancellationTokenSource = new CancellationTokenSource();
        private const int movieDelaySeconds = 60;

        public MergeVersionsListener(
            ILibraryManager libraryManager, 
            MergeVersionsManager mergeVersionsManager, 
            ILogger<MergeVersionsListener> logger)
        {
            _libraryManager = libraryManager;
            _mergeVersionsManager = mergeVersionsManager;
            _logger = logger;

            // Subscribe to the library's item added event
            _libraryManager.ItemAdded += OnItemAdded;
        }

        private async void OnItemAdded(object sender, ItemChangeEventArgs e)
        {

            var item = e.Item;

            if (item is Movie movie)
            {
                // Cancel any previous movie delay if exists
                _movieCancellationTokenSource?.Cancel();
                _movieCancellationTokenSource = new CancellationTokenSource();

                try                
                {
                    await Task.Delay(TimeSpan.FromSeconds(movieDelaySeconds), _movieCancellationTokenSource.Token);
                }
                catch (TaskCanceledException)
                {
                    return;
                }

                _logger.LogInformation($"New movie added, {movieDelaySeconds}s ago");

                var progress = new Progress<double>(percent =>
                {
                    _logger.LogInformation($"Merging movies progress: {percent}%");
                });

                await _mergeVersionsManager.MergeMoviesAsync(progress);
            }
            else if (item is Episode episode)
            {
                // Cancel any previous episode delay if exists
                _episodeCancellationTokenSource?.Cancel();
                _episodeCancellationTokenSource = new CancellationTokenSource();

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(episodeDelaySeconds), _episodeCancellationTokenSource.Token);
                }
                catch (TaskCanceledException)
                {
                    return;
                }

                _logger.LogInformation($"New episode added, {episodeDelaySeconds}s ago");

                var progress = new Progress<double>(percent =>
                {
                    _logger.LogInformation($"Merging episodes progress: {percent}%");
                });

                await _mergeVersionsManager.MergeEpisodesAsync(progress);
            }
        }
  
    }
}
