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
        private readonly ILogger<MergeVersionsManager> _logger;
        private readonly SessionInfo _session;
        private readonly IFileSystem _fileSystem;

        public MergeVersionsManager(
            ILibraryManager libraryManager,
            ILogger<MergeVersionsManager> logger,
            IFileSystem fileSystem)
        {
            _libraryManager = libraryManager;
            _logger = logger;
            _fileSystem = fileSystem;
        }

        public async Task MergeMoviesAsync(String name, int? productionYear, IProgress<double> progress)
        {
            if (name != null)
            {
                _logger.LogInformation($"Scanning for repeated movies: {name} ({productionYear})");
            } 
            else
            {
                _logger.LogInformation("Scanning for repeated movies");
            }

            var duplicateMovies = GetMoviesFromLibrary()
                .Where(x => 
                    (string.IsNullOrEmpty(name) || x.Name == name) &&
                    (productionYear == null || x.ProductionYear == productionYear)
                    )
                .GroupBy(x => x.ProviderIds["Tmdb"])
                .Where(x => x.Count() > 1)
                .ToList();
            //_logger.LogInformation($"Movies to process: {duplicateMovies.Count}");

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

        public async Task SplitMoviesAsync(String name, int? productionYear, IProgress<double> progress)
        {
            var movies = GetMoviesFromLibrary()
                .Where(x => 
                    (string.IsNullOrEmpty(name) || x.Name == name) &&
                    (productionYear == null || x.ProductionYear == productionYear)
                    )
                .ToList();
            
            var current = 0;
            foreach (var m in movies)
            {
                current++;
                var percent = current / (double)movies.Count * 100;
                progress?.Report((int)percent);

                _logger.LogInformation($"Splitting Movie: {m.Name} ({m.ProductionYear})");
                Stopwatch stopwatch = new Stopwatch();
                stopwatch.Start();
                await DeleteAlternateSources(m.Id);
                stopwatch.Stop();
                _logger.LogInformation($"DeleteAlternateSources Execution Time : {stopwatch.ElapsedMilliseconds} ms");                
            }
            progress?.Report(100);
        }

        public async Task MergeEpisodesAsync(
            String name, int? productionYear, string seriesName, int? parentIndexNumber, int? indexNumber, IProgress<double> progress)
        {   
            if (name != null)
            {
                _logger.LogInformation($"Scanning for repeated episodes on: {seriesName}: S{parentIndexNumber} E{indexNumber} - {name} ({productionYear})");
            } 
            else
            {
                _logger.LogInformation("Scanning for repeated episodes");
            }

            var duplicateEpisodes = GetEpisodesFromLibrary()
                .Where(x => 
                    (string.IsNullOrEmpty(name) || x.Name == name) &&
                    (productionYear == null || x.ProductionYear == productionYear) &&
                    (string.IsNullOrEmpty(seriesName) || x.SeriesName == seriesName) &&
                    (parentIndexNumber == null || x.ParentIndexNumber == parentIndexNumber) &&
                    (indexNumber == null || x.IndexNumber == indexNumber)
                    )
                .GroupBy(x => x.ProviderIds["Tvdb"])
                .Where(x => x.Count() > 1)
                .ToList();
            //_logger.LogInformation($"Episodes to process: {duplicateEpisodes.Count}");

            var current = 0;
            foreach (var e in duplicateEpisodes)
            {
                current++;
                var percent = current / (double)duplicateEpisodes.Count * 100;
                progress?.Report((int)percent);
                _logger.LogInformation(
                    $"Merging {e.ElementAt(0).SeriesName}: S{e.ElementAt(0).ParentIndexNumber} E{e.ElementAt(0).IndexNumber} - {e.ElementAt(0).Name} ({e.ElementAt(0).ProductionYear})"
                );
                Stopwatch stopwatch = new Stopwatch();
                stopwatch.Start();
                await MergeVersions(e.Select(e => e.Id).ToList());
                stopwatch.Stop();
                _logger.LogInformation($"MergeVersions Execution Time : {stopwatch.ElapsedMilliseconds} ms");
            }
            progress?.Report(100);
        }

        public async Task SplitEpisodesAsync(
            String name, int? productionYear, string seriesName, int? parentIndexNumber, int? indexNumber, IProgress<double> progress)
        {
            var episodes = GetEpisodesFromLibrary()
                .Where(x => 
                    (string.IsNullOrEmpty(name) || x.Name == name) &&
                    (productionYear == null || x.ProductionYear == productionYear) &&
                    (string.IsNullOrEmpty(seriesName) || x.SeriesName == seriesName) &&
                    (parentIndexNumber == null || x.ParentIndexNumber == parentIndexNumber) &&
                    (indexNumber == null || x.IndexNumber == indexNumber)
                    )
                .ToList();
            
            var current = 0;
            foreach (var e in episodes)
            {
                current++;
                var percent = current / (double)episodes.Count * 100;
                progress?.Report((int)percent);

                _logger.LogInformation($"Splitting Episode: {e.SeriesName}. S{e.ParentIndexNumber} E{e.IndexNumber}, {e.Name} ({e.ProductionYear})");
                Stopwatch stopwatch = new Stopwatch();
                stopwatch.Start();
                await DeleteAlternateSources(e.Id);
                stopwatch.Stop();
                _logger.LogInformation($"DeleteAlternateSources Execution Time : {stopwatch.ElapsedMilliseconds} ms");
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
                .Where(IsEligible)
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
                        HasTvdbId = true,
                    }
                )
                .Select(m => m as Episode)
                .Where(IsEligible)
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

            var primaryVersion = items
                .OrderBy(i =>
                {
                    if (i.Video3DFormat.HasValue || i.VideoType != VideoType.VideoFile)
                    {
                        return 1;
                    }
                    return 0;
                })
                .ThenByDescending(i => i.GetDefaultVideoStream()?.Width ?? 0)
                .ThenByDescending(i => i.GetDefaultVideoStream()?.BitRate ?? 0)
                .First();

            //_logger.LogInformation($"Got primaryVersion: {primaryVersion.Id.ToString("N", CultureInfo.InvariantCulture)}");
            //_logger.LogInformation($"primaryVersion.Path: {primaryVersion.Path}");

            var alternateVersionsOfPrimary = primaryVersion
                .LinkedAlternateVersions.Where(l => items.Any(i => i.Path == l.Path))
                .ToList();
            //_logger.LogInformation($"Got previous linked alternateVersionsOfPrimary: {string.Join(", ", alternateVersionsOfPrimary.Select(l => ((Guid)l.ItemId).ToString("N", CultureInfo.InvariantCulture)))}");

            foreach (var item in items.Where(i => !i.Id.Equals(primaryVersion.Id)))
            {
                var originalItem = item;

                //_logger.LogInformation($"Currently processing item.Id: {item.Id.ToString("N", CultureInfo.InvariantCulture)}");
                //_logger.LogInformation($"item.Path: {item.Path}");

                //_logger.LogInformation($"item.PrimaryVersionId: {item.PrimaryVersionId} vs. primaryVersion.Id: {primaryVersion.Id.ToString("N", CultureInfo.InvariantCulture)}");
                // Only update if the PrimaryVersionId has been changed
                if (!string.Equals(item.PrimaryVersionId, primaryVersion.Id.ToString("N", CultureInfo.InvariantCulture)))
                {
                    item.SetPrimaryVersionId(primaryVersion.Id.ToString("N", CultureInfo.InvariantCulture));
                    await item.UpdateToRepositoryAsync(
                        ItemUpdateType.MetadataEdit,
                        CancellationToken.None
                    )
                    .ConfigureAwait(false);
                }

                if (
                    !alternateVersionsOfPrimary.Any(i =>
                        string.Equals(i.Path, item.Path, StringComparison.OrdinalIgnoreCase)
                    )
                )
                {
                    alternateVersionsOfPrimary.Add(
                        new LinkedChild { Path = item.Path, ItemId = item.Id }
                    );
                }
                //_logger.LogInformation($"alternateVersionsOfPrimary: {string.Join(", ", alternateVersionsOfPrimary.Select(l => ((Guid)l.ItemId).ToString("N", CultureInfo.InvariantCulture)))}");

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

                //_logger.LogInformation($"item.LinkedAlternateVersions.Length: {item.LinkedAlternateVersions.Length}");
                if (item.LinkedAlternateVersions.Length > 0)
                {
                    //_logger.LogInformation($"originalItem.LinkedAlternateVersions: {string.Join(", ", originalItem.LinkedAlternateVersions.Select(l => l.ItemId))} vs. item.LinkedAlternateVersions: {string.Join(", ", item.LinkedAlternateVersions.Select(l => l.ItemId))}");
                    if (!originalItem.LinkedAlternateVersions.SequenceEqual(item.LinkedAlternateVersions))
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

            //_logger.LogInformation($"alternateVersionsOfPrimary: {string.Join(", ", alternateVersionsOfPrimary.Select(l => l.ItemId))} vs. primaryVersion.LinkedAlternateVersions: {string.Join(", ", primaryVersion.LinkedAlternateVersions.Select(l => l.ItemId))} ");
            // Update primary version's LinkedAlternateVersions only if there are changes
            if (!primaryVersion.LinkedAlternateVersions.SequenceEqual(alternateVersionsOfPrimary))
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

        private bool IsEligible(BaseItem item)
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

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                _session?.DisposeAsync();
            }
        }
    }
}
