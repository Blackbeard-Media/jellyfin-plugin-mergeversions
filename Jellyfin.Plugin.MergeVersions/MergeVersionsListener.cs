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

        private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        private const int delaySeconds = 60;    

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
            // Cancel the previous timer if it exists
            _cancellationTokenSource.Cancel();
            _cancellationTokenSource = new CancellationTokenSource();

            // wait 10s
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), _cancellationTokenSource.Token);
            }
            catch (TaskCanceledException)
            {
                return;
            }

            var item = e.Item;

            if (item is Movie movie)
            {
                _logger.LogInformation($"New movie added");

                var progress = new Progress<double>(percent =>
                {
                    _logger.LogInformation($"Merging movies progress: {percent}%");
                });

                await _mergeVersionsManager.MergeMoviesAsync(progress);
            }
            else if (item is Episode episode)
            {
                _logger.LogInformation($"New episode added");

                var progress = new Progress<double>(percent =>
                {
                    _logger.LogInformation($"Merging episodes progress: {percent}%");
                });

                await _mergeVersionsManager.MergeEpisodesAsync(progress);
            }
        }
  
    }
}
