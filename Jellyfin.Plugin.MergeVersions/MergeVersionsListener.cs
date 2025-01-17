using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MergeVersions
{
    public class MergeVersionsListener : IHostedService, IDisposable
    {
        private readonly ILibraryManager _libraryManager;
        private readonly MergeVersionsManager _mergeVersionsManager;
        private readonly ILogger<MergeVersionsListener> _logger;

        private bool _mergeInProgress = false;
        private static readonly object _mergeLock = new object();
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(10); // limit concurrent async tasks
        
        private enum MediaType
        {
            Movie,
            Episode
        }

        public MergeVersionsListener(
            ILibraryManager libraryManager, 
            MergeVersionsManager mergeVersionsManager, 
            ILogger<MergeVersionsListener> logger)
        {
            _libraryManager = libraryManager;
            _mergeVersionsManager = mergeVersionsManager; 
            _logger = logger;
        }

        private async void OnLibraryManagerItemRemoved(object sender, ItemChangeEventArgs e)
        {            
            if (string.IsNullOrEmpty(e.Item.Name))
            {
                return;
            }

            string name;
            string productionYear = string.Empty;
            string seriesName = string.Empty;
            string parentIndexNumber = string.Empty;
            string indexNumber = string.Empty;
            MediaType mediaType;

            if (e.Item is Movie movie) 
            {
                name = movie.Name;
                productionYear = movie.ProductionYear.ToString();
                mediaType = MediaType.Movie;
            }
            else if (e.Item is Episode episode) 
            {
                name = episode.Name;
                productionYear = episode.ProductionYear.ToString();
                seriesName = episode.SeriesName;
                parentIndexNumber = episode.ParentIndexNumber.ToString();
                indexNumber = episode.IndexNumber.ToString();
                mediaType = MediaType.Episode;
            }
            else 
            {
                return;
            }

            int? productionYearInt = 
                !string.IsNullOrEmpty(productionYear) && int.TryParse(productionYear, out var parsedProdYear) ? (int?)parsedProdYear : null;
            int? parentIndexNumberInt = 
                !string.IsNullOrEmpty(parentIndexNumber) && int.TryParse(parentIndexNumber, out var parsedParentIndex) ? (int?)parsedParentIndex : null;
            int? indexNumberInt = 
                !string.IsNullOrEmpty(indexNumber) && int.TryParse(indexNumber, out var parsedIndex) ? (int?)parsedIndex : null;


            await SplitRemovedItem(name, productionYearInt, seriesName, parentIndexNumberInt, indexNumberInt, mediaType);

            await Task.Delay(TimeSpan.FromSeconds(10)).ConfigureAwait(false);    
            await MergeRemovedItem(name, productionYearInt, seriesName, parentIndexNumberInt, indexNumberInt, mediaType);
        }
        
        private async Task SplitRemovedItem(
            string name, int? productionYearInt, string seriesName, int? parentIndexNumberInt, int? indexNumberInt, MediaType mediaType)
        {            
            await _semaphore.WaitAsync();
            try
            {
                if (mediaType == MediaType.Movie)
                {
                    //_logger.LogInformation($"Movie deleted, splitting versions: {name} ({productionYearInt})");
                    await _mergeVersionsManager.SplitMoviesAsync(name, productionYearInt, true, null);
                }
                else if (mediaType == MediaType.Episode)
                {
                    //_logger.LogInformation($"Episode deleted, splitting versions: {seriesName}: S{parentIndexNumberInt} E{indexNumberInt} - {name} ({productionYearInt})");
                    await _mergeVersionsManager.SplitEpisodesAsync(name, productionYearInt, seriesName, parentIndexNumberInt, indexNumberInt, true, null);
                }
            }
            catch (TaskCanceledException){ }
            finally
            {
                _semaphore.Release();
            }
        }

        private async Task MergeRemovedItem(
            string name, int? productionYearInt, string seriesName, int? parentIndexNumberInt, int? indexNumberInt, MediaType mediaType)
        {
            if (_mergeInProgress)
            {
                return;
            }
                
            //_logger.LogInformation($"Doing single merge...");
            await _semaphore.WaitAsync();
            try
            {
                if (mediaType == MediaType.Movie)
                {
                    //_logger.LogInformation($"Searching versions for Movie: {name} ({productionYearInt})");
                    await _mergeVersionsManager.MergeMoviesAsync(name, productionYearInt, true, null);
                }
                else if (mediaType == MediaType.Episode)
                {
                    //_logger.LogInformation($"Searching versions for Episode: {seriesName}: S{parentIndexNumberInt} E{indexNumberInt} - {name} ({productionYearInt})");
                    await _mergeVersionsManager.MergeEpisodesAsync(name, productionYearInt, seriesName, parentIndexNumberInt, indexNumberInt, true, null);
                }
            }
            catch (TaskCanceledException){ }
            finally
            {                
                _semaphore.Release();
            }   
        }

        private async void OnLibraryManagerItemAdded(object sender, ItemChangeEventArgs e)
        {
            if (!(e.Item is Movie) && !(e.Item is Episode) || e.Item.LocationType == LocationType.Virtual || _mergeInProgress)
            {
                return;
            }

            using (var cancellationTokenSource = new CancellationTokenSource())
            {
                var cancellationToken = cancellationTokenSource.Token;
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(60), cancellationToken).ConfigureAwait(false);
                }
                catch (TaskCanceledException)
                {
                    return;
                }

                lock (_mergeLock)
                {
                    if (_mergeInProgress)
                    {
                        return;
                    }
                    _mergeInProgress = true;
                    cancellationTokenSource.Cancel();
                }

                try
                {
                    await _mergeVersionsManager.MergeMoviesAsync(null, null, false, null);
                    await _mergeVersionsManager.MergeEpisodesAsync(null, null, null, null, null, false, null);
                }
                catch (TaskCanceledException) { }
                finally
                {
                    lock (_mergeLock)
                    {
                        _mergeInProgress = false;
                    }
                }
            }
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            // Subscribe to the library's item added and removed event
            _libraryManager.ItemAdded += OnLibraryManagerItemAdded;
            _libraryManager.ItemRemoved += OnLibraryManagerItemRemoved;

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            // Unsubscribe to the library's item added and removed event
            _libraryManager.ItemAdded -= OnLibraryManagerItemAdded;
            _libraryManager.ItemRemoved -= OnLibraryManagerItemRemoved;

            return Task.CompletedTask;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing) 
            { 
                _semaphore?.Dispose();
            }
        }

    }
}
