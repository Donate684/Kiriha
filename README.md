# Kiriha
[![Release](https://img.shields.io/github/v/release/Donate684/kiriha?include_prereleases&style=flat-square)](https://github.com/Donate684/kiriha/releases) [![Build Status](https://img.shields.io/github/actions/workflow/status/Donate684/kiriha/release.yml?style=flat-square)](https://github.com/Donate684/kiriha/actions) [![.NET](https://img.shields.io/badge/-11.0-512bd4?style=flat-square&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/) [![Platform](https://img.shields.io/badge/Platform-Windows%20(x64)-0078D6?style=flat-square&logo=windows&logoColor=white)](https://github.com/Donate684/kiriha/releases) [![License](https://img.shields.io/badge/License-GPLv3-blue.svg?style=flat-square)](LICENSE) [![Downloads](https://img.shields.io/github/downloads/Donate684/kiriha/total?style=flat-square)](https://github.com/Donate684/kiriha/releases)

Indispensable tool for those who care more about meticulously tracking “Chinese cartoons” on MyAnimeList than actually watching them. Maniacally parses schedules and auto-inflates the watch counter. Delegate your degeneracy to technology!

## Features

*   **Modern Fluent UI and Offline Mode:** Fast interface powered by **Avalonia UI 12** and .NET 11.
*   **Franchise Chronology:** Visual timeline for every title.
*   **Release Map & Schedule:** Intuitive seasonal calendar with countdown timers until new episodes air.
*   **Mirrored Sync:** Support for **MyAnimeList** and two versions of **Shikimori**.
*   **Seasonal List:** A convenient seasonal overview tracking upcoming sequels and new releases.
*   **Torrent Aggregator:** Built-in search and **Nyaa** release RSS feed with automatic episode matching and one-click magnet link launching.
*   **Discord Rich Presence:** Automatically shares the currently watched title, poster, episode number, and elapsed time to your **Discord** status.
*   **Automatic Scrobbling:** The background **Anisthesia** engine and **AnitomySharp** parser recognize titles and episode numbers on the fly from files and Windows media sessions without a single click.
*   **Built-in Player:** Native engine powered by **libmpv**.

## Requirements

*   **Operating System:** Windows 11 (Windows 10 supports too, but i can't help you with visual bugs).

## Installation
1. On Windows go to release page (https://github.com/Donate684/Kiriha/releases) download ```Kiriha-win-Setup.exe``` and run.

## Build from Source

### Prerequisites

*   [.NET 11.0 SDK](https://dotnet.microsoft.com/download) (Preview) or later
*   **Windows 10/11 (x64)**
*   **libmpv:**
    *   **Windows:** Automatically downloaded via script, or placed in `subprojects/mpv/`

---

### Quick Build

#### Windows:
Run the automated build script (downloads latest `libmpv`, compiles solution, and executes unit tests):
```build.bat```

### Manual Build

#### Windows:
1. Clone the repository:
```
git clone https://github.com/Donate684/kiriha.git
```
```
cd kiriha
```

2. Download libmpv binaries:
```
powershell -ExecutionPolicy Bypass -File scripts\download-mpv.ps1
```

3. Compile the solution:
```
dotnet build Kiriha.sln -c Release
```

4. Run unit tests:
```
dotnet test Tests/Kiriha.Tests/Kiriha.Tests.csproj -c Release
```

5. Run Kiriha:
```
dotnet run --project src/Kiriha/Kiriha.csproj -c Release
```

### Publishing & Packaging

1. To create a self-contained, optimized standalone release (with ReadyToRun AOT):
```
dotnet publish src/Kiriha/Kiriha.csproj -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=true -o ./publish
```

2. (Optional) Generate Installer with Velopack:

```
dotnet tool install -g vpk
```
```
vpk pack --packId Kiriha --packVersion 1.0.0 --packDir ./publish --mainExe Kiriha.exe --icon ./src/Kiriha/Assets/kiriha.ico
```

## License

This project is licensed under the [GNU General Public License v3.0](LICENSE).