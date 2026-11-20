# Validation and limits

Windows 11 x64 beta, tested on one PC. A clean independent second-PC test has not been completed. Raw recordings, desktop captures and local reports are excluded from publication.

## Owner field results

The owner reported DOSBox recording with CPU and Cyberpunk 2077 with CPU/NVIDIA. Background-video sound was excluded from selected-game audio. Fullscreen transitions can interrupt recording, as documented.

Editing field results passed trim + CRT, manual crop, smaller exports, added sound with separate volume/fades/muted original, and a longer combined crop/trim/audio export with sound synchronized throughout. Video files supplied added audio; images without audio were rejected. Persistent crop display, optional picture presets, samples and numbered outputs were also reported working. New drag-to-move behavior is controlled-tested; checking it with the owner's footage remains useful before calling a release stable.

## Controlled checks

- CPU/NVIDIA exports, full stream decode, dimensions/frame times and AAC packet preservation where copied.
- Constant/variable frame rate, nonzero origins, accurate cuts and timed audio-cue sync.
- Crop pixels/dimensions, movement without resizing, edge limits, 100% view and explicit redraw.
- Original processing without Vulkan, approved CRT reference, scaled preview comparison and explicit HDR rejection.
- Volume/mute, multiple tracks, selected-track mixing, delay/fades and short/absent soundtracks.
- In-app sample open/play/pause/seek/resume/cancel, retaining the whole section's audio timeline.
- Standalone MP3/WAV/M4A listening, full-file seeking, unchanged edits and missing/changed-file errors.
- Numbered names, late collisions, frozen edits, locked/changed input protection and temporary cleanup.
- Recorder preview, countdown, shortcut start/stop, unchanged target bounds/focus, interruption saving and shortcut pause around Video tools.
- About text/link destinations, normal/minimum layouts, setup/archive/tool hashes and publication privacy/allowlist review.

This release moves to supported .NET 10 LTS. Recording, export and playback are checked on that runtime. These results establish the listed cases on this PC, not universal game/driver compatibility or performance.

## Developer entry points

Use fresh folders. Some checks need synthetic fixtures, NVIDIA hardware, Vulkan or normal desktop/media access.

```powershell
.\app\CaptureAndRelease.exe --ui-self-test .\.build\recorder-check
.\app\CaptureAndRelease.exe --release-checks .\.build\crop-check --video <synthetic-video>
.\app\CaptureAndRelease.exe --editing-tests .\.build\editing-check --fixtures <fixture-folder> --include-nvidia
.\app\CaptureAndRelease.exe --trim-tests .\.build\trim-check --fixtures <fixture-folder>
.\app\CaptureAndRelease.exe --crt-export-tests .\.build\export-check --fixtures <fixture-folder> --include-nvidia
```

The recorder check creates its own animated/audio window and interrupts its own capture subprocess. It does not send real shortcuts to the user's game. This simulated loss is not an exclusive-fullscreen transition. `source/make-export-fixtures.py` creates basic synthetic media; extended local cases are described in test code. Personal gameplay is neither required nor included.

Remaining limits: clean-PC setup, broader/older/multiple GPUs, anti-cheat games, HDR, extended sessions beyond owner reports and every Windows scaling setting. Known browser/window and fullscreen limitations remain in README.
