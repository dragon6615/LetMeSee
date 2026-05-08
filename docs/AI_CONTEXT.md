# AI Context

這份文件給 AI 或新維護者快速理解 LetMeSee。處理任務前，建議先讀本檔，再視任務讀 `README.md`、`BuildGuide.md` 與相關原始碼。

## 專案目的

LetMeSee 是一個 Windows 圖片檢視器，使用 WPF 與 .NET 9 製作。核心目標是快速開圖、同資料夾瀏覽、全螢幕檢視、縮放和平移、GIF 動畫播放、圖片狀態 overlay，以及目前使用者層級的 Windows 檔案關聯。

## 技術棧

- 語言：C#，nullable enabled，implicit usings enabled。
- UI：WPF，`net9.0-windows`，`UseWPF=true`。
- 專案型態：單一 Windows desktop app，輸出為 `WinExe`。
- 外部套件：目前沒有 NuGet package reference；`NuGet.config` 只啟用 nuget.org。
- 平台假設：Windows。檔案關聯、螢幕資訊與自然排序使用 Windows registry / shell / user32 / shlwapi API。

## 常用指令

```powershell
dotnet build
dotnet build -c Release
dotnet run -- "C:\Path\To\image.jpg"
dotnet publish -c Release -r win-x64 --self-contained true -o .\publish\win-x64
```

目前 repo 沒有自動化測試專案。修改後至少跑 `dotnet build`；涉及發佈時再跑 Release build 或 publish。

## 重要檔案

- `LetMeSee.csproj`：WPF/.NET 9 專案設定、版本與 icon。
- `Program.cs`：明確入口點。建立 `App`、讀第一個命令列參數、建立 `MainWindow`，並將啟動診斷寫到 `%LOCALAPPDATA%\LetMeSee\startup.log`。
- `App.xaml` / `App.xaml.cs`：目前只有基本 WPF application shell。
- `MainWindow.xaml`：主要 UI。包含隱藏選單列、黑色 viewport、`Canvas` + `Image`、載入/錯誤訊息、左下角資訊 overlay、右鍵選單。
- `MainWindow.xaml.cs`：大部分行為集中在這裡，包含載入、瀏覽、快捷鍵、縮放、全螢幕、另存、旋轉、GIF、刪除、剪貼簿與 overlay。
- `Services/ImageLoader.cs`：透過 WPF/WIC 載入圖片，解碼後快取 `BitmapSource`。
- `Services/AppSettings.cs`：讀寫 `%APPDATA%\LetMeSee\settings.json`，目前只有 `StartFullScreen`。
- `Services/FileAssociationRegistrar.cs`：註冊/取消註冊目前使用者的 Open With metadata 和圖片右鍵選單。
- `Register-FileAssociations.ps1` / `Unregister-FileAssociations.ps1`：檔案關聯輔助腳本，和 app 內建 Setup 選單用途相近。
- `Assets/AppIcon.ico`、`Assets/icon.png`：應用圖示資產。
- `BuildGuide.md`：建置與 publish 流程說明。
- `LICENSE.md`：授權限制。這不是開源授權專案。

## 核心流程

### 啟動

1. `Program.Main` 寫入 startup log。
2. 建立 WPF `App`，設定 `ShutdownMode.OnMainWindowClose`。
3. 取第一個命令列參數作為初始圖片路徑。
4. 建立 `MainWindow(imagePath)` 並執行。
5. `MainWindow.Loaded` 更新檔案關聯選單狀態、聚焦 viewport，然後載入初始圖片。

### 載入圖片

主要入口是 `MainWindow.LoadImageAsync`。

1. 將路徑正規化為 full path，遞增 `_imageLoadVersion` 避免舊 async load 覆蓋新狀態。
2. 停止舊 GIF timer，重置顯示旋轉。
3. 需要時重新列舉同資料夾圖片並更新 `_currentImageIndex`。
4. 透過 `ImageLoader.LoadAsync` 讀取靜態 bitmap。
5. 若副檔名是 `.gif`，額外嘗試 `TryLoadAnimatedGifAsync`。
6. 更新 `_currentImage`、`_currentAnimation`、`_currentSourceImageDetails`、window title、overlay 與 `ImageView.Source`。
7. 需要時進入全螢幕、調整視窗大小、fit to window。
8. 啟動 GIF timer，並預載前後各 2 張鄰近圖片。

### 同資料夾瀏覽

- 支援的副檔名集中在 `MainWindow.SupportedImageExtensions`。
- `RefreshFolderImages` 會列舉目前圖片所在資料夾。
- 排序使用 `LogicalPathComparer`，底層呼叫 `StrCmpLogicalW` 做 Windows 自然排序。
- `NavigateRelativeAsync` / `NavigateToIndexAsync` 負責切換圖片。
- `QueueNearbyImagesForCache` 會背景預載目前圖片前後各 2 張。

### GIF 動畫

GIF 動畫不只顯示 WPF decoder 的 frame。`LoadAnimatedGif` 會讀 frame metadata、delay、offset、disposal method，並把局部 frame 合成成完整畫布大小的 frame。播放使用 `DispatcherTimer`，每次 tick 更新 `ImageView.Source`。

修改 GIF 相關功能時要注意：

- 許多 GIF frame 是局部更新，不是完整影格。
- disposal method 支援 do not dispose、restore background、restore previous。
- frame delay 有最小值 20 ms，預設值 100 ms。
- 旋轉動畫 GIF 時，程式保留 `_displayRotationDegrees` 並對每一幀套用顯示旋轉。

### 視窗、縮放與全螢幕

