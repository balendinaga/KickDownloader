# KickDownloader

![GitHub Release](https://img.shields.io/github/v/release/balendinaga/KickDownloader?style=flat-square&color=53fc18)
![GitHub Downloads](https://img.shields.io/github/downloads/balendinaga/KickDownloader/total?style=flat-square)
![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-blue?style=flat-square)
![License](https://img.shields.io/github/license/balendinaga/KickDownloader?style=flat-square)

Download [Kick](https://kick.com) VODs, clips and chat history, and render the chat as a transparent video you can
overlay on your footage. Windows desktop app and command-line tool.

The chat renderer is [TwitchDownloader](https://github.com/lay295/TwitchDownloader)'s `ChatRenderer`, vendored under
its MIT license (see [Credits and licenses](#credits-and-licenses)). Chat is saved in TwitchDownloader's JSON format
(`ChatRoot` v1.4.0), so the files also work with TwitchDownloader's own renderer.

> **Disclaimer.** This project is not affiliated with, endorsed, or sponsored by Kick.
> It uses Kick's public, undocumented web endpoints, which can change or stop working at any time without notice.
> You are responsible for following Kick's Terms of Service and the copyright of the content you download.
> Use it for personal use and with the creators' permission. See [Legal](#legal).

## Features

- **VOD download**: full VOD or a time range, any quality, saved as MP4 (HLS streamed through FFmpeg, no re-encoding).
- <img width="944" height="792" alt="1" src="https://github.com/user-attachments/assets/61c0ac19-60ee-4363-9df9-de3387a97ee1" />

- **Clip download**.
- <img width="937" height="781" alt="2" src="https://github.com/user-attachments/assets/7cb32f44-ecab-4276-9397-3083ea93c7cf" />

- **Chat download**: full VOD or a time range, as TwitchDownloader-compatible JSON. Emote and badge images are embedded.
- <img width="940" height="787" alt="3" src="https://github.com/user-attachments/assets/9736e296-27e4-44bf-b7e7-f17598666a59" />

- **Chat render**: transparent chat video (WebM VP9 alpha, MOV ProRes 4444 alpha) or MP4 with a solid background.
- <img width="938" height="790" alt="4" src="https://github.com/user-attachments/assets/699e1f84-eb23-4c28-ac9b-a42b23793816" />

- Accepts full `kick.com` links or bare ids, including the newer UUIDv7 VOD links.
- Built-in rate-limit handling: spaced requests, automatic backoff on HTTP 429, download resumes where it stopped.
- Desktop app with dark theme, progress bar, cancel button and "open folder", plus a scriptable CLI.
- Cancelled or failed jobs leave no partial files. Existing files are never overwritten.

## Screenshots

Screenshots will be added under `docs/images/`.

<!--
![Download VOD tab](docs/images/vod-tab.png)
![Download Chat tab](docs/images/chat-tab.png)
![Render Chat tab](docs/images/render-tab.png)
-->

## Requirements

| For | You need |
|---|---|
| Running the release `.exe` files | Windows 10/11 x64 and [FFmpeg](https://ffmpeg.org/download.html) (`ffmpeg` on `PATH`, or pick the path in the app / pass `--ffmpeg <path>`). The release builds are self-contained: no .NET install needed. |
| Building from source | [.NET 10 SDK](https://dotnet.microsoft.com/download), Windows (the desktop app is WPF) |

## Download

Get `KickDownloaderWPF.exe` (desktop app) and `kickdl.exe` (command line) from the
[Releases](../../releases) page.

## Build from source

```powershell
git clone <this repository>
cd KickDownloader
.\build.ps1                      # runs the tests, then publishes dist\kickdl.exe and dist\KickDownloaderWPF.exe
.\build.ps1 -FrameworkDependent  # smaller exes that need the .NET 10 (Desktop) runtime
.\build.ps1 -SkipTests
```

Output goes to `dist\`. Run the tests alone with `dotnet test`.

## Desktop app

`KickDownloaderWPF.exe` has four tabs. Every input accepts a link copied from the browser or a bare id.

- **Download VOD**: paste the link, press *Get Info*, pick a quality, optionally enter a start and end time.
- **Download Clip**: downloads the clip. *Send to Download Chat tab* carries the clip's VOD, start offset and length
  to the chat tab in one click.
- **Download Chat**: saves the VOD chat as `.json` (optional time range).
- **Render Chat**: choose a chat `.json`, then width, height, FPS, font size, emote scale and format
  (WebM VP9 transparent, MOV ProRes transparent, MP4).

The status bar shows progress and a **Cancel** button; **Open Folder** appears when a job finishes. If Kick rate-limits
you, the status bar says so and counts down before continuing automatically. The FFmpeg path and output folder are
stored in `%AppData%\KickDownloader\settings.json`. One job runs at a time.

## Command line

Times (`T`) are seconds (`3600`) or `hh:mm:ss` (`01:00:00`). Existing output files are never overwritten.

| Command | What it does |
|---|---|
| `kickdl check [channel]` | Read-only live API check (default channel `xqc`) |
| `kickdl clip <clip id or url> <out.mp4>` | Download a clip and print the matching chat command |
| `kickdl vod <vod link or uuid> <out.mp4> [--start T] [--duration T] [--quality 720p60]` | Download a VOD or part of it |
| `kickdl chat <channel> <vod link or uuid> <out.json> [--start T] [--duration T] [--threshold 50]` | Download VOD chat as TwitchDownloader JSON |
| `kickdl render <chat.json> <out> [options]` | Render chat to a transparent video |

Every command accepts `--ffmpeg <path>`.

### VOD ids

Kick has two kinds of VOD ids:

- **Legacy (UUIDv4)**, e.g. `371f0495-9aaa-4e7a-b88e-22b50c6194b0`: `kickdl check <channel>` lists them. The id alone is enough.
- **New (UUIDv7)**, e.g. `01a1d181-ff00-7f71-8638-3a34ea90175b`: the id in current `kick.com/<channel>/videos/…` links.
  It can only be resolved together with the channel, so **paste the full link**. A bare UUIDv7 is rejected with a message
  saying so (for `chat`, the channel argument is enough).

### vod

```powershell
kickdl vod 371f0495-9aaa-4e7a-b88e-22b50c6194b0 vod.mp4                                      # everything, best quality
kickdl vod https://kick.com/channel/videos/<id> clip.mp4 --start 01:00:00 --duration 30       # 30 s from the 1 h mark
kickdl vod 371f0495-9aaa-4e7a-b88e-22b50c6194b0 clip.mp4 --start 3600 --duration 90 --quality 480p
```

Quality names come from the stream's master playlist (`1080p60`, `720p60`, `480p30`, …); a prefix such as `480p` works.
Ranges are cut with `-c copy` (no re-encoding), so a cut starts at the keyframe a few seconds **before** the requested start.

### chat

```powershell
kickdl chat xqc 371f0495-9aaa-4e7a-b88e-22b50c6194b0 chat.json --start 01:00:00 --duration 30
```

- `--start` / `--duration` count from the start of the VOD (use the same values as for `vod`).
- Kick serves chat in 5-second windows. A window that reaches `--threshold` messages (default 50) may be truncated, so it
  is queried again from its last message; messages are de-duplicated by `id`.
- Requests are spaced 400 ms apart. On HTTP 429 the request is retried up to 8 times, waiting 5 → 15 → 30 → 60 s
  (or the server's `Retry-After` when longer), and the download continues where it stopped.

### render

```powershell
kickdl render chat.json chat.mov                                       # ProRes 4444, transparent (default)
kickdl render chat.json chat.webm --format webm                        # VP9, transparent, small file
kickdl render chat.json chat.mp4 --format mp4 --background "#111111"   # no alpha
kickdl render chat.json chat.webm --format webm --width 400 --height 700 --font-size 14 --fps 30
```

| Option | Default | Notes |
|---|---|---|
| `--format` | `mov` | `mov`, `webm` or `mp4`; the output extension must match |
| `--width`, `--height` | 350, 600 | even numbers, 100–8192 |
| `--fps` | 30 | 1–120 |
| `--background` | `#00000000` | `#RRGGBB` or `#AARRGGBB` |
| `--message-color` | `#ffffff` | |
| `--font-size` | 12 | |
| `--update-rate` | 0.2 | chat refresh interval in seconds |
| `--start`, `--end` | range in the json | trim |
| `--timestamp true` | off | show message times |
| `--outline true` | off | outlined text |
| `--emote-scale` | 1.0 | |
| `--verbose true` | off | show ffmpeg output |

ProRes 4444 is about 6 MB per second of video (60 s ≈ 364 MB). Use `webm` for long renders.

## Overlay the transparent chat on a video

Chat in the bottom-right corner, 20 px from the edges:

```powershell
# chat.webm: the libvpx-vp9 decoder is needed for alpha and must come BEFORE -i
ffmpeg -i video.mp4 -c:v libvpx-vp9 -i chat.webm -filter_complex "[0:v][1:v]overlay=W-w-20:H-h-20:eof_action=pass[v]" -map "[v]" -map 0:a? -c:v libx264 -preset veryfast -crf 18 -pix_fmt yuv420p -c:a copy final.mp4

# chat.mov
ffmpeg -i video.mp4 -i chat.mov -filter_complex "[0:v][1:v]overlay=W-w-20:H-h-20:eof_action=pass[v]" -map "[v]" -map 0:a? -c:v libx264 -preset veryfast -crf 18 -pix_fmt yuv420p -c:a copy final.mp4
```

- Other corners: top-right `overlay=W-w-20:20`, bottom-left `overlay=20:H-h-20`, top-left `overlay=20:20`.
- `eof_action=pass` lets the video continue if the chat video ends first.
- The output is H.264, so no transparency is needed there; alpha is only used while compositing.
- Timing: the chat starts at the `chat --start` offset. For a clip this matches the clip start. If you cut a VOD with
  `vod --start` and `-c copy`, the video can start a few seconds early; delay the chat with `-itsoffset 3` before
  `-i chat.webm`. The right offset differs per cut.

## End-to-end example: clip + transparent chat

```powershell
# 1) Download the clip. The last output line is the matching chat command.
kickdl clip clip_01M7G5HZCPJGB4125AC4RZJDA9 clip.mp4
#    matching chat: chat xqc 025962f7-7936-47e4-878c-eca630ab516f <chat.json> --start 38530 --duration 49

# 2) Run it: chat for the clip's range in the VOD
kickdl chat xqc 025962f7-7936-47e4-878c-eca630ab516f chat.json --start 38530 --duration 49

# 3) Render the transparent chat video
kickdl render chat.json chat.webm --format webm --width 400 --height 700 --font-size 14

# 4) Overlay
ffmpeg -i clip.mp4 -c:v libvpx-vp9 -i chat.webm -filter_complex "[0:v][1:v]overlay=W-w-20:H-h-20:eof_action=pass[v]" -map "[v]" -map 0:a? -c:v libx264 -preset veryfast -crf 18 -pix_fmt yuv420p -c:a copy final.mp4
```

## Known limitations

- Role badges (`subscriber`, `sub_gifter`, `moderator`, …) come without images in Kick's chat data and are not drawn.
  Level and custom badges are drawn.
- Third-party emotes (7TV etc.) are not supported; only Kick emotes are embedded.
- If a clip's source VOD was removed, the clip's chat cannot be downloaded.
- Kick's Cloudflare protection currently lets a plain `HttpClient` through. If that changes (HTTP 403), the request layer
  is behind `IKickTransport` so a TLS-impersonating transport can be added; it does not exist yet.
- Kick's web endpoints are unofficial and may change. The UUIDv7 lookup lives in one place, `KickApiClient.GetVideoAsync`.

## Legal

- KickDownloader is provided "as is" under the MIT license, without warranty.
- It is an independent project. It is **not affiliated with, endorsed, or sponsored by Kick**; "Kick" is a trademark of
  its owner.
- Downloaded VODs, clips and chat belong to their creators and to Kick. Respect copyright and Kick's Terms of Service.
  Do not redistribute content without permission.
- Chat files contain public usernames and messages. Handle them in line with the privacy laws that apply to you.
- Do not use this tool to overload Kick's servers. The built-in request spacing and backoff are there for that reason; do
  not remove them.

## Development

```powershell
dotnet test                                       # unit tests; the render smoke test runs when ffmpeg is on PATH
dotnet run --project src/KickDownloaderCLI -- check xqc
```

See [CONTRIBUTING.md](CONTRIBUTING.md).

## Credits and licenses

- KickDownloader: [MIT](LICENSE), Copyright (c) 2026 Gorkem UYSAL.
- Chat renderer: [TwitchDownloader](https://github.com/lay295/TwitchDownloader) by lay295, MIT License,
  Copyright (c) lay295. The `TwitchDownloaderCore` render code in `src/KickDownloaderCore/Render/` is used with its
  license and notices intact:
  [`LICENSE-TwitchDownloader.txt`](src/KickDownloaderCore/Render/LICENSE-TwitchDownloader.txt),
  [`TD-License`](src/KickDownloaderCore/Render/Resources/TD-License) and
  [`THIRD-PARTY-LICENSES.txt`](src/KickDownloaderCore/Render/Resources/THIRD-PARTY-LICENSES.txt).
- Everything else bundled or used (SkiaSharp, HarfBuzzSharp, Inter, Noto Color Emoji, Twemoji, …) is listed in
  [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
