# LetMeSee

LetMeSee 是一個以 WPF 與 .NET 9 製作的輕量 Windows 圖片檢視器。設計重點是快速開圖、鍵盤瀏覽、全螢幕檢視、GIF 動畫播放，以及能直接檢查目前圖片載入後狀態的資訊 overlay。

## 功能

- 可從命令列參數、開檔對話框、拖放檔案開啟圖片。
- 可用方向鍵、滑鼠滾輪、PageUp/PageDown、Home、End 瀏覽同資料夾圖片。
- 預設以全螢幕開啟；可用 `F` 或 `Enter` 切換全螢幕。
- 可用 `Ctrl` + 滑鼠滾輪、`+`、`-` 縮放圖片。
- `*` 可符合視窗大小；`/` 可顯示實際大小。
- 圖片大於可視範圍時，可用方向鍵平移。
- `Ctrl+C` 可複製目前圖片檔案。
- `Delete` 可將目前圖片送到資源回收桶，並切換到下一張可用圖片。
- 右鍵功能選單：
  - 開啟檔案
  - Save As
  - 向左旋轉 90 度
  - 向右旋轉 90 度
  - 離開
- Save As 支援 PNG、JPEG、BMP、GIF、TIFF；JPEG 品質設定為 `100`。
- 支援 GIF 動畫播放。
- 按 `V` 可顯示或隱藏左下角圖片詳細資訊。
- 可切換目前使用者的檔案關聯與 Windows 檔案總管圖片右鍵選單。

## 支援格式

目前接受以下副檔名：

- JPEG：`.jpg`, `.jpeg`
- PNG：`.png`
- BMP：`.bmp`
- GIF：`.gif`
- WebP：`.webp`
- TIFF：`.tif`, `.tiff`
- 透過 Windows Imaging Component codec 支援的 RAW：`.cr2`, `.cr3`, `.nef`, `.arw`, `.raf`, `.orf`, `.rw2`, `.dng`
- 若 Windows 安裝必要 codec，則可支援 HEIF/HEIC：`.heic`, `.heif`

實際能否解碼取決於 Windows Imaging Component 與使用者電腦已安裝的 codec。

## 快捷鍵

| 按鍵 | 功能 |
| --- | --- |
| `Ctrl+O` | 開啟圖片 |
| `F` / `Enter` | 切換全螢幕 |
| `Esc` | 關閉全螢幕視窗或離開 |
| 滑鼠滾輪 | 上一張 / 下一張 |
| `Ctrl` + 滑鼠滾輪 | 放大 / 縮小 |
| `+` / `-` | 放大 / 縮小 |
| `/` | 實際大小 |
| `*` | 符合視窗大小 |
| 方向鍵 | 圖片放大時平移，否則切換圖片 |
| `PageUp` / `PageDown` | 上一張 / 下一張 |
| `Home` / `End` | 同資料夾第一張 / 最後一張 |
| `Ctrl+C` | 複製目前圖片檔案 |
| `Delete` | 將目前圖片送到資源回收桶 |
| `V` | 顯示 / 隱藏圖片詳細資訊 |

## 圖片詳細資訊

按 `V` 後，左下角會顯示紅色粗體資訊 overlay。內容包含程式能直接掌握的狀態：

- 檔名與完整路徑
- 檔案大小
- 來源圖片解析度
- 來源 pixel format 與 bits per pixel
- 來源 DPI
- 來源影格數
- 是否偵測到內嵌 ICC/profile
- 目前載入後 bitmap 解析度
- 目前載入後 bitmap pixel format 與 bits per pixel
- 目前載入後 bitmap DPI

## AI 接手文件

給 AI 或新維護者快速理解專案的摘要請見 [docs/AI_CONTEXT.md](docs/AI_CONTEXT.md)。

## 程式設計概要

專案刻意維持小型架構，核心集中在單一 WPF 視窗與幾個服務類別。

### `Program.cs`

明確的程式進入點，負責：

- 建立 WPF `App`。
- 讀取第一個命令列參數作為可選圖片路徑。
- 建立並執行 `MainWindow`。
- 將啟動診斷寫入 `%LOCALAPPDATA%\LetMeSee\startup.log`。

### `MainWindow.xaml`

定義主要 UI：

