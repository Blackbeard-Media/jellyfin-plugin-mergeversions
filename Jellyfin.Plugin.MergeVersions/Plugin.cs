using System;
using System.Collections.Generic;
using Jellyfin.Plugin.MergeVersions.Configuration;
using MediaBrowser.Controller;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;

namespace Jellyfin.Plugin.MergeVersions
{
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages 
    {
        private readonly ILoggerFactory _loggerFactory;

        public Plugin(IServerApplicationPaths appPaths, IXmlSerializer xmlSerializer, ILibraryManager libraryManager, ILoggerFactory loggerFactory, IFileSystem fileSystem)
            : base(appPaths, xmlSerializer)
        {
            Instance = this;
            _loggerFactory = loggerFactory;

            // Create an instance of MergeVersionsListener
            var mergeVersionsManagerLogger = loggerFactory.CreateLogger<MergeVersionsManager>(); 
            var _mergeVersionsManager = new MergeVersionsManager(libraryManager, mergeVersionsManagerLogger, fileSystem);
            var mergeVersionsListenerLogger = loggerFactory.CreateLogger<MergeVersionsListener>(); 
            new MergeVersionsListener(libraryManager, _mergeVersionsManager, mergeVersionsListenerLogger);
        }

        public override string Name => "Merge Versions by BBM (Beta)";

        public static Plugin Instance { get; private set; }

        public override string Description
            => "Merge Versions by BBM (Beta)";

        public PluginConfiguration PluginConfiguration => Configuration;

        private readonly Guid _id = new Guid("bba04227-c153-41f2-9ea2-83ed646d2da4");
        public override Guid Id => _id;

        public IEnumerable<PluginPageInfo> GetPages()
        {
            return new[]
            {
                new PluginPageInfo
                {
                    Name = "Merge Versions by BBM (Beta)",
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.configurationpage.html"
                }
            };
        }
    }
}
