# SubtitleBoom v1.0 — First Public Release

SubtitleBoom is a free and open-source Windows desktop application for subtitle alignment, transcription, translation, and subtitle editing.

It is designed to work locally on your computer and combines automatic speech processing with a built-in subtitle editor.

## Screenshots

### Main window

![SubtitleBoom main window](screenshots/01_SubtitleBoom_main_window.png)

### Subtitle editor

![SubtitleBoom subtitle editor](screenshots/02_SubtitleBoom_subtitle_editor.png)

### Batch processing

![SubtitleBoom batch processing](screenshots/03_SubtitleBoom_batch_processing.png)

## Features

* Automatic subtitle-to-speech alignment
* Built-in subtitle editor
* Audio waveform and detected-speech visualization
* Video and audio preview
* Offline speech transcription using whisper.cpp
* Speech translation to English
* Automatic processing-language detection
* SRT and TXT workflows
* Optional timestamps in TXT transcription
* Batch processing of multiple projects
* YouTube subtitle output support
* Local project data and reusable processing results
* Multiple interface languages
* Tiny and Base Whisper models included in the standard offline package
* Additional Whisper models can be used when available locally
* Designed to work without an Internet connection after installation

## Release status

This repository contains the release-ready source baseline for SubtitleBoom v1.0.

* Application: `SubtitleBoom.exe`
* Version: `1.0.0`
* Project data: `SubtitleBoom_Data`
* Default UI language: English
* Processing language default: automatic detection
* Target platform: Windows x64
* Target framework: .NET 8 (`net8.0-windows`)
* Offline runtime: whisper.cpp, Whisper model files, and an LGPL-compatible FFmpeg build

## Download

The ready-to-use Windows x64 version is available from the GitHub Releases section.

For the first public release, download:

`SubtitleBoom_v1.0_Windows_x64.zip`

Extract the archive and run `SubtitleBoom.exe`.

The release package contains the runtime components and standard Whisper models required for offline operation.

## Build from source

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

User documentation is available in the `docs` folder.

Interface language packs are stored in the `languages` folder.

## Donation

SubtitleBoom is free and open-source software. If you find it useful and would like to support its development, voluntary donations are welcome.

Official PayPal.Me link:

https://paypal.me/VladicaMilosavljevic

The application reads the same link from `config/donation.txt`.

## License

SubtitleBoom source code is released under the MIT License. See `LICENSE`.

Third-party components remain under their respective licenses. See `THIRD_PARTY_LICENSES.txt` and the `third_party_licenses` folder.

## Third-party components

SubtitleBoom uses or references, among other components:

* whisper.cpp / GGML runtime — MIT
* OpenAI Whisper model weights — MIT
* FFmpeg — LGPL-compatible build used as a separate executable
* LibVLCSharp.WinForms — LGPL 2.1
* VideoLAN.LibVLC.Windows 3.0.23.1 — LGPL 2.1 or later

For the exact FFmpeg build identifier and source-compliance information, see:

* `runtime/bin/FFMPEG_BUILD_INFO.txt`
* `third_party_licenses/FFMPEG_SOURCE_NOTICE.txt`

## Important redistribution note

If you redistribute a SubtitleBoom binary package that includes FFmpeg or libVLC binaries, retain the applicable third-party notices and license texts.

For FFmpeg, make the complete corresponding source for the exact distributed build available in accordance with the applicable LGPL requirements and FFmpeg's redistribution guidance.

Do not remove `LICENSE`, `THIRD_PARTY_LICENSES.txt`, or `third_party_licenses` from redistributed binary packages.

## Release assets

The public SubtitleBoom v1.0 release provides:

1. `SubtitleBoom_v1.0_Windows_x64.zip` — tested Windows x64 binary package.
2. `FFmpeg_Source_and_Compliance_SubtitleBoom_v1.0_FINAL.zip` — corresponding FFmpeg source and compliance-support material for the bundled FFmpeg build.

The SubtitleBoom source code is available directly in this public GitHub repository.

GitHub also automatically provides source-code archives for the v1.0 tag.
