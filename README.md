# LetMeSee

LetMeSee is a lightweight Windows image viewer built with WPF and .NET 9. It focuses on fast image opening, keyboard-driven browsing, fullscreen viewing, animated GIF playback, and simple image inspection tools.

## Features

- Open image files from command-line arguments, the file dialog, or drag and drop.
- Browse images in the same folder with arrow keys, mouse wheel, PageUp/PageDown, Home, and End.
- Automatically starts in fullscreen by default, with `F` or `Enter` to toggle fullscreen.
- Zoom with `Ctrl` + mouse wheel, `+`, and `-`.
- Fit to window with `*`; show actual size with `/`.
- Pan oversized images with arrow keys.
- Copy the current image file with `Ctrl+C`.
- Delete the current image file with `Delete`; the file is sent to the Recycle Bin.
- Right-click context menu:
  - Open file
  - Save As
  - Rotate left 90 degrees
  - Rotate right 90 degrees
  - Exit
- Save As supports PNG, JPEG, BMP, GIF, and TIFF. JPEG quality is set to 100.
- GIF animation playback is supported.
- Press `V` to show or hide image details in the lower-left corner.
- Optional current-user file association and Explorer image context-menu registration.

## Supported Formats

The app currently accepts these extensions:

- JPEG: `.jpg`, `.jpeg`
- PNG: `.png`
- BMP: `.bmp`
- GIF: `.gif`
- WebP: `.webp`
- TIFF: `.tif`, `.tiff`
- RAW formats supported by Windows Imaging Component codecs: `.cr2`, `.cr3`, `.nef`, `.arw`, `.raf`, `.orf`, `.rw2`, `.dng`
- HEIF/HEIC if the required Windows codecs are installed: `.heic`, `.heif`

Actual decoding depends on Windows Imaging Component and the codecs installed on the user's machine.

## Keyboard Shortcuts

| Key | Action |
| --- | --- |
| `Ctrl+O` | Open image |
| `F` / `Enter` | Toggle fullscreen |
| `Esc` | Close fullscreen window or exit |
| Mouse wheel | Previous / next image |
| `Ctrl` + mouse wheel | Zoom in / out |
| `+` / `-` | Zoom in / out |
| `/` | Actual size |
| `*` | Fit to window |
| Arrow keys | Pan if zoomed, otherwise browse images |
| `PageUp` / `PageDown` | Previous / next image |
| `Home` / `End` | First / last image in folder |
| `Ctrl+C` | Copy current image file |
| `Delete` | Send current image file to Recycle Bin |
| `V` | Show / hide image details overlay |

## Image Details Overlay

Press `V` to display a red, bold overlay in the lower-left corner. It shows information the app can directly inspect, including:

- File name and full path
- File size
- Source image resolution
- Source pixel format and bits per pixel
- Source DPI
- Source frame count
- Whether an embedded ICC/profile was detected
- Current loaded bitmap resolution
- Current loaded bitmap pixel format and bits per pixel
- Current loaded bitmap DPI

## Design Overview

The project is intentionally small and centered around a single WPF window.

### `Program.cs`

`Program` is the explicit application entry point. It:

- Creates the WPF `App`.
- Reads the first command-line argument as an optional image path.
- Creates and runs `MainWindow`.
- Writes startup diagnostics to `%LOCALAPPDATA%\LetMeSee\startup.log`.

### `MainWindow.xaml`

Defines the main UI:

- A hidden menu bar for file, view, setup, and help actions.
- A black image viewport.
- A `Canvas` containing the displayed `Image`.
- A centered message area for loading and error messages.
- A lower-left image details overlay.
- A right-click context menu.

### `MainWindow.xaml.cs`

Contains most of the application behavior:

- Image loading orchestration.
- Folder enumeration and natural path sorting.
- Keyboard and mouse interaction.
- Zoom, fit-to-window, actual-size display, panning, and resizing.
- Fullscreen and title-bar visibility handling.
- Save As encoding.
- Rotation of the currently displayed image.
- Animated GIF playback.
- Image details overlay generation.
- Delete and clipboard operations.

### `Services/ImageLoader.cs`

Loads images through WPF/WIC and caches decoded `BitmapSource` instances.

Important details:

- Uses `BitmapCacheOption.OnLoad`, so the source file is not held open after decoding.
- Copies image bytes into memory before constructing `BitmapImage`.
- Freezes decoded bitmaps so they can be safely reused.
- Maintains an LRU cache capped at 512 MB by default.
- Cache entries are invalidated if file length or last-write time changes.

### Animated GIF Handling

GIF animation is handled separately from normal static image loading.

The app uses `GifBitmapDecoder` to read GIF frames, frame delays, frame offsets, and disposal metadata. Frames are composited into full canvas-sized bitmaps before playback. This is required because many GIF frames are partial updates rather than complete images.

Playback is driven by a WPF `DispatcherTimer`, and each timer tick updates `ImageView.Source` to the next precomposited frame.

### `Services/FileAssociationRegistrar.cs`

Registers and unregisters current-user Windows file association information:

- Creates a `LetMeSee.Image` ProgID.
- Adds supported image extensions to Open With metadata.
- Adds a current-user Explorer image right-click command: `Open with LetMeSee`.
- Notifies the shell after changes.

This does not require machine-wide registry writes.

### `Services/AppSettings.cs`

Stores simple user settings in:

```text
%APPDATA%\LetMeSee\settings.json
```

Currently it stores whether the app should start in fullscreen.

## Save As Behavior

Save As re-encodes the currently displayed bitmap. It supports:

- PNG
- JPEG
- BMP
- GIF
- TIFF

The target format is selected by the output file extension. JPEG is encoded with quality level `100`.

Save As does not currently preserve animation, ICC profiles, EXIF, or other metadata. For animated GIFs, it saves the currently displayed frame as a single image.

## Build

Requirements:

- Windows
- .NET 9 SDK

Build from the repository root:

```powershell
dotnet build
```

Run:

```powershell
dotnet run -- "C:\Path\To\image.jpg"
```

Publish a Windows x64 build:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true
```

Build output is intentionally ignored by Git.

## File Association Scripts

The repository includes helper scripts:

- `Register-FileAssociations.ps1`
- `Unregister-FileAssociations.ps1`

These scripts are alternatives to the in-app setup menu and update current-user registry entries.

## Repository Hygiene

The `.gitignore` excludes local build output and machine-specific files:

- `bin/`
- `obj/`
- `publish/`
- `.vs/`
- IDE user files
- logs, test output, coverage output, and OS thumbnail files

Source files, assets, project files, and helper scripts are intended to be committed.
