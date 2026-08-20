$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$RuntimeBin = Join-Path $Root "runtime\bin"
$RuntimeModels = Join-Path $Root "runtime\models"
$Publish = Join-Path $Root "PROGRAM"
$Download = Join-Path $Root "_download"
$Project = Join-Path $Root "src\SubtitleAligner\SubtitleAligner.csproj"


function Test-DownloadedFile {
    param(
        [Parameter(Mandatory=$true)][string]$Path,
        [Parameter(Mandatory=$true)][long]$MinimumBytes
    )

    return (Test-Path $Path) -and ((Get-Item $Path).Length -ge $MinimumBytes)
}

function Find-ExistingModel {
    param(
        [Parameter(Mandatory=$true)][string]$FileName,
        [Parameter(Mandatory=$true)][long]$MinimumBytes,
        [Parameter(Mandatory=$true)][string[]]$SearchRoots
    )

    foreach ($searchRoot in $SearchRoots) {
        if ([string]::IsNullOrWhiteSpace($searchRoot) -or !(Test-Path $searchRoot)) {
            continue
        }

        Write-Host "Trazim postojeci $FileName u: $searchRoot" -ForegroundColor DarkGray

        try {
            $candidate = Get-ChildItem `
                -Path $searchRoot `
                -Filter $FileName `
                -File `
                -Recurse `
                -ErrorAction SilentlyContinue |
                Where-Object { $_.Length -ge $MinimumBytes } |
                Sort-Object LastWriteTime -Descending |
                Select-Object -First 1

            if ($candidate) {
                return $candidate.FullName
            }
        }
        catch {
            Write-Host "Pretraga nije uspela u ${searchRoot}: $($_.Exception.Message)" -ForegroundColor Yellow
        }
    }

    return $null
}

function Find-ExistingFile {
    param(
        [Parameter(Mandatory=$true)][string]$FileName,
        [Parameter(Mandatory=$true)][long]$MinimumBytes,
        [Parameter(Mandatory=$true)][string[]]$SearchRoots
    )

    foreach ($searchRoot in $SearchRoots) {
        if ([string]::IsNullOrWhiteSpace($searchRoot) -or !(Test-Path $searchRoot)) {
            continue
        }

        Write-Host "Trazim postojeci $FileName u: $searchRoot" -ForegroundColor DarkGray
        try {
            $candidate = Get-ChildItem -Path $searchRoot -Filter $FileName -File -Recurse -ErrorAction SilentlyContinue |
                Where-Object { $_.Length -ge $MinimumBytes } |
                Sort-Object LastWriteTime -Descending |
                Select-Object -First 1
            if ($candidate) { return $candidate.FullName }
        }
        catch {
            Write-Host "Pretraga nije uspela u ${searchRoot}: $($_.Exception.Message)" -ForegroundColor Yellow
        }
    }

    return $null
}

function Reuse-LocalFile {
    param(
        [Parameter(Mandatory=$true)][string]$FileName,
        [Parameter(Mandatory=$true)][string]$Destination,
        [Parameter(Mandatory=$true)][long]$MinimumBytes,
        [Parameter(Mandatory=$true)][string[]]$SearchRoots,
        [Parameter(Mandatory=$true)][string]$Description,
        [switch]$CopySiblingDlls
    )

    if (Test-DownloadedFile -Path $Destination -MinimumBytes $MinimumBytes) {
        Write-Host "$Description je vec prisutan - preskacem." -ForegroundColor Green
        return
    }

    $existing = Find-ExistingFile -FileName $FileName -MinimumBytes $MinimumBytes -SearchRoots $SearchRoots
    if (!$existing) {
        throw "$Description nije pronadjen lokalno. Ovaj OFFLINE build ne preuzima nista. Kopiraj runtime iz ranije verzije u runtime\\bin ili raspakuj projekat pored ranijih verzija pa pokreni BUILD.bat ponovo."
    }

    Write-Host "Pronadjen lokalni ${Description}:" -ForegroundColor Green
    Write-Host $existing -ForegroundColor DarkGray
    Copy-Item -Path $existing -Destination $Destination -Force

    if ($CopySiblingDlls) {
        $sourceDir = Split-Path $existing -Parent
        Get-ChildItem $sourceDir -Filter "*.dll" -File -ErrorAction SilentlyContinue |
            Copy-Item -Destination (Split-Path $Destination -Parent) -Force
    }

    if (!(Test-DownloadedFile -Path $Destination -MinimumBytes $MinimumBytes)) {
        throw "$Description je pronadjen, ali kopiranje nije uspelo."
    }

    Write-Host "$Description je uspesno kopiran iz ranije verzije." -ForegroundColor Green
}

New-Item -ItemType Directory -Force -Path $RuntimeBin, $RuntimeModels | Out-Null

Write-Host ""
Write-Host "SUBTITLEBOOM v1.0 FIRST PUBLIC RELEASE - OFFLINE BUILD" -ForegroundColor Cyan
Write-Host ""

Write-Host "1/7 Proveravam .NET SDK..."
$version = (& dotnet --version)
if ($LASTEXITCODE -ne 0) { throw ".NET SDK nije pronadjen." }
Write-Host "Pronadjen .NET SDK: $version" -ForegroundColor Green

$Parent1 = Split-Path $Root -Parent
$Parent2 = Split-Path $Parent1 -Parent
$Parent3 = Split-Path $Parent2 -Parent

# OFFLINE: pretraga je ogranicena na trenutni projekat i nekoliko nadredjenih foldera.
$LocalSearchRoots = @(
    $Root,
    $Parent1,
    $Parent2,
    $Parent3,
    (Join-Path $env:LOCALAPPDATA "SubtitleAligner"),
    (Join-Path $env:LOCALAPPDATA "YouTubeSubtitleAligner")
) | Where-Object {
    ![string]::IsNullOrWhiteSpace($_) -and (Test-Path $_)
} | Select-Object -Unique

$WhisperExe = Join-Path $RuntimeBin "whisper-cli.exe"
Write-Host "2/7 Proveravam ukljuceni whisper.cpp runtime (OFFLINE)..."
$RequiredWhisperFiles = @(
    "whisper-cli.exe",
    "whisper.dll",
    "ggml.dll",
    "ggml-base.dll",
    "ggml-cpu-x64.dll",
    "SDL2.dll"
)
foreach ($whisperFile in $RequiredWhisperFiles) {
    $whisperPath = Join-Path $RuntimeBin $whisperFile
    if (!(Test-Path $whisperPath)) {
        throw "Nedostaje runtime\bin\$whisperFile. SubtitleBoom v1.0 source paket mora da sadrzi provereni whisper.cpp runtime."
    }
}

# whisper.cpp ispisuje informacije o ucitavanju CPU backenda na STDERR i kada radi ispravno.
# Start-Process koristimo da PowerShell taj normalni statusni ispis ne pretvori u gresku builda.
$whisperStdOut = Join-Path $env:TEMP ("subtitleboom_whisper_stdout_" + [guid]::NewGuid().ToString("N") + ".txt")
$whisperStdErr = Join-Path $env:TEMP ("subtitleboom_whisper_stderr_" + [guid]::NewGuid().ToString("N") + ".txt")
try {
    $whisperProcess = Start-Process -FilePath $WhisperExe -ArgumentList "--help" -NoNewWindow -Wait -PassThru `
        -RedirectStandardOutput $whisperStdOut -RedirectStandardError $whisperStdErr
    if ($whisperProcess.ExitCode -ne 0 -and $whisperProcess.ExitCode -ne 1) {
        throw "Ukljuceni whisper.cpp runtime ne moze da se pokrene (exit code $($whisperProcess.ExitCode))."
    }
}
finally {
    Remove-Item $whisperStdOut -Force -ErrorAction SilentlyContinue
    Remove-Item $whisperStdErr -Force -ErrorAction SilentlyContinue
}
Write-Host "Proveren ukljuceni whisper.cpp runtime: OK" -ForegroundColor Green

$FfmpegExe = Join-Path $RuntimeBin "ffmpeg.exe"
Write-Host "3/7 Proveravam ukljuceni LGPL FFmpeg (OFFLINE)..."
if (!(Test-DownloadedFile -Path $FfmpegExe -MinimumBytes 1000000)) {
    throw "Nedostaje runtime\bin\ffmpeg.exe. SubtitleBoom v1.0 source paket mora da sadrzi provereni LGPL FFmpeg build."
}

$ffmpegVersionText = (& $FfmpegExe -version 2>&1 | Out-String)
if ($LASTEXITCODE -ne 0) {
    throw "Ukljuceni FFmpeg ne moze da se pokrene."
}
if ($ffmpegVersionText -match "--enable-gpl" -or $ffmpegVersionText -match "--enable-nonfree") {
    throw "Ukljuceni FFmpeg nije dozvoljeni LGPL build (pronadjen --enable-gpl ili --enable-nonfree)."
}
Write-Host "Proveren LGPL FFmpeg build: OK" -ForegroundColor Green

$TinyModel = Join-Path $RuntimeModels "ggml-tiny.bin"
$BaseModel = Join-Path $RuntimeModels "ggml-base.bin"
Write-Host "4/7 Pripremam lokalne Whisper modele Tiny i Base (OFFLINE)..."

Reuse-LocalFile `
    -FileName "ggml-tiny.bin" `
    -Destination $TinyModel `
    -MinimumBytes 74000000 `
    -SearchRoots $LocalSearchRoots `
    -Description "Whisper Tiny model"

Reuse-LocalFile `
    -FileName "ggml-base.bin" `
    -Destination $BaseModel `
    -MinimumBytes 140000000 `
    -SearchRoots $LocalSearchRoots `
    -Description "Whisper Base model"

Write-Host "5/7 Kompajliram SubtitleBoom..."
if (Test-Path $Publish) { Remove-Item -Recurse -Force $Publish }

& dotnet publish $Project `
    -c Release `
    -r win-x64 `
    --self-contained false `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $Publish

if ($LASTEXITCODE -ne 0) { throw "dotnet publish nije uspeo." }

# SubtitleBoom v1.0 is published only for Windows x64.
# VideoLAN.LibVLC.Windows contains native runtimes for x64, x86 and ARM64;
# remove architectures that cannot be used by this release to keep PROGRAM smaller.
$unusedLibVlcArchitectures = @(
    (Join-Path $Publish "libvlc\win-x86"),
    (Join-Path $Publish "libvlc\win-arm64")
)
foreach ($unusedArch in $unusedLibVlcArchitectures) {
    if (Test-Path $unusedArch) {
        Remove-Item -Recurse -Force $unusedArch
    }
}

$requiredLibVlcX64 = Join-Path $Publish "libvlc\win-x64\libvlc.dll"
if (!(Test-Path $requiredLibVlcX64)) {
    throw "Nedostaje x64 LibVLC runtime: $requiredLibVlcX64"
}

Write-Host "6/7 Kopiram licence i javne release fajlove..."
$programConfig = Join-Path $Publish "config"
New-Item -ItemType Directory -Path $programConfig -Force | Out-Null
Copy-Item (Join-Path $Root "LICENSE") (Join-Path $Publish "LICENSE") -Force
Copy-Item (Join-Path $Root "THIRD_PARTY_LICENSES.txt") (Join-Path $Publish "THIRD_PARTY_LICENSES.txt") -Force
Copy-Item (Join-Path $Root "README.md") (Join-Path $Publish "README.md") -Force
if (Test-Path (Join-Path $Root "third_party_licenses")) {
    Copy-Item (Join-Path $Root "third_party_licenses") (Join-Path $Publish "third_party_licenses") -Recurse -Force
}
Copy-Item (Join-Path $Root "config\donation.txt") (Join-Path $programConfig "donation.txt") -Force

Write-Host "7/7 Proveravam rezultat..."
$requiredFiles = @(
    (Join-Path $Publish "SubtitleBoom.exe"),
    (Join-Path $Publish "runtime\bin\whisper-cli.exe"),
    (Join-Path $Publish "runtime\bin\ffmpeg.exe"),
    (Join-Path $Publish "runtime\models\ggml-tiny.bin"),
    (Join-Path $Publish "runtime\models\ggml-base.bin")
)
foreach ($required in $requiredFiles) {
    if (!(Test-Path $required)) { throw "Nedostaje: $required" }
}

Write-Host ""
Write-Host "==================================================" -ForegroundColor Green
Write-Host " BUILD SUBTITLEBOOM v1.0 FIRST PUBLIC RELEASE JE USPESNO ZAVRSEN" -ForegroundColor Green
Write-Host "==================================================" -ForegroundColor Green
Write-Host ""
Write-Host "Otvori PROGRAM i pokreni SubtitleBoom.exe" -ForegroundColor Yellow
Write-Host ""
Read-Host "Pritisni Enter da zatvoris prozor"

