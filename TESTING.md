# Validation and limits

This is a **Windows x64 beta**, developed and tested on one Windows 11 PC. It has not yet passed a clean second-PC installation test.

Before the public rename, the owner successfully recorded DOSBox games with CPU encoding and Cyberpunk 2077 with NVIDIA encoding. Earlier field tests also exercised Cyberpunk with CPU encoding and confirmed that unrelated background playback was excluded from DOSBox recordings. These are specific test cases, not a compatibility guarantee for all games or hardware.

The preceding build passed controlled tests for CPU Screen capture, compatibility Screen capture, Window capture, NVIDIA Screen capture, preview, countdown cancellation, start/stop shortcut handling, interruption saving, and recovery after abrupt recorder exit. Media checks covered full decode, nonblack sample frames, timestamps and separation of a selected 440 Hz tone from an unrelated 880 Hz tone. They do not establish perceptual A/V synchronization for every game or long recording.

The public-release rename retains the capture implementation and adds About credits, branding, a separate settings location and upstream dependency setup. Release checks repeat the controlled capture/UI/media tests and verify that setup obtains the same FFmpeg executables as the tested build. Raw desktop captures and local reports are deliberately excluded from the repository and release ZIP.

Developer checks (after building and installing tools):

```powershell
.\app\CaptureAndRelease.exe --self-test .\.build\capture-test --include-nvidia
.\app\CaptureAndRelease.exe --ui-self-test .\.build\ui-test
```

The first command plays controlled audio tones and captures a generated animated window. Omit `--include-nvidia` if that hardware is unavailable. The UI test registers an available shortcut, creates its own test source, records it, and terminates its own capture subprocess to check interruption saving. This simulated loss is **not** a real exclusive-fullscreen transition. Use fresh test folders for each run.

Still unverified: clean-PC setup, broad GPU/driver support, anti-cheat games, HDR, multiple-GPU configurations, long-session stability and real fullscreen transitions beyond the owner's specific field reports. Browser Window black video and fullscreen access-loss interruptions remain documented limitations.
