# DMA Launcher v1.0.0

**GitHub:** https://github.com/yoHan0422/DMA

- Developer source baseline: **DMA v0.2.31** (unchanged).
- Launcher: **C# / Windows Forms / .NET 10 / Windows x64 self-contained**.
- User sees one `DMA_Launcher.exe` with **실행**, **복구 중지**, progress and diagnostics.
- Download uses public GitHub Releases from `yoHan0422/DMA`, with releases tagged `v0.2.31`, `v0.2.32` and assets `DMA_v0.2.31.zip`, `DMA_v0.2.32.zip`, etc.
- Auto update downloads newest versioned release asset. Downloads optional `DMA_v<version>.zip.sha256` and verifies content when present.
- Installs in `%LOCALAPPDATA%\DMA\Launcher\versions\<version>\` and keeps DMA user settings under `%LOCALAPPDATA%\DMA` untouched.
- Confirms `DMA.exe` is running and a main window is visible before reporting startup success. It does **not** verify in-game interaction.
- If installation, download or startup fails, automatically diagnoses from exceptions, retries download/installation/startup **without any maximum attempt count**, until the DMA main window is shown or the user presses **복구 중지** / closes launcher. Retrying an already installed crashing version forces a fresh download and install.
- Logs: `%LOCALAPPDATA%\DMA\Launcher\launcher.log`.
- No login server; no .NET SDK needed for guild members; no DMA source code delivered.

## 1. Create the launcher EXE with GitHub Actions

Commit `Launcher/DMA.Launcher.csproj`, `Launcher/Program.cs`, `.github/workflows/build-launcher.yml` to GitHub repository `yoHan0422/DMA`. The Windows GitHub Actions workflow builds a **self-contained** `DMA_Launcher.exe` and publishes it under GitHub Release `launcher-v1.0.0`.

This project was written in an environment without Windows/.NET 10 SDK. **A GitHub Actions build and a test on a real Windows machine are required** before calling the EXE verified.

## 2. Build **DMA v0.2.31** developer-side and publish binaries only

**Important: the provided `DMA_v0.2.31.zip` from the conversation is a SOURCE ZIP. Do not upload it as the guild distribution asset.**

On a Windows 10/11 x64 developer machine (with internet), after signing in via GitHub CLI (`gh auth login`), run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Publish-DMA-v0.2.31.ps1 -SourceZip "C:\path\to\DMA_v0.2.31.zip" -Upload
```

The script extracts to a temporary copy, builds **using the original build.ps1 with -SkipAI**, requires the existing WPF self-test to pass, ZIPs **only** the `publish\` runtime files into `dist\DMA_v0.2.31.zip`, generates `dist\DMA_v0.2.31.zip.sha256`, and uploads them to GitHub Release `v0.2.31`.

Without GitHub CLI, omit `-Upload`, then manually open GitHub > Releases > Draft a new release > tag `v0.2.31` and attach the two generated files from `dist\`.

**The original v0.2.31 source archive is never modified or uploaded to the public GitHub repo.**

## 3. Guild member procedure

1. Download `DMA_Launcher.exe` from the `launcher-v1.0.0` GitHub Release.
2. Open it and click **실행**.
3. The launcher fetches and installs the latest `DMA_vX.zip` release asset and launches `DMA.exe`.
4. On later launches it checks for newer DMA versions. On failure it automatically retries without any fixed attempt limit.

## Distribution verification status

- Launcher source: produced.
- Repo upload / GitHub Actions build: check the GitHub Actions tab.
- DMA v0.2.31 Windows runtime: **not built or verified in this environment**.
- Actual guild-member startup: **not verified**.