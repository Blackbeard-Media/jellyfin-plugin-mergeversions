using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.IO;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MergeVersions.Api
{
    /// <summary>
    /// The Merge Versions api controller.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("MergeVersions")]
    [Produces(MediaTypeNames.Application.Json)]
    public class MergeVersionsController : ControllerBase
    {
        private readonly MergeVersionsManager _mergeVersionsManager;
        private readonly ILogger<MergeVersionsManager> _logger;

        /// <summary>
        /// Initializes a new instance of <see cref="TMDbBoxSetsController"/>.

        public MergeVersionsController(
            ILibraryManager libraryManager,
            ILogger<MergeVersionsManager> logger,
            IFileSystem fileSystem
        )
        {
            _mergeVersionsManager = new MergeVersionsManager(libraryManager, logger, fileSystem);

            _logger = logger;
        }

        /// <summary>
        /// Scans all movies and merges repeated ones.
        /// </summary>
        /// <reponse code="204">Library scan and merge started successfully. </response>
        /// <returns>A <see cref="NoContentResult"/> indicating success.</returns>
        [HttpPost("MergeMovies")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<ActionResult> MergeMoviesRequestAsync()
        {
            _logger.LogInformation("Starting a manual refresh, looking up for repeated versions");
            _ = _mergeVersionsManager.MergeMoviesAsync(null, null, null);
            return NoContent();
        }

        /// <summary>
        /// Scans all movies and splits merged ones.
        /// </summary>
        /// <reponse code="204">Library scan and split started successfully. </response>
        /// <returns>A <see cref="NoContentResult"/> indicating success.</returns>
        [HttpPost("SplitMovies")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<ActionResult> SplitMoviesRequestAsync()
        {
            _logger.LogInformation("Splitting all movies");
            _ = _mergeVersionsManager.SplitMoviesAsync(null, null, false, null);
            return NoContent();
        }

        /// <summary>
        /// Scans all episodes and merge repeated ones.
        /// </summary>
        /// <reponse code="204">Library scan and merge started successfully. </response>
        /// <returns>A <see cref="NoContentResult"/> indicating success.</returns>
        [HttpPost("MergeEpisodes")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<ActionResult> MergeEpisodesRequestAsync()
        {
            _logger.LogInformation("Starting a manual refresh, looking up for repeated versions");
            _ = _mergeVersionsManager.MergeEpisodesAsync(null, null, null, null, null, null);
            return NoContent();
        }

        /// <summary>
        /// Scans all episodes and splits merged ones.
        /// </summary>
        /// <reponse code="204">Library scan and split started successfully. </response>
        /// <returns>A <see cref="NoContentResult"/> indicating success.</returns>
        [HttpPost("SplitEpisodes")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<ActionResult> SplitEpisodesRequestAsync()
        {
            _logger.LogInformation("Splitting all episodes");
            _ = _mergeVersionsManager.SplitEpisodesAsync(null, null, null, null, null, false, null);
            return NoContent();
        }
    }
}
