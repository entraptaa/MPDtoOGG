# MPDtoOGG
![GitHub Release](https://img.shields.io/github/v/release/entraptaa/MPDtoOGG)
![GitHub Downloads (all assets, all releases)](https://img.shields.io/github/downloads/entraptaa/MPDtoOGG/total)
[![Discord](https://discord.com/api/guilds/582304739815981140/widget.png?style=shield)](https://discord.gg/GvHfG33)

-----------------

MPDtoOGG is for extracting Fortnite Festival Jam Track Previews from their [internal API](https://fortnitecontent-website-prod07.ol.epicgames.com/content/api/pages/fortnite-game/spark-tracks).

> [!IMPORTANT]
> We take no responsibility for the inproper use of this program. Epic Games does not tolerate the possibility of having the full songs that are NOT purchased in the game's official Item Shop. (being violation of the [EULA](https://store.epicgames.com/en-US/eula)).

-----------------

### Requirements

- [NET 9.0 Runtime or higher](https://dotnet.microsoft.com/en-us/download/dotnet/9.0/runtime)
- [FFmpeg](https://www.ffmpeg.org/download.html)

-----------------

### Usage

```
MPDtoOGG.exe --pid <pid> (required) --out <output dir> (optional)
```
-----------------


### Download the application

You can download the application pre-compiled from our [releases](https://github.com/entraptaa/MPDtoOGG/releases).

### Build via command line

1. Clone the Source Code
```git
git clone https://github.com/entraptaa/MPDtoOGG
```

2. Build the program
```
cd MPDtoOGG
dotnet publish MPDtoOGG -c Release --no-self-contained -p:PublishReadyToRun=false -p:PublishSingleFile=true -p:DebugType=None -p:GenerateDocumentationFile=false -p:DebugSymbols=false
```

## Thanks To
[FNLookup](https://github.com/FNLookup/data) - Having documentation for obtaining these MPDs.
[Athena](https://github.com/djlorenzouasset/Athena) - "Inspiration" for this Readme. ***(Athena is an amazing program to get latest Fortnite cosmetics on your Private Server profile and Item Shop)***