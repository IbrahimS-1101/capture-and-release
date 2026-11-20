# Capture & Release

A lightweight Windows screen/window recorder with selected-application audio, plus tools to trim, crop, filter and adjust saved videos.

Created by **[Ibrahim Samir](https://github.com/IbrahimS-1101)**, with development assistance from OpenAI Codex. Free and open source under [GPL-3.0-only](LICENSE).

## Download and setup

**[Windows x64 beta downloads](https://github.com/IbrahimS-1101/capture-and-release/releases)** · current package: **0.4.0 beta 1**.

Requirements: **Windows 11 x64**, **[.NET 10 Desktop Runtime x64](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)** and enough free space. Install the Desktop Runtime rather than only the base runtime. NVIDIA encoding needs a compatible GPU/driver. CRT requires Vulkan, including with CPU encoding.

1. Extract the entire ZIP to a writable folder; keep its files together.
2. Install the .NET 10 Desktop Runtime if missing. Microsoft's installer may ask for permission.
3. Run **Setup Recording Tools.cmd** once. It obtains FFmpeg 8.0.1 from [Gyan Doshi's release](https://github.com/GyanD/codexffmpeg/releases/tag/8.0.1), verifies a pinned SHA-256 and installs FFmpeg/ffprobe with notices. Download: about 234 MB; extraction needs additional space. This script does not request administrator access.
4. Run **Start Capture & Release.cmd** for recording, or **Start Video Tools.cmd** for editing.

The public package **does not bundle FFmpeg or .NET**. Setup needs internet once; recording/editing afterward work locally. No automatic media uploads or update downloads. About links open only when clicked. The app is unsigned; Windows may show an unfamiliar-app warning.

For offline dependency setup, obtain the exact FFmpeg ZIP above and run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Install-RecordingTools.ps1 -ArchivePath .\ffmpeg-8.0.1-full_build.zip
```

The execution-policy override applies to that process only. Manual setup: copy the archive's `bin/ffmpeg.exe` and `bin/ffprobe.exe` to `app/tools` and retain LICENSE/README. See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

## Record

1. Open the game/application, refresh and select its actual window under Application audio. Select the game/DOSBox process rather than its launcher.
2. Choose **Screen** for the whole monitor or **Window** for the selected window where supported. Screen includes other visible windows/notifications; application selection determines audio.
3. Choose encoding, frame rate, quality, countdown and destination. Check preview. Enter fullscreen before recording starts.
4. Start or use **Ctrl + Shift + F9**. The shortcut cancels a countdown or stops/saves a recording. Wait for the saved confirmation.

Only the selected application's process-tree audio is captured. There is no microphone or system-wide fallback. Browser tabs can share a process tree, so window selection does not isolate a tab's sound.

## Video tools

Open a saved video. **Original / no effects** is selected initially. Sources and existing outputs are protected.

- **Trim:** set start/end directly or mark them from preview. Start is included; end is excluded. Use full video resets the range. Cuts follow decoded frames.
- **Crop:** choose Square/16:9/9:16, enter source-pixel coordinates, or draw with Select on preview. **Drag inside the blue rectangle to move it without resizing.** It stops at the picture edges. Crop keeps the source/outline visible; other tabs show processed output. Left/Top provide precise positioning.
- **Filters:** Original or CRT, with softening, scanlines, phosphor mask, bloom and optional curvature. Defaults and **DOS 320×200** remain available. **Match this picture** explicitly applies a mild preset using current output size/aspect, keeping curvature on/off and resetting other CRT controls. Source changes never apply a look automatically. Known logical game dimensions can be set separately.
- **Audio:** adjust/mute original sound or add one file. Video files can supply audio; files without audio are rejected. Added sound has a separate level, clip-relative start and fades. It plays once and ends with the section. With an added file, one chosen original track and added sound become one stereo AAC track; without one, original tracks stay separate.
- **Listen to file:** identify the selected file alone, with pause/restart/full-file seeking. Mix edits are not applied.
- **Export:** original/cropped size, fit within 1080p/720p, and High/Balanced/Smaller quality. Fit presets preserve proportions to even-pixel rounding and never enlarge a picture. Output is H.264 MP4, CPU or NVIDIA encoded.

**Update preview** applies picture changes to a still frame. **Play 10 s sample** renders current edits to a temporary clip and plays it inside the app. It starts at Preview time if inside the kept section, otherwise at its beginning. Audio delay/fades remain aligned to the full saved section when sampling its middle/end. Close, adjust and regenerate to compare. This is rendered sampling, not a live timeline/mixer.

Playback supports pause/replay, restart and seeking through Windows media components. Open in player is available for unsupported playback. Samples are cleaned on close on a best-effort basis if another player holds them open. Cancel preparation/export beside progress.

PNG saves include completed picture edits. Saved JSON is CRT-only, not an editing project. New sources reset trim/crop/audio/export choices while retaining CRT settings. Crop precedes CRT; size reduction follows it. Conflicting MP4/PNG names are numbered, including conflicts arising during encoding; completion shows the actual saved name.

## Why encoder file sizes differ

CPU uses x264; NVIDIA uses NVENC. Both encode H.264 but choose predictions, motion representations and remaining picture detail differently. Our presets target quality rather than a fixed file size; matching names/numbers do not promise identical quality or size across encoders. Nearly identical-looking pictures can therefore use different amounts of data. A larger file alone is not proof of a better picture.

## Limits, files and recovery

- Chrome Window capture can be black while audio/pointer work. Screen mode with the browser fullscreen is a workaround; anything covering it remains visible.
- Alt-Tab, display/fullscreen changes or source closure can interrupt recording. The captured portion is saved where possible; automatic reconnect is unavailable. Stop before switching away.
- DOSBox may leave fullscreen or close during switching. Let loading finish. The recorder does not send Alt-Tab/Alt-Enter or resize the game.
- Window support varies; keep it restored and test a short clip. Compatibility capture is an alternate Screen method, not a guaranteed fullscreen fix.
- Editing supports completed opaque SDR, square pixels, up to 32 megapixels/frame and 24 hours. Tagged PQ/HLG HDR and non-square pixels are rejected; untagged HDR is not always identifiable. MP4 requires even dimensions.
- Unchanged whole-video AAC can be copied; cuts/audio edits encode AAC at 192 kb/s. Empty audio tracks in a cut may be omitted. A limiter handles boosts/mixes but cannot repair existing distortion.
- Curved/fine CRT patterns can alias after compression/display scaling. Judge the sample/output at intended size. PNG preview does not simulate H.264 compression exactly.
- One kept section, one rectangular crop and one added file. No streaming, automatic upload, tab-specific capture, animated crop, multi-clip timeline or frame interpolation. Subtitles/chapters/source metadata are not copied.
- At least 1 GB free to start; large jobs need more. Recording limit: five and a half hours. Abrupt exits can leave owned intermediate folders.

Recordings default to Videos / Screen Recordings. Preferences use `%LOCALAPPDATA%\CaptureAndRelease-VideoTools\settings.json`, with older preferences read for migration. No personal settings are bundled.

A `.recording-…` folder beside each recording holds intermediate media, timing and logs. If finalization was interrupted, use Recover recording and select its `session.json`. Keep intermediates until recovery succeeds. Recovery after disk/power failure is not guaranteed. Logs can contain paths/application names; review before sharing.

Editing exports lock/check inputs, use a unique temporary folder by the destination and commit without overwriting. Original sources are not modified.

## Build and validation

Install .NET 10 SDK (10.0.402 or a later compatible feature band), Visual Studio C++ desktop build tools and a recent Windows SDK:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\source\build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\Install-RecordingTools.ps1
```

No third-party managed packages are needed. Intermediates stay in `.build`. See [TESTING.md](TESTING.md), [CHANGELOG.md](CHANGELOG.md) and [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md). Local checks do not establish broad hardware or clean second-PC compatibility.

## Optional support

The program is free to use. Sharing anywhere is appreciated; a mention on **[X](https://x.com/isamirDM)** or **[a coffee](https://buymeacoffee.com/isamir)** is welcome. Support is optional and unlocks no features. Bug reports/improvements are welcome; no response time is promised.
