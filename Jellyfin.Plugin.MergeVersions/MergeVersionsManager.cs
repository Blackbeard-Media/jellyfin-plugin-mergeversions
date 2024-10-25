using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MergeVersions
{
    public class MergeVersionsManager : IDisposable
    {
        private readonly ILibraryManager _libraryManager;
        private readonly Timer _timer;
        private readonly ILogger<MergeVersionsManager> _logger; // TODO logging
        private readonly SessionInfo _session;
        private readonly IFileSystem _fileSystem;

        public MergeVersionsManager(
            ILibraryManager libraryManager,
            ILogger<MergeVersionsManager> logger,
            IFileSystem fileSystem
        )
        {
            _libraryManager = libraryManager;
            _logger = logger;
            _fileSystem = fileSystem;
            _timer = new Timer(_ => OnTimerElapsed(), null, Timeout.Infinite, Timeout.Infinite);
        }

        public async Task MergeMoviesAsync(IProgress<double> progress)
        {
            _logger.LogInformation("Scanning for repeated movies");

            var duplicateMovies = GetMoviesFromLibrary()
                .GroupBy(x => x.ProviderIds["Tmdb"])
                .Where(x => x.Count() > 1)
                .ToList();

            _logger.LogInformation($"Movies to process: {duplicateMovies.Count}");

            var current = 0;
            foreach (var m in duplicateMovies)
                {
                    current++;
                    var percent = current / (double)duplicateMovies.Count * 100;
                    progress?.Report((int)percent);
                    _logger.LogInformation(
                        $"Merging {m.ElementAt(0).Name} ({m.ElementAt(0).ProductionYear})"
                    );
                    Stopwatch stopwatch = new Stopwatch();
                    stopwatch.Start();
                    await MergeVersions(m.Select(m => m.Id).ToList());
                    stopwatch.Stop();
                    _logger.LogInformation($"MergeVersions Execution Time: {stopwatch.ElapsedMilliseconds} ms");
                }
            progress?.Report(100);
        }

        public void SplitMovies(IProgress<double> progress)
        {
            var movies = GetMoviesFromLibrary();
            var current = 0;
            Parallel.ForEach(
                movies,
                async m =>
                {
                    current++;
                    var percent = current / (double)movies.Count * 100;
                    progress?.Report((int)percent);

                    _logger.LogInformation($"Spliting {m.Name} ({m.ProductionYear})");
                    await DeleteAlternateSources(m.Id);
                }
            );
            progress?.Report(100);
        }

        public async Task MergeEpisodesAsync(IProgress<double> progress)
        {
            _logger.LogInformation("Scanning for repeated episodes");

            var duplicateEpisodes = GetEpisodesFromLibrary()
                .GroupBy(x => new
                {
                    x.SeriesName,
                    x.SeasonName,
                    x.Name,
                    x.IndexNumber,
                    x.ProductionYear
                })
                .Where(x => x.Count() > 1)
                .ToList();

            _logger.LogInformation($"Episodes to process: {duplicateEpisodes.Count}");

            var current = 0;
            foreach (var e in duplicateEpisodes)
            {
                current++;
                var percent = current / (double)duplicateEpisodes.Count * 100;
                progress?.Report((int)percent);
                _logger.LogInformation(
                    $"Merging {e.ElementAt(0).SeriesName} - {e.ElementAt(0).Name} ({e.ElementAt(0).ProductionYear})"
                );
                Stopwatch stopwatch = new Stopwatch();
                stopwatch.Start();
                await MergeVersions(e.Select(e => e.Id).ToList());
                stopwatch.Stop();
                _logger.LogInformation($"MergeVersions Execution Time : {stopwatch.ElapsedMilliseconds} ms");
            }
            progress?.Report(100);
        }

        public async Task SplitEpisodesAsync(IProgress<double> progress)
        {
            var episodes = GetEpisodesFromLibrary();
            var current = 0;

            foreach (var e in episodes)
            {
                current++;
                var percent = current / (double)episodes.Count * 100;
                progress?.Report((int)percent);

                _logger.LogInformation($"Spliting {e.IndexNumber} ({e.SeriesName})");
                await DeleteAlternateSources(e.Id);
            }
            progress?.Report(100);
        }

        private List<Movie> GetMoviesFromLibrary()
        {
            return _libraryManager
                .GetItemList(
                    new InternalItemsQuery
                    {
                        IncludeItemTypes = [BaseItemKind.Movie],
                        IsVirtualItem = false,
                        Recursive = true,
                        HasTmdbId = true,
                    }
                )
                .Select(m => m as Movie)
                .Where(IsElegible)
                .ToList();
        }

        private List<Episode> GetEpisodesFromLibrary()
        {
            return _libraryManager
                .GetItemList(
                    new InternalItemsQuery
                    {
                        IncludeItemTypes = [BaseItemKind.Episode],
                        IsVirtualItem = false,
                        Recursive = true,
                    }
                )
                .Select(m => m as Episode)
                .Where(IsElegible)
                .ToList();
        }

        private async Task MergeVersions(List<Guid> ids)
        {
            var items = ids.Select(i => _libraryManager.GetItemById<BaseItem>(i, null))
                .OfType<Video>()
                .OrderBy(i => i.Id)
                .ToList();

            _logger.LogInformation($"Items to merge: {items.Count}");

            if (items.Count < 2)
            {
                return;
            }

            var primaryVersion = items.FirstOrDefault(i =>
                i.MediaSourceCount > 1 && string.IsNullOrEmpty(i.PrimaryVersionId)
            );
            if (primaryVersion is null)
            {
                primaryVersion = items
                    .OrderBy(i =>
                    {
                        if (i.Video3DFormat.HasValue || i.VideoType != VideoType.VideoFile)
                        {
                            return 1;
                        }

                        return 0;
                    })
                    .ThenByDescending(i => i.GetDefaultVideoStream()?.Width ?? 0)
                    .First();
            }
            _logger.LogInformation($"Got primaryVersion: {primaryVersion.Id}");

            var alternateVersionsOfPrimary = primaryVersion
                .LinkedAlternateVersions.Where(l => items.Any(i => i.Path == l.Path))
                .ToList();
            _logger.LogInformation($"Got previous linked alternateVersionsOfPrimary: {string.Join(", ", alternateVersionsOfPrimary.Select(l => l.Id))}");

            foreach (var item in items.Where(i => !i.Id.Equals(primaryVersion.Id)))
            {
                var originalItem = item;

                 _logger.LogInformation($"Currently processing item.Id: {item.Id}");


                item.SetPrimaryVersionId(
                    primaryVersion.Id.ToString("N", CultureInfo.InvariantCulture)
                );

                _logger.LogInformation($"item.PrimaryVersionId: {originalItem.PrimaryVersionId} vs. originalItem.PrimaryVersionId: {originalItem.PrimaryVersionId}");
                // Only update if the PrimaryVersionId has changed
                if (!string.Equals(item.PrimaryVersionId, originalItem.PrimaryVersionId))
                {
                    await item.UpdateToRepositoryAsync(
                        ItemUpdateType.MetadataEdit,
                        CancellationToken.None
                    )
                    .ConfigureAwait(false);
                }

                var linkedVersionsOfSecondary = item.LinkedAlternateVersions.ToList();
                _logger.LogInformation($"linkedVersionsOfSecondary: {string.Join(", ", linkedVersionsOfSecondary.Select(l => l.Id))}");
                _logger.LogInformation($"item.Path: {item.Path}");
                if (
                    !alternateVersionsOfPrimary.Any(i =>
                        string.Equals(i.Path, item.Path, StringComparison.OrdinalIgnoreCase)
                    )
                )
                {
                    alternateVersionsOfPrimary.Add(
                        new LinkedChild { Path = item.Path, ItemId = item.Id }
                    );
                    linkedVersionsOfSecondary.Add(
                        new LinkedChild { Path = primaryVersion.Path, ItemId = primaryVersion.Id }
                    );
                        
                }
                _logger.LogInformation($"Final alternateVersionsOfPrimary: {string.Join(", ", alternateVersionsOfPrimary.Select(l => l.Id))}");
                _logger.LogInformation($"Final linkedVersionsOfSecondary: {string.Join(", ", linkedVersionsOfSecondary.Select(l => l.Id))}");
                
                foreach (var linkedItem in item.LinkedAlternateVersions)
                {
                    if (
                        !alternateVersionsOfPrimary.Any(i =>
                            string.Equals(
                                i.Path,
                                linkedItem.Path,
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                    )
                    {
                        alternateVersionsOfPrimary.Add(linkedItem);
                    }
                }

                _logger.LogInformation($"linkedVersionsOfSecondary.Count: {linkedVersionsOfSecondary.Count}");
                if (linkedVersionsOfSecondary.Count > 0)
                {
                    _logger.LogInformation($"originalItem.LinkedAlternateVersions: {string.Join(", ", originalItem.LinkedAlternateVersions.Select(l => l.Id))} vs. linkedVersionsOfSecondary: {string.Join(", ", linkedVersionsOfSecondary.Select(l => l.Id))}");
                    if (!originalItem.LinkedAlternateVersions.ToArray().SequenceEqual(linkedVersionsOfSecondary.ToArray()))
                    {
                        // Clear LinkedAlternateVersions if there were changes
                        item.LinkedAlternateVersions = [];
                        await item.UpdateToRepositoryAsync(
                            ItemUpdateType.MetadataEdit,
                            CancellationToken.None
                        )
                        .ConfigureAwait(false);
                    }
                }
            }

            // Update primary version's LinkedAlternateVersions only if there are changes
            if (!primaryVersion.LinkedAlternateVersions.ToArray().SequenceEqual(alternateVersionsOfPrimary.ToArray()))
            {
                primaryVersion.LinkedAlternateVersions = alternateVersionsOfPrimary.ToArray();
                await primaryVersion.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, CancellationToken.None).ConfigureAwait(false);
            }
        }

        private async Task DeleteAlternateSources(Guid itemId)
        {
            var item = _libraryManager.GetItemById<Video>(itemId);
            if (item is null)
            {
                return;
            }

            if (item.LinkedAlternateVersions.Length == 0 && item.PrimaryVersionId != null)
            {
                item = _libraryManager.GetItemById<Video>(Guid.Parse(item.PrimaryVersionId));
            }

            if (item is null)
            {
                return;
            }

            foreach (var link in item.GetLinkedAlternateVersions())
            {
                link.SetPrimaryVersionId(null);
                link.LinkedAlternateVersions = [];

                await link.UpdateToRepositoryAsync(
                        ItemUpdateType.MetadataEdit,
                        CancellationToken.None
                    )
                    .ConfigureAwait(false);
            }

            item.LinkedAlternateVersions = [];
            item.SetPrimaryVersionId(null);
            await item.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, CancellationToken.None)
                .ConfigureAwait(false);
        }

        private bool IsElegible(BaseItem item)
        {
            if (
                Plugin.Instance.PluginConfiguration.LocationsExcluded != null
                && Plugin.Instance.PluginConfiguration.LocationsExcluded.Any(s =>
                    _fileSystem.ContainsSubPath(s, item.Path)
                )
            )
            {
                return false;
            }
            return true;
        }

        private void OnTimerElapsed() { }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                _timer?.Dispose();
                _session?.DisposeAsync();
            }
        }
    }
}
