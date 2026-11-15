# Third-party notices

KickDownloader itself is MIT licensed (see [LICENSE](LICENSE)). It contains and depends on the third-party
components below, each under its own license.

## TwitchDownloader (lay295/TwitchDownloader)

- Source: <https://github.com/lay295/TwitchDownloader>
- Copyright (c) lay295
- License: MIT

The chat rendering engine in `src/KickDownloaderCore/Render/` is copied from the `TwitchDownloaderCore` project
of TwitchDownloader (version 1.56.5 at the time of copying). The full original license text is kept in
[`src/KickDownloaderCore/Render/LICENSE-TwitchDownloader.txt`](src/KickDownloaderCore/Render/LICENSE-TwitchDownloader.txt).
Its additional notice, which must be kept unmodified with all copies and binaries, is in
[`src/KickDownloaderCore/Render/Resources/TD-License`](src/KickDownloaderCore/Render/Resources/TD-License), and the
licenses of the libraries and resources it bundles are listed in
[`src/KickDownloaderCore/Render/Resources/THIRD-PARTY-LICENSES.txt`](src/KickDownloaderCore/Render/Resources/THIRD-PARTY-LICENSES.txt).
`build.ps1` copies this file, `TD-License` and `THIRD-PARTY-LICENSES.txt` next to the published executables.

> The MIT License
>
> Copyright (c) lay295

Changes made to the copied files:

- A `#nullable disable` line was added at the top of each `.cs` file.
- Parts KickDownloader does not use were left out: the Twitch chat/video/clip downloaders (`ChatDownloader`,
  `ChatUpdater`, `VideoDownloader`, `ClipDownloader`, `TsMerger`) with their option classes, and the
  `InterVariable*.ttf` fonts.
- The original `TwitchDownloaderCore.*` namespaces are kept on purpose, so the files stay easy to diff against upstream.

KickDownloader is an independent project. It is not affiliated with, endorsed by, or sponsored by TwitchDownloader
or its authors.

## Resources bundled in `Render/Resources/`

| Resource | License | Source |
|---|---|---|
| Inter fonts (Regular, Bold, Italic, BoldItalic) | SIL Open Font License 1.1 | <https://github.com/rsms/inter> (license: `LICENSE.txt` in that repository) |
| Noto Color Emoji | Apache License 2.0 | <https://github.com/googlefonts/noto-emoji> |
| Twemoji | Graphics: CC BY 4.0, code: MIT | <https://github.com/twitter/twemoji> |

The Noto and Twemoji license texts are included in `THIRD-PARTY-LICENSES.txt` (see above).

## NuGet packages

| Package | License |
|---|---|
| SkiaSharp, SkiaSharp.HarfBuzz, HarfBuzzSharp | MIT |
| xUnit, Microsoft.NET.Test.Sdk, coverlet (tests only, not shipped) | Apache License 2.0 / MIT |

See each package's own page on nuget.org for the license text.

## FFmpeg

FFmpeg is **not** bundled. KickDownloader runs an `ffmpeg` executable that you install yourself
(<https://ffmpeg.org/>, LGPL or GPL depending on the build you choose).
