# Credits and third-party components

## Capture & Release

Copyright (c) 2026 **Ibrahim Samir**. Application source, the native audio helper, setup/build scripts and original artwork are licensed under **GPL-3.0-only**; see [LICENSE](LICENSE). Developed with assistance from OpenAI Codex. No OpenAI service or SDK is required to run the recorder.

The source for the application and native helper accompanies the binary release and is available at https://github.com/IbrahimS-1101/capture-and-release. The application is provided without warranty under the license.

## FFmpeg — separately obtained dependency

FFmpeg is developed by the FFmpeg contributors: https://ffmpeg.org/.
Windows build provider: **Gyan Doshi**, https://www.gyan.dev/ffmpeg/builds/.

The one-time setup obtains **8.0.1-full_build-www.gyan.dev** directly from the provider's GitHub release:

- Package: https://github.com/GyanD/codexffmpeg/releases/download/8.0.1/ffmpeg-8.0.1-full_build.zip
- SHA-256: `467cde100a47ed4b03a897988aeb4a296890c1e2b2d2864204657d002bc5fb90`
- Provider's release and source reference: https://github.com/GyanD/codexffmpeg/releases/tag/8.0.1
- FFmpeg source commit identified by the provider: https://github.com/FFmpeg/FFmpeg/commit/894da5ca7d
- Licensing information: https://ffmpeg.org/legal.html

This build identifies its license as GPLv3 and contains separately credited libraries, including x264. Setup preserves the package's LICENSE and README as `app/tools/FFmpeg-LICENSE.txt` and `FFmpeg-README.txt`; consult those for the actual build configuration and library versions. The upstream FFmpeg source link alone is not a claim to provide all corresponding source for the provider's static build.

**No FFmpeg executable or library is redistributed in our repository or release archives.** Users download the dependency directly from its provider. If you redistribute an installed folder containing those binaries, you become responsible for the relevant dependency license and corresponding-source obligations. Our application license and source do not replace those requirements.

## Microsoft technologies

The UI uses **.NET Windows Forms**. The .NET 9 Desktop Runtime is installed separately from Microsoft and is not bundled in the release. .NET project notices: https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT and https://github.com/dotnet/winforms/blob/main/LICENSE.TXT.

`ProcessAudio.exe` is built from the accompanying original `source/ProcessAudio.cpp` with Microsoft Visual C++ and a statically linked Visual C++ runtime. Microsoft components retain their applicable Microsoft license terms. Windows APIs and the Windows SDK are not re-licensed by this project's GPL notice.

Audio capture uses the documented Windows WASAPI application/process-loopback interfaces. No Microsoft sample source files are included. Documentation: https://learn.microsoft.com/en-us/windows/win32/api/audioclientactivationparams/ne-audioclientactivationparams-process_loopback_mode.

Windows, NVIDIA, and other product names belong to their respective owners. This project is independent and does not imply their endorsement.
