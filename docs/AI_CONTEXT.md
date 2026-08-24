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
dotnet publish -c Release -r win-x64 --self-contained true
```

目前 repo 沒有自動化測試專案。修改後至少跑 `dotnet build`；涉及發佈時再跑 Release build 或 publish。

## 重要檔案

- `LetMeSee.csproj`：WPF/.NET 9 專案設定、版本與 icon。
- `Program.cs`：明確入口點。建立 `App`、讀第一個命令列參數、建立 `MainWindow`。
- `Services/DiagnosticLog.cs`：診斷紀錄，寫到 `%LOCALAPPDATA%\LetMeSee\letmesee.log`，超過 256 KB 輪替成 `.old`。記錄啟動/結束、圖片載入（尺寸、動畫幀數、耗時、資料夾位置）、載入失敗、資料夾列舉、刪除、另存、檔案關聯的讀取與套用、未處理例外。「說明 > 開啟診斷紀錄」可直接開啟。
- `App.xaml` / `App.xaml.cs`：目前只有基本 WPF application shell。
- `MainWindow.xaml`：主要 UI。包含選單列、黑色 viewport、`Canvas` + `Image`、載入/錯誤訊息、左下角資訊 overlay、右鍵選單。
- `MainWindow.xaml.cs`：大部分行為集中在這裡，包含載入、瀏覽、快捷鍵、縮放、全螢幕、另存、旋轉、GIF、刪除、剪貼簿與 overlay。
- `Services/ImageLoader.cs`：透過 WPF/WIC 載入圖片，解碼後快取 `BitmapSource`。
- `Services/AppSettings.cs`：讀寫 `%APPDATA%\LetMeSee\settings.json`，目前只有 `StartFullScreen`。
- `Services/FileAssociationRegistrar.cs`：讀取與套用目前使用者的 Open With metadata 和圖片右鍵選單，支援逐一副檔名。
- `Services/SupportedImageFormats.cs`：支援格式的唯一來源，瀏覽、開檔對話框、About 與檔案關聯都讀這裡。
- `FileAssociationsWindow.xaml` / `.xaml.cs`：檔案關聯設定頁面。
- `Register-FileAssociations.ps1` / `Unregister-FileAssociations.ps1`：檔案關聯輔助腳本，和 app 內建 Setup 選單用途相近。
- `Assets/AppIcon.ico`、`Assets/icon.png`：應用圖示資產。
- `BuildGuide.md`：建置、publish 與自動發佈流程說明。
- `.github/workflows/release.yml`：推 `v*` tag 就自動 publish、打包安裝程式並建立 GitHub Release，版本號取自 tag。
- `Services/DefaultProgramPrompt.cs`：包裝 `SHOpenWithDialog`，請 Windows 跳出「開啟方式」對話框。
- `installer/LetMeSee.iss`：Inno Setup 腳本。安裝到 Program Files（需管理員）、不建立檔案關聯、反安裝時清掉 app 寫的 per-user 關聯。`AppId` 的 GUID 不可更改。
- `LICENSE.md`：授權限制。這不是開源授權專案。

## 核心流程

### 啟動

1. `Program.Main` 寫入診斷紀錄。
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
5. 若副檔名是 `.gif`，額外嘗試 `TryLoadAnimatedGifAsync`；合成在背景 STA 執行緒執行。
6. 更新 `_currentImage`、`_currentAnimation`、`_currentSourceImageDetails`、window title、overlay 與 `ImageView.Source`。
7. 需要時進入全螢幕、調整視窗大小、fit to window。
8. 啟動 GIF timer，並預載前後各 2 張鄰近圖片。

### 同資料夾瀏覽

- 支援的副檔名集中在 `Services/SupportedImageFormats.cs`。
- `RefreshFolderImages` 會列舉目前圖片所在資料夾，但列舉結果會被快取；只有換資料夾或 `FileSystemWatcher` 標記 `_areFolderImagesStale` 時才重新列舉，避免每次瀏覽都在 UI thread 打磁碟。
- 排序使用 `LogicalPathComparer`，底層呼叫 `StrCmpLogicalW` 做 Windows 自然排序。
- `NavigateRelativeAsync` / `NavigateToIndexAsync` 負責切換圖片。
- `NavigateRelativeAsync` 以 `_requestedImagePath`（最後一次要求載入的圖片）為基準，不是以 `_currentImagePath`（最後一次載入完成的圖片），否則快速連按或滾輪會一直從同一張重新出發。
- `QueueNearbyImagesForCache` 會背景預載目前圖片前後各 2 張，並吃目前載入的 `CancellationToken`，被新的瀏覽取代時就停手。

### GIF 動畫

GIF 動畫不只顯示 WPF decoder 的 frame。`LoadAnimatedGif` 會讀 frame metadata、delay、offset、disposal method，並把局部 frame 合成成完整畫布大小的 frame。播放使用 `DispatcherTimer`，每次 tick 更新 `ImageView.Source`；tick 只在影格尺寸改變時才重算 title 與 overlay。

修改 GIF 相關功能時要注意：

- 許多 GIF frame 是局部更新，不是完整影格。
- disposal method 支援 do not dispose、restore background、restore previous。
- frame delay 有最小值 20 ms，預設值 100 ms。
- 旋轉動畫 GIF 時，程式保留 `_displayRotationDegrees` 並對每一幀套用顯示旋轉。
- 合成透過 `RunOnBackgroundRenderThreadAsync` 在背景 STA 執行緒進行（`RenderTargetBitmap` 需要 dispatcher thread），frame 一律 `Freeze()` 後才交給 UI thread。
- 預估合成後大小超過 `MaxAnimationBytes`（384 MB）的 GIF 不會播放動畫，改為顯示靜態第一幀。
- `GetGifRepeatCount` 讀 NETSCAPE2.0 application extension 的循環次數，0 表示無限循環；播完指定次數後 `StopAnimationTimer` 會停在最後一幀。

### 視窗、縮放與全螢幕

- 圖片顯示尺寸由 `SetImageDisplaySize` 根據 DPI 換算成 WPF logical size。
- `FitToWindow` 設定 `_isFitMode=true`，依 viewport 尺寸計算 scale。
- `ActualSize` / zoom 會離開 fit mode。
- `1` / `2` / `3`（含 numpad）透過 `SetFixedScale` 切換 1x / 2x / 3x，視窗模式下視窗會跟著縮放後尺寸調整。
- 視窗最小尺寸固定為 `MinimumWindowWidth` / `MinimumWindowHeight`，不會隨圖片大小拉高，使用者永遠可以把視窗縮小。
- 圖片大於 viewport 時，方向鍵會先平移；沒有 overflow 時才切換圖片。滑鼠左鍵拖曳同理：有 overflow 就平移圖片，沒有才拖動視窗。
- `Ctrl` + 滾輪縮放會透過 `GetImageRatioAt` / `RestoreZoomAnchor` 保持游標下的點不動。
- `ToggleFullScreen` 會保存前一個視窗狀態，使用目前 monitor bounds，並將 `StartFullScreen` 寫入 settings。
- 視窗模式下雙擊圖片會切換標題列顯示，透過 `WindowChrome` 隱藏 caption；離開全螢幕時標題列一律還原。
- 功能表是視窗模式版面的一部分。`ToggleMainMenuVisibility` 直接切換顯示，只有在視窗模式下才會更新 `_isMenuHiddenByUser`（全螢幕下的顯示只是暫時 peek）；離開全螢幕時 `ApplyMainMenuVisibility` 依這個旗標還原。
- `Alt` 單獨輕按會叫出功能表：`Window_KeyDown` 用 `_isAltTapCandidate` 記錄「這次 Alt 沒有搭配其他鍵」，`Window_KeyUp` 才動作。視窗模式下功能表已經在畫面上時不攔截，交給 WPF 做標準的 Alt 功能表操作。
- `Esc` 不再攔截功能表、一律關閉視窗。

### Save As、旋轉、刪除與剪貼簿

- Save As 支援 PNG/JPEG/BMP/GIF/TIFF。格式由副檔名決定，JPEG 使用 `QualityLevel=100`。
- Save As 目前只重編碼目前顯示中的 bitmap；不保留 GIF 動畫、ICC profile、EXIF 或其他 metadata。
- 靜態圖旋轉會建立 `TransformedBitmap` 並更新 `_currentImage`；動畫 GIF 則記錄顯示旋轉角度。
- Delete 會先跳確認對話框，確認後才用 `Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(..., RecycleOption.SendToRecycleBin)`，刪除後切到下一張可用圖片，沒有剩餘圖片就清空狀態。
- `Ctrl+C` 會把目前圖片檔案以 file drop list 形式放入剪貼簿。

### 圖片資訊 Overlay

按 `V` 切換左下角 overlay。內容來自 `BuildImageInfoText`，包含檔名、路徑、檔案大小、來源解析度/格式/DPI/影格數/ICC profile，以及目前載入後 bitmap 的解析度/格式/DPI。

來源資訊由 `ReadImageSourceDetails` 透過 `BitmapDecoder.Create(..., BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad)` 讀第一個 frame。

### 檔案關聯

`FileAssociationRegistrar` 只寫目前使用者 registry，不需要 machine-wide 權限。

功能表「設定 > 檔案關聯...」開啟 `FileAssociationsWindow`。這個頁面每次開啟時重新讀 registry 決定勾選狀態，不記在 settings 裡，所以顯示的一定是系統當下的真實狀態。

- `GetRegisteredExtensions()`：逐一檢查 `Software\Classes\<ext>\OpenWithProgids` 底下有沒有 `LetMeSee.Image`。
- `IsImageContextMenuRegistered()`：檢查 `SystemFileAssociations\image\shell\LetMeSee\command`。
- `GetRegisteredExecutablePath()`：從 ProgID 的 open command 解析出執行檔路徑；和目前執行檔不同時，設定頁面會提示關聯指向舊的複本，套用會改指到目前這一份。
- `Apply(extensions, addImageContextMenu)`：建立 ProgID / `Capabilities` / Open With metadata，勾選的副檔名寫入、沒勾的移除（`SupportedTypes` 和 `FileAssociations` 兩個 key 會整個重建），最後 `SHChangeNotify`。兩個參數都空就呼叫 `Unregister()` 整組移除。

副檔名清單一律來自 `SupportedImageFormats.Extensions`，registrar 和兩個 PowerShell 腳本都涵蓋 RAW 與 HEIF/HEIC。

重要區別：這些註冊只讓 LetMeSee **出現在「開啟方式」清單與預設應用程式清單**，不等於預設開啟程式。真正的預設在 `HKCU\...\Explorer\FileExts\<ext>\UserChoice`，Windows 8 之後用帶簽章的 `Hash` 保護，程式寫不進去也不該嘗試。設定頁面每一列右側會用 `DescribeDefaultHandler` 顯示該副檔名目前的預設程式（讀 `UserChoice` 的 ProgID），視窗 `Activated` 時重新讀取，所以去 Windows 設定改完切回來就會更新。

指定預設有兩條路，都由使用者按下最後一步：

- 圖片右鍵「設為 .jpg 的預設開啟程式...」→ `Services/DefaultProgramPrompt.cs` 呼叫 `SHOpenWithDialog`，帶 `OAIF_ALLOW_REGISTRATION | OAIF_REGISTER_EXT`，跳出 Windows 標準的「開啟方式」對話框。
- 設定頁面的按鈕 → `ms-settings:defaultapps?registeredAppUser=LetMeSee`。

注意 `IsOurProgId`：使用者若是用「瀏覽到執行檔」指定的，Windows 記的是 `Applications\LetMeSee.exe` 而不是 `LetMeSee.Image`，兩者都要算成 LetMeSee。套用後對話框不會關閉，方便使用者接著設定預設。

## 開發慣例

- 優先維持小型架構；目前沒有 MVVM 分層，UI 事件與狀態集中在 `MainWindow.xaml.cs`。
- 不要為了小改動引入大型 framework 或額外 NuGet 套件。
- UI 文案統一使用繁體中文，包含選單、對話框標題、訊息與 overlay；新增字串時不要混用英文。
- 圖片載入應避免鎖住來源檔案；`ImageLoader` 使用 memory copy、`BitmapCacheOption.OnLoad`、`Freeze()`。
- 可被背景或重複使用的 `BitmapSource` 應 `Freeze()`。
- 非關鍵背景工作，例如預載，失敗時不要中斷可見圖片載入。
- 修改支援格式時只改 `Services/SupportedImageFormats.cs`；瀏覽判斷、OpenFileDialog filter、About 清單與檔案關聯都由它衍生。另外要同步 README 與兩個 PowerShell 腳本。
- 修改快捷鍵或 UI 行為時，同步更新 `README.md` 的快捷鍵與功能描述。
- 修改 publish/build 流程時，同步更新 `BuildGuide.md`、`.github/workflows/release.yml` 與 `installer/LetMeSee.iss`。
- `bin/`、`obj/`、`publish/` 是產物，不要提交或手動維護。
- publish 一律用 .NET 預設輸出位置（`bin/Release/net9.0-windows/win-x64/publish/`），不要加 `-o` 改路徑。
- 版本號只維護 `LetMeSee.csproj` 的 `<Version>`；`FileVersion`／`AssemblyVersion` 由它衍生，CI 發佈時再用 tag 覆寫。

## 已知限制與注意事項

- 沒有自動化測試；WPF 互動主要靠 build 與手動測試。
- `App` 註冊了 `DispatcherUnhandledException`：未預期例外會寫入 `letmesee.log`、跳訊息框，並讓程式繼續執行，不會直接結束。
- 診斷紀錄超過 256 KB 會輪替成 `letmesee.log.old`。
- Save As 不保留 metadata，也不保留動畫 GIF，只存目前 frame。
- 實際可解碼格式取決於 Windows Imaging Component 與使用者已安裝 codec。
- 不支援 SVG，而且是刻意不支援。WIC 沒有 SVG 解碼器，要支援就得引入外部向量算繪器（例如 SharpVectors），會打破目前零 NuGet 相依的狀態，且向量圖與 `BitmapSource` 管線不合。不要把 `.svg` 加進 `SupportedImageFormats`。
- 檔案關聯更動後 Windows Explorer 可能需要重開或重新登入才完全刷新。
- `ImageLoader` 快取上限預設 512 MB；大圖超過上限不會被加入快取。快取項目的檔案長度與 mtime 是在解碼「之前」取樣，載入中被改寫的檔案下次查詢就會失效重解。
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
