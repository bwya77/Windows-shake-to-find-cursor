# Windows Shake to Find Cursor

A tiny Windows desktop app inspired by macOS cursor discovery. Shake the mouse back and forth for at least one second and the native Windows cursor itself grows based on shake energy: tighter, faster oscillation makes it larger, slowing down makes it shrink, and a hard cap keeps it from growing without bound. The enlarged cursor currently caps at 288px tall.

The app does not draw an overlay. It temporarily replaces the native Windows cursor with cached generated cursor frames, then restores the normal system cursor when shaking stops. Detection uses a short sliding window so straight-line movement does not trigger growth, and it is suppressed while mouse buttons are held so dragging and selection keep working normally.

Right-click the tray icon and choose **Settings** to adjust the maximum cursor size, how long shaking must continue before growth starts, and how long the cursor holds at peak size before shrinking.

## Run

```powershell
dotnet run --project C:\Git\GitHub\Windows-shake-to-find-cursor\ShakeToBigCursor.csproj
```

The app lives in the system tray. Right-click the tray icon and choose **Exit** to quit.

## Build

```powershell
dotnet publish C:\Git\GitHub\Windows-shake-to-find-cursor\ShakeToBigCursor.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

## Installer, startup, and updates

Release builds include signed portable zips and Inno Setup installers for x64 and arm64. The installer can register **Start at login**, and the tray menu can toggle that setting later without elevation by using the per-user `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` key.

The tray menu also includes **Check for updates**. The app checks GitHub Releases, downloads the matching installer for the current architecture when available, and launches it silently so installed copies can update in place.

## Releases

Every push to `main` runs `.github/workflows/release.yml`. The workflow publishes self-contained x64 and arm64 builds, signs binaries and installers when Azure Trusted Signing secrets are configured, creates SHA-256 checksums and provenance attestation, and publishes a GitHub release tagged as `v<version>-build.<run number>`.
