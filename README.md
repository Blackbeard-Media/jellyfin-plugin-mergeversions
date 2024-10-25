<h1 align="center">Jellyfin Merge Versions Plugin by BBM</h1>
<h3 align="center">Part of the <a href="https://jellyfin.org">Jellyfin Project</a></h3>

<p align="center">
Jellyfin Merge Versions plugin is a plugin that automatically groups every repeated movie and episode

</p>

## Install Process


## From Repository
1. In Jellyfin, go to Dashboard -> Plugins -> Repositories -> Add and paste this Link https://raw.githubusercontent.com/Blackbeard-Media/jellyfin-plugin-manifest/master/manifest.json
2. o to Catalog and search for the Plugin you want to install
3. Click on it and install
4. Restart Jellyfin


## From .zip file
1. Download the .zip file from release page
2. Extract it and place the .dll file in a folder called ```plugins/Merge Versions``` under  the program data directory or inside the portable install directory
3. Restart Jellyfin

## User Guide
1. To merge your movies or episodes you can do it from Schedule task or directly from the configuration of the plugin.
2. Spliting is only avaible through the configuration



## Build Process
1. Clone or download this repository
2. Ensure you have .NET Core SDK setup and installed
3. Build plugin with following command.
```sh
dotnet publish --configuration Release --output bin
```
4. Place the resulting .dll file in a folder called ```plugins/Merge versions``` under  the program data directory or inside the portable install directory


