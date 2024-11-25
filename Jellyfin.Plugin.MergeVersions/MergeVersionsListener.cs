using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MergeVersions
{
    public class MergeVersionsListener : IDisposable
    {
        private readonly ILibraryManager _libraryManager;
        private readonly MergeVersionsManager _mergeVersionsManager;
        private readonly ILogger<MergeVersionsListener> _logger;

        private readonly ConcurrentDictionary<string, bool> _processingMergeItems = new ConcurrentDictionary<string, bool>();
        private readonly ConcurrentDictionary<string, bool> _processingSplitItems = new ConcurrentDictionary<string, bool>();
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(4); // limit concurrent async tasks
        
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

            await SplitRemovedItem(name, productionYear, seriesName, parentIndexNumber, indexNumber, mediaType);
        }
        
        private async Task SplitRemovedItem(
            string name, string productionYear, string seriesName, string parentIndexNumber, string indexNumber, MediaType mediaType)
        {
            string key = $"{name}-{productionYear}-{seriesName}-{parentIndexNumber}-{indexNumber}-{mediaType}";
            if (!_processingSplitItems.TryAdd(key, true))
            {
                return;
            }
            
            await _semaphore.WaitAsync();
            try
            {
                int? productionYearInt = 
                    !string.IsNullOrEmpty(productionYear) && int.TryParse(productionYear, out var parsedProdYear) ? (int?)parsedProdYear : null;
                int? parentIndexNumberInt = 
                    !string.IsNullOrEmpty(parentIndexNumber) && int.TryParse(parentIndexNumber, out var parsedParentIndex) ? (int?)parsedParentIndex : null;
                int? indexNumberInt = 
                    !string.IsNullOrEmpty(indexNumber) && int.TryParse(indexNumber, out var parsedIndex) ? (int?)parsedIndex : null;

                if (mediaType == MediaType.Movie)
                {
                    //_logger.LogInformation($"Movie deleted, splitting versions: {name} ({productionYearInt})");
                    await _mergeVersionsManager.SplitMoviesAsync(name, productionYearInt, null);
                }
                else if (mediaType == MediaType.Episode)
                {
                    //_logger.LogInformation($"Episode deleted, splitting versions: {seriesName}: S{parentIndexNumberInt} E{indexNumberInt} - {name} ({productionYearInt})");
                    await _mergeVersionsManager.SplitEpisodesAsync(name, productionYearInt, seriesName, parentIndexNumberInt, indexNumberInt, null);
                }
            }
            catch (TaskCanceledException){ }
            finally
            {
                _semaphore.Release();
                _processingSplitItems.TryRemove(key, out _);
            }
        }

        private async void OnLibraryManagerItemUpdated(object sender, ItemChangeEventArgs e)
        {
            if (e.Item.LocationType == LocationType.Virtual)
            {
                return;
            }

            string name;
            string productionYear = string.Empty;
            string seriesName = string.Empty;
            string parentIndexNumber = string.Empty;
            string indexNumber = string.Empty;
            MediaType mediaType;

            if (e.Item is Movie movie && !string.IsNullOrEmpty(movie.Name)) 
            {
                name = movie.Name;
                productionYear = movie.ProductionYear != null ? movie.ProductionYear.ToString() : string.Empty;
                mediaType = MediaType.Movie;

                //_logger.LogInformation($"New Movie added: {name} ({productionYear})");
            }
            else if (e.Item is Episode episode && !string.IsNullOrEmpty(episode.Name)) 
            {
                name = episode.Name;
                productionYear = episode.ProductionYear != null ? episode.ProductionYear.ToString() : string.Empty;
                seriesName = episode.SeriesName;
                parentIndexNumber = episode.ParentIndexNumber.ToString();
                indexNumber = episode.IndexNumber.ToString();
                mediaType = MediaType.Episode;

                //_logger.LogInformation($"New Episode added: {name} ({int.Parse(productionYear)})");
            }
            else 
            {
                return;
            }

            await MergeUpdatedItem(name, productionYear, seriesName, parentIndexNumber, indexNumber, mediaType);
        }

        private async Task MergeUpdatedItem(
            string name, string productionYear, string seriesName, string parentIndexNumber, string indexNumber, MediaType mediaType)
        {
            string key = $"{name}-{productionYear}-{seriesName}-{parentIndexNumber}-{indexNumber}-{mediaType}";
            if (!_processingMergeItems.TryAdd(key, true))
            {
                return;
            }

            await _semaphore.WaitAsync();
            try
            {
                int? productionYearInt = 
                    !string.IsNullOrEmpty(productionYear) && int.TryParse(productionYear, out var parsedProdYear) ? (int?)parsedProdYear : null;
                int? parentIndexNumberInt = 
                    !string.IsNullOrEmpty(parentIndexNumber) && int.TryParse(parentIndexNumber, out var parsedParentIndex) ? (int?)parsedParentIndex : null;
                int? indexNumberInt = 
                    !string.IsNullOrEmpty(indexNumber) && int.TryParse(indexNumber, out var parsedIndex) ? (int?)parsedIndex : null;

                if (mediaType == MediaType.Movie)
                {
                    //_logger.LogInformation($"Searching versions for Movie: {name} ({productionYearInt})");
                    await _mergeVersionsManager.MergeMoviesAsync(name, productionYearInt, null);
                }
                else if (mediaType == MediaType.Episode)
                {
                    //_logger.LogInformation($"Searching versions for Episode: {seriesName}: S{parentIndexNumberInt} E{indexNumberInt} - {name} ({productionYearInt})");
                    await _mergeVersionsManager.MergeEpisodesAsync(name, productionYearInt, seriesName, parentIndexNumberInt, indexNumberInt, null);
                }
            }
            catch (TaskCanceledException){ }
            finally
            {                
                _semaphore.Release();

                await Task.Delay(TimeSpan.FromMilliseconds(5000));
                _processingMergeItems.TryRemove(key, out _);
            }
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            // Subscribe to the library's item added and removed event
            _libraryManager.ItemUpdated += OnLibraryManagerItemUpdated;
            _libraryManager.ItemRemoved += OnLibraryManagerItemRemoved;

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            // Unsubscribe to the library's item added and removed event
            _libraryManager.ItemUpdated -= OnLibraryManagerItemUpdated;
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
