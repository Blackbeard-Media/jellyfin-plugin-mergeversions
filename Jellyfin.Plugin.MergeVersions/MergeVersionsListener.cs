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
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MergeVersions
{
    public class MergeVersionsListener : IHostedService, IDisposable
    {
        private readonly ILibraryManager _libraryManager;
        private readonly ITaskManager _taskManager;
        private readonly MergeVersionsManager _mergeVersionsManager;
        private readonly ILogger<MergeVersionsListener> _logger;

        private CancellationTokenSource _mergeCancellationTokenSource;
        private bool _mergeScheduled = false;
        private bool _mergeInProgress = false;
        private static readonly object _mergeLock = new object();
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(10);
        
        private enum MediaType
        {
            Movie,
            Episode
        }

        public MergeVersionsListener(
            ILibraryManager libraryManager, 
            ITaskManager taskManager,
            MergeVersionsManager mergeVersionsManager, 
            ILogger<MergeVersionsListener> logger)
        {
            _libraryManager = libraryManager;
            _taskManager = taskManager;
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
               
            ScheduleMerge();
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

        private void OnLibraryManagerItemAdded(object sender, ItemChangeEventArgs e)
        {
            if (!(e.Item is Movie) && !(e.Item is Episode) || e.Item.LocationType == LocationType.Virtual)
            {
                return;
            }

            ScheduleMerge();
        }

        private void ScheduleMerge()
        {
            lock (_mergeLock)
            {
                if (_mergeScheduled || _mergeInProgress)
                {
                    return;
                }

                _mergeScheduled = true;
                _mergeCancellationTokenSource = new CancellationTokenSource();
                _ = MergeItemsAsync(_mergeCancellationTokenSource.Token);
            }
        }


        private async Task MergeItemsAsync(CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(60), cancellationToken).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                lock (_mergeLock)
                {
                    _mergeScheduled = false;
                }
                return;
            }

            lock (_mergeLock)
            {
                _mergeScheduled = false;

                if (_mergeInProgress)
                {
                    return;
                }
                _mergeInProgress = true;
            }

            try
            {
                //await _mergeVersionsManager.MergeMoviesAsync(null, null, false, null);
                //await _mergeVersionsManager.MergeEpisodesAsync(null, null, null, null, null, false, null);  

                foreach (var taskName in new[] { "Merge All Movies", "Merge All Episodes" })
                {
                    var task = _taskManager.ScheduledTasks.FirstOrDefault(t => t.Name == taskName);
                    if (task != null)
                    {
                        await _taskManager.Execute(task, new TaskOptions());
                    }
                }
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

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _libraryManager.ItemAdded += OnLibraryManagerItemAdded;
            _libraryManager.ItemRemoved += OnLibraryManagerItemRemoved;

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
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
