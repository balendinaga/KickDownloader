# Contributing

Thanks for helping. This is a small project; keep changes focused.

## Setup

- Windows, [.NET 10 SDK](https://dotnet.microsoft.com/download), [FFmpeg](https://ffmpeg.org/) on `PATH` (optional;
  only the render smoke test uses it).
- `dotnet test` must pass before you open a pull request. `.\build.ps1` must build both executables.

## Layout

| Path | Contents |
|---|---|
| `src/KickDownloaderCore` | Kick API client, downloaders, chat conversion, render service |
| `src/KickDownloaderCore/Render` | Vendored `TwitchDownloaderCore` renderer (see below) |
| `src/KickDownloaderCLI` | `kickdl` command line |
| `src/KickDownloaderWPF` | Desktop app |
| `tests/KickDownloaderCore.Tests` | xUnit tests and JSON fixtures |

## Guidelines

- No network in tests. Use the fixtures in `tests/KickDownloaderCore.Tests/Fixtures` and the `IKickTransport` seam.
  Fixtures must not contain real viewers' names: replace them with placeholders.
- Add a test for every parser, validation rule, branch or retry path you change.
- Validate input at the boundary (links, times, file paths). Media URLs must stay restricted to `https://*.kick.com`.
- Do not weaken request spacing or 429 backoff, and do not add code that bypasses access controls or DRM.
- UI text, error messages and code comments are English.
- Keep the diff small. Do not reformat unrelated code.

## The vendored renderer

`src/KickDownloaderCore/Render` is copied from [TwitchDownloader](https://github.com/lay295/TwitchDownloader) (MIT) and
keeps its `TwitchDownloaderCore.*` namespaces. Keep changes there minimal and note them in
`THIRD_PARTY_NOTICES.md`. Never remove or edit `LICENSE-TwitchDownloader.txt`, `Resources/TD-License` or
`Resources/THIRD-PARTY-LICENSES.txt`. If you copy more files from upstream, update the notices and the license list.

## Pull requests

1. Fork, create a branch, make the change.
2. Run `dotnet test` and `.\build.ps1`.
3. Describe what changed and why. Mention anything you could not test (for example live Kick behavior).

By contributing you agree that your contribution is licensed under the MIT license of this project.

## Reporting problems

Open an issue with the command or app tab you used, the (redacted) link, the error text, and your OS and .NET version.
Do not post private links or tokens. Kick changes its web endpoints from time to time, so "404" or "403" reports are
most useful with the exact URL form of the VOD or clip.
