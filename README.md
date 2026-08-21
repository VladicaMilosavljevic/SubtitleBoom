# SubtitleBoom v1.0 — First Public Release

SubtitleBoom is an offline Windows desktop application for subtitle alignment, transcription, translation, and subtitle editing.

## Release status

This source package is the release-ready source baseline for SubtitleBoom v1.0. The application version, visible window titles, localization resources, and project-data folder are finalized for the first public release.

* Application: `SubtitleBoom.exe`
* Version: `1.0.0`
* Project data: `SubtitleBoom_Data`
* Default UI language: English
* Processing language default: automatic detection
* Target platform: Windows x64
* Target framework: .NET 8 (`net8.0-windows`)
* Offline runtime: whisper.cpp, Whisper model files, and an LGPL-compatible FFmpeg build

## Build

On Windows, run `BUILD.bat` from the source-package root.

The build script:

1. checks the installed .NET SDK;
2. validates the bundled whisper.cpp runtime;
3. validates that the bundled FFmpeg does not report `--enable-gpl` or `--enable-nonfree`;
4. prepares the Tiny and Base Whisper models from local files only;
5. publishes the Windows x64 application into the `PROGRAM` folder;
6. copies the project license, third-party notices, third-party license texts, and donation configuration into the published package.

The build workflow itself does not download runtime components or models.

## Documentation

User documentation is in the `docs` folder. Interface language packs are in `languages`.

## Donation

SubtitleBoom is free and open-source software. Voluntary donations are welcome.

Official PayPal.Me link:

https://paypal.me/VladicaMilosavljevic

The application reads the same link from `config/donation.txt`.

## License

SubtitleBoom source code is released under the MIT License. See `LICENSE`.

Third-party components remain under their respective licenses. See `THIRD_PARTY_LICENSES.txt` and the `third_party_licenses` folder.

## Third-party components

The release uses or references, among other components:

* whisper.cpp / GGML runtime — MIT
* OpenAI Whisper model weights — MIT
* FFmpeg — LGPL-compatible build used as a separate executable
* LibVLCSharp.WinForms — LGPL 2.1
* VideoLAN.LibVLC.Windows 3.0.23.1 — LGPL 2.1 or later

For the exact FFmpeg build identifier and source-compliance note, see `runtime/bin/FFMPEG_BUILD_INFO.txt` and `third_party_licenses/FFMPEG_SOURCE_NOTICE.txt`.

## Important redistribution note

If you redistribute a SubtitleBoom binary package that includes FFmpeg or libVLC binaries, retain the applicable third-party notices and license texts. For FFmpeg, make the complete corresponding source for the exact distributed build available in accordance with the applicable LGPL requirements and FFmpeg's redistribution guidance.

## Release assets

For the public v1.0 release, the following release assets are provided:

1. `SubtitleBoom_v1.0_Windows_x64.zip` — the tested Windows x64 binary package.
2. `FFmpeg_Source_and_Compliance_SubtitleBoom_v1.0_FINAL.zip` — FFmpeg corresponding-source and compliance support material for the bundled FFmpeg build.

The SubtitleBoom source code is available directly in this public GitHub repository. GitHub also automatically provides source-code archives (`Source code (zip)` and `Source code (tar.gz)`) for the v1.0 tag.

Do not remove `LICENSE`, `THIRD_PARTY_LICENSES.txt`, or `third_party_licenses` from redistributed binary packages.