- 圖片顯示尺寸由 `SetImageDisplaySize` 根據 DPI 換算成 WPF logical size。
- `FitToWindow` 設定 `_isFitMode=true`，依 viewport 尺寸計算 scale。
- `ActualSize` / zoom 會離開 fit mode。
- 圖片大於 viewport 時，方向鍵會先平移；沒有 overflow 時才切換圖片。
- `ToggleFullScreen` 會保存前一個視窗狀態，使用目前 monitor bounds，並將 `StartFullScreen` 寫入 settings。
- 視窗模式下雙擊圖片會切換標題列顯示，透過 `WindowChrome` 隱藏 caption。

### Save As、旋轉、刪除與剪貼簿

- Save As 支援 PNG/JPEG/BMP/GIF/TIFF。格式由副檔名決定，JPEG 使用 `QualityLevel=100`。
- Save As 目前只重編碼目前顯示中的 bitmap；不保留 GIF 動畫、ICC profile、EXIF 或其他 metadata。
- 靜態圖旋轉會建立 `TransformedBitmap` 並更新 `_currentImage`；動畫 GIF 則記錄顯示旋轉角度。
- Delete 使用 `Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(..., RecycleOption.SendToRecycleBin)`，刪除後切到下一張可用圖片，沒有剩餘圖片就清空狀態。
- `Ctrl+C` 會把目前圖片檔案以 file drop list 形式放入剪貼簿。

### 圖片資訊 Overlay

按 `V` 切換左下角 overlay。內容來自 `BuildImageInfoText`，包含檔名、路徑、檔案大小、來源解析度/格式/DPI/影格數/ICC profile，以及目前載入後 bitmap 的解析度/格式/DPI。

來源資訊由 `ReadImageSourceDetails` 透過 `BitmapDecoder.Create(..., BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad)` 讀第一個 frame。

### 檔案關聯

`FileAssociationRegistrar` 只寫目前使用者 registry，不需要 machine-wide 權限。它會：

- 建立 `LetMeSee.Image` ProgID。
- 建立 `Software\Classes\Applications\<exe>\Capabilities`。
- 把支援副檔名加入 Open With metadata。
- 加入 `SystemFileAssociations\image\shell\LetMeSee` 右鍵選單。
- 呼叫 `SHChangeNotify` 通知 shell 更新關聯。

注意：`FileAssociationRegistrar` 與 PowerShell 腳本目前只處理常見格式集合，不包含 RAW 與 HEIF/HEIC。`MainWindow.SupportedImageExtensions` 則包含 RAW 和 HEIF/HEIC。

## 開發慣例

- 優先維持小型架構；目前沒有 MVVM 分層，UI 事件與狀態集中在 `MainWindow.xaml.cs`。
- 不要為了小改動引入大型 framework 或額外 NuGet 套件。
- 圖片載入應避免鎖住來源檔案；`ImageLoader` 使用 memory copy、`BitmapCacheOption.OnLoad`、`Freeze()`。
- 可被背景或重複使用的 `BitmapSource` 應 `Freeze()`。
- 非關鍵背景工作，例如預載，失敗時不要中斷可見圖片載入。
- 修改支援格式時，同步檢查 `SupportedImageExtensions`、OpenFileDialog filter、README、About message、檔案關聯服務與腳本是否需要更新。
- 修改快捷鍵或 UI 行為時，同步更新 `README.md` 的快捷鍵與功能描述。
- 修改 publish/build 流程時，同步更新 `BuildGuide.md`。
- `bin/`、`obj/`、`publish/` 是產物，不要提交或手動維護。

## 已知限制與注意事項

- 沒有自動化測試；WPF 互動主要靠 build 與手動測試。
- Save As 不保留 metadata，也不保留動畫 GIF，只存目前 frame。
- 實際可解碼格式取決於 Windows Imaging Component 與使用者已安裝 codec。
- 檔案關聯更動後 Windows Explorer 可能需要重開或重新登入才完全刷新。
- `ImageLoader` 快取上限預設 512 MB；大圖超過上限不會被加入快取。
- `MainWindow.xaml.cs` 是高耦合核心檔，修改時要小心狀態欄位之間的互動，特別是 `_currentImage`、`_currentAnimation`、`_imageLoadVersion`、`_isFitMode`、`_isFullScreen`。

## 建議驗證

最小驗證：

```powershell
dotnet build
```

涉及功能時建議手動檢查：

- 從命令列開啟圖片。
- 使用 Open dialog 與 drag/drop 開圖。
- 同資料夾上一張/下一張、Home/End。
- 滑鼠滾輪瀏覽，Ctrl+滾輪縮放。
- fit、actual size、平移、旋轉。
- GIF 動畫播放與旋轉。
- `V` overlay 顯示資訊。
- Save As 輸出不同格式。
- Delete 是否進資源回收桶並切到下一張。
- 全螢幕切換、Esc、視窗模式雙擊隱藏標題列。
- 檔案關聯註冊/取消註冊，若任務涉及 registry。

## 給下一個 AI 的接手提示

開始做任務時先確認：

1. 這是 UI 行為、圖片載入、檔案關聯、建置發佈，還是文件更新。
2. 如果要改支援格式，搜尋所有格式清單，不要只改一處。
3. 如果要改 GIF 或縮放，先讀完整相關 method，狀態欄位互相影響。
4. 如果要改檔案關聯，確認 app 內 `FileAssociationRegistrar` 和兩個 PowerShell 腳本是否都要同步。
5. 完成後跑 `dotnet build`，並在回覆中說明有沒有手動測試限制。
