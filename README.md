# Windows Shake to Find Cursor

A tiny Windows desktop app inspired by macOS cursor discovery. Shake the mouse back and forth briefly and the native Windows cursor itself grows based on shake energy: tighter, faster oscillation makes it larger, slowing down makes it shrink, and a hard cap keeps it from growing without bound. The enlarged cursor currently caps at 288px tall.

The app does not draw an overlay. It temporarily replaces the native Windows cursor with cached generated cursor frames, then restores the normal system cursor when shaking stops. Detection uses a short sliding window so straight-line movement does not trigger growth, and it is suppressed while mouse buttons are held so dragging and selection keep working normally.

## Run

```powershell
dotnet run --project C:\Git\GitHub\Windows-shake-to-find-cursor\ShakeToBigCursor.csproj
```

The app lives in the system tray. Right-click the tray icon and choose **Exit** to quit.

## Build

```powershell
dotnet publish C:\Git\GitHub\Windows-shake-to-find-cursor\ShakeToBigCursor.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

## Releases

Every push runs `.github/workflows/release.yml`, publishes a self-contained win-x64 build, zips the binaries, and creates a GitHub release named `build-<run number>`.