- 預設隱藏的選單列。
- 黑色圖片 viewport。
- 用於顯示圖片的 `Canvas` 與 `Image`。
- 置中的載入與錯誤訊息。
- 左下角圖片詳細資訊 overlay。
- 右鍵功能選單。

### `MainWindow.xaml.cs`

包含大多數應用程式行為：

- 圖片載入流程。
- 同資料夾圖片列舉與自然排序。
- 鍵盤與滑鼠操作。
- 縮放、符合視窗、實際大小、平移、視窗尺寸調整。
- 全螢幕與標題列顯示控制。
- Save As 編碼。
- 目前顯示圖片旋轉。
- GIF 動畫播放。
- 圖片詳細資訊 overlay 產生。
- 刪除檔案與複製檔案到剪貼簿。

### `Services/ImageLoader.cs`

透過 WPF/WIC 載入圖片並快取解碼後的 `BitmapSource`。

設計重點：

- 使用 `BitmapCacheOption.OnLoad`，解碼後不會持續鎖住來源檔案。
- 先將圖片檔案複製到記憶體，再建立 `BitmapImage`。
- 解碼後呼叫 `Freeze()`，方便安全重複使用。
- 預設 LRU 快取上限為 512 MB。
- 若檔案大小或最後修改時間改變，快取會失效。

### GIF 動畫處理

GIF 動畫不走一般靜態圖片播放路徑，而是額外用 `GifBitmapDecoder` 讀取：

- frame
- frame delay
- frame offset
- disposal metadata

很多 GIF frame 不是完整畫面，而是局部更新區塊。因此程式會先將每個 frame 合成為完整畫布大小的 bitmap，再透過 WPF `DispatcherTimer` 逐 frame 更新 `ImageView.Source` 播放。

### `Services/FileAssociationRegistrar.cs`

負責註冊與取消註冊目前使用者的 Windows 檔案關聯資訊：

- 建立 `LetMeSee.Image` ProgID。
- 將支援的圖片副檔名加入 Open With metadata。
- 加入目前使用者的 Windows 檔案總管圖片右鍵指令：`Open with LetMeSee`。
- 修改後通知 shell 更新關聯。

這些操作不需要寫入 machine-wide registry。

### `Services/AppSettings.cs`

儲存簡單使用者設定：

```text
%APPDATA%\LetMeSee\settings.json
```

目前儲存是否預設全螢幕啟動。

## Save As 行為

Save As 會重新編碼目前顯示中的 bitmap，支援：

- PNG
- JPEG
- BMP
- GIF
- TIFF

輸出格式由存檔副檔名決定。JPEG 使用 `QualityLevel = 100`。

目前 Save As 不保留動畫、ICC profile、EXIF 或其他 metadata。對動畫 GIF 來說，Save As 會存下目前顯示的單一 frame。

## 建置

需求：

- Windows
- .NET 9 SDK

在 repository 根目錄建置：

```powershell
dotnet build
```

執行：

```powershell
dotnet run -- "C:\Path\To\image.jpg"
```

發佈 Windows x64 版本：

```powershell
dotnet publish -c Release -r win-x64 --self-contained true
```

建置輸出不會提交到 Git。

## 檔案關聯腳本

repository 包含兩個輔助腳本：

- `Register-FileAssociations.ps1`
- `Unregister-FileAssociations.ps1`

這些腳本是 app 內建 Setup 選單之外的替代方式，用於更新目前使用者的 registry 關聯設定。

## Repository 管理

`.gitignore` 會排除本機建置輸出與機器相關檔案：

- `bin/`
- `obj/`
- `publish/`
- `.vs/`
- IDE user files
- logs、test output、coverage output、OS thumbnail files

原始碼、assets、project files、helper scripts 會保留並提交。

## 授權

本專案不是開源授權。原始碼公開供瀏覽用途，未授權複製、修改、散布、再發布、轉授權，或用於任何商業或非商業產品。

詳細條款請見 [LICENSE.md](LICENSE.md)。

---

# English

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

## AI Handoff Notes

For a compact project briefing for AI assistants or new maintainers, see [docs/AI_CONTEXT.md](docs/AI_CONTEXT.md).

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

## License

This project is not open source. The source code is public for viewing purposes only. No permission is granted to copy, modify, distribute, republish, sublicense, or use this code in any commercial or non-commercial product.

See [LICENSE.md](LICENSE.md) for details.
