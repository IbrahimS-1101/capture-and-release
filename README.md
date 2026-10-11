# Capture & Release

A lightweight Windows recorder for games and other applications, with **selected application audio** and direct MP4 output.

Created by **[Ibrahim Samir](https://github.com/IbrahimS-1101)**, with development assistance from OpenAI Codex. Free and open source under [GPL-3.0-only](LICENSE).

## Download and setup

**[Download the Windows x64 beta](https://github.com/IbrahimS-1101/capture-and-release/releases/tag/v0.3.0-beta.1)**

Requirements: **Windows 11 x64**, [.NET 9 Desktop Runtime x64](https://dotnet.microsoft.com/en-us/download/dotnet/9.0), and enough free recording space. Application-only audio requires Windows build 20348 or later; normal Windows 10 desktop releases are below that requirement. NVIDIA encoding additionally needs a compatible GPU and driver. CPU recording does not require an NVIDIA GPU.

1. Extract the entire release ZIP to a writable folder.
2. Run **Setup Recording Tools.cmd** once. It downloads FFmpeg 8.0.1 directly from [Gyan Doshi's upstream release](https://github.com/GyanD/codexffmpeg/releases/tag/8.0.1), verifies the pinned SHA-256, and installs only the recording tools and their notices. The download is about **234 MB**; temporary extraction needs additional space. No administrator access is requested.
3. Open **app/CaptureAndRelease.exe**. Keep the app folder together.

Setup needs internet access. Recording afterward works offline. The application does not download updates, upload recordings, or collect telemetry. About-box links open your browser only when clicked. The executable is unsigned; Windows may display its unfamiliar-app warning.

FFmpeg binaries are **not included** in our GitHub repository or release ZIP. For manual setup, download the exact full-build ZIP linked above, extract `bin/ffmpeg.exe` and `bin/ffprobe.exe` into `app/tools`, and retain its LICENSE and README. For offline setup with that downloaded archive:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Install-RecordingTools.ps1 -ArchivePath .\ffmpeg-8.0.1-full_build.zip
```

The supplied setup launcher uses PowerShell execution-policy bypass for that one process only; it does not change the machine's policy. The dependency download is fixed to a version and checksum; updates require a new reviewed setup script.

## Record

1. Open your game or application. Click **Refresh applications** and select its actual window under **Application audio**. Select the game/DOSBox process rather than its launcher.
2. Choose the picture source:
   - **Screen · entire monitor:** records everything visible on that monitor, including other windows and notifications. The selected application determines the audio only.
   - **Window · selected application:** records the selected window where supported. Keep it restored, rather than minimized.
3. Start with **CPU · reliable default**, **30 fps**, and **1080p · balanced**. NVIDIA GPU encoding is also available. Intel/AMD users can use CPU encoding; their hardware encoders are not offered in this beta.
4. Choose an output folder and use **Check preview**. Click **Start recording** and return to your application during the countdown, or press **Ctrl + Shift + F9** in the game. Enter fullscreen before recording starts.
5. Press the same shortcut to stop and save. During the countdown it cancels recording. Wait for the saved confirmation, then use **Open recordings**.

Only playback from the selected application's process tree is captured. **There is no microphone or whole-system-audio fallback.** Browser audio may include other tabs belonging to that process tree; selecting a window does not isolate a tab's sound.

## Known limits

- **Chrome Window capture can produce black video**, even while audio/pointer capture works. Use Screen mode with the browser fullscreen as a workaround. Anything appearing over it is still captured.
- **Alt-Tab or a fullscreen/display change can interrupt Screen recording.** The recorder attempts to save the captured portion and explains the interruption. Automatic resume is not implemented. Stop with the shortcut before switching away.
- DOSBox may leave fullscreen or close when switching applications. Let it finish loading before starting recording. The recorder does not resize or send Alt-Tab/Alt-Enter to the game.
- Window capture is application-dependent. Covered DOSBox windows worked in testing; that does not guarantee every program will work. Check a short recording first.
- **Compatibility capture** provides an alternate Screen method; it is not a guaranteed fullscreen fix.
- CPU encoding can affect demanding games. Lower resolution/frame rate or use NVIDIA encoding if available. HDR, anti-cheat games, clean-PC installation, and broad hardware compatibility remain unverified.
- There is no streaming, editor, overlay, browser-tab-specific capture, automatic upload, or automatic capture reconnection.

## Files, settings and recovery

The default output folder is **Videos / Screen Recordings**. Proportions are preserved; smaller sources are not enlarged by the resolution presets. Original-size output keeps the source resolution, rounded for H.264. There is no automatic cropping.

Settings are stored in `%LOCALAPPDATA%\CaptureAndRelease\settings.json`. Existing Local Screen Recorder/Gameplay Recorder settings can be read on first launch; new preferences are written separately.

While recording, a `.recording-…` folder beside the MP4 contains intermediate video/audio, timing metadata, and diagnostic logs. Large intermediates are removed only after the final MP4 passes a structural check. Small logs remain. A five-and-a-half-hour limit keeps PCM audio below its file-size boundary.

If saving is interrupted, choose **Recover recording…** and select `session.json` from the surviving recording folder. Keep those files until recovery succeeds. Recovery is not guaranteed after a disk or power failure.

Recordings and local diagnostic files can contain application names, file paths and other personal information. Review them before attaching anything to a public issue. This project publishes no user recordings, settings, or diagnostic logs.

## Build from source

Install the **.NET 9 SDK**, **Visual Studio C++ desktop build tools**, and a recent **Windows SDK** with application-loopback audio headers, then run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\source\build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\Install-RecordingTools.ps1
```

The first command builds the WinForms application and native WASAPI helper; the second obtains FFmpeg separately. Build intermediates stay in the ignored `.build` folder. No package manager dependencies are used by the application source.

See [TESTING.md](TESTING.md) for validation scope and developer checks, [CHANGELOG.md](CHANGELOG.md) for changes, and [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for credits. Focused bug reports and improvements are welcome through GitHub issues and pull requests. No support response time is promised.
