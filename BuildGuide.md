# Build Guide

這份文件說明 LetMeSee 常用的 .NET 建置與發佈指令。

## 基本概念

`.NET` 常見流程可以分成三種：

| 指令類型 | 用途 |
| --- | --- |
| `dotnet build` | 編譯專案，預設使用 Debug 組態 |
| `dotnet build -c Release` | 用 Release 組態編譯專案 |
| `dotnet publish` | 產生可交付給使用者的發佈資料夾 |

`Release` 是「建置組態」，`publish` 是「發佈動作」。兩者不是同一件事。

## 1. Debug Build

指令：

```powershell
dotnet build
```

等同於：

```powershell
dotnet build -c Debug
```

用途：

- 日常開發。
- 快速確認程式可以編譯。
- 保留較多除錯資訊。
- 編譯最佳化較少。

常見輸出位置：

```text
bin\Debug\net9.0-windows\
```

Debug build 通常不適合直接交付給一般使用者。

## 2. Release Build

指令：

```powershell
dotnet build -c Release
```

用途：

- 確認正式組態可以編譯。
- 啟用 Release 編譯最佳化。
- 適合在發佈前檢查 build 是否成功。

常見輸出位置：

```text
bin\Release\net9.0-windows\
```

Release build 是正式組態的編譯結果，但不一定是最適合直接打包給使用者的資料夾。要交付使用者通常使用 `dotnet publish`。

## 3. Publish

基本發佈指令：

```powershell
dotnet publish -c Release -r win-x64 --self-contained true
```

用途：

- 產生可交付給使用者的版本。
- 整理執行需要的檔案。
- 可指定目標平台，例如 `win-x64`。
- 可選擇是否包含 .NET runtime。

常見輸出位置：

```text
bin\Release\net9.0-windows\win-x64\publish\
```

真正要打包、壓縮或放到 GitHub Release 的內容，是 `publish` 資料夾裡面的檔案。

## 為什麼 publish 後 win-x64 底下還有一份 publish？

執行：

```powershell
dotnet publish -c Release -r win-x64 --self-contained true
```

會看到類似結構：

```text
bin\Release\net9.0-windows\win-x64\
bin\Release\net9.0-windows\win-x64\publish\
```

這是正常的。

`win-x64` 那層包含 build 與 publish 過程產生的輸出與中間結果。

真正要交付使用者的是：

```text
bin\Release\net9.0-windows\win-x64\publish\
```

## 輸出位置：使用 .NET 預設

本專案不覆寫 publish 的輸出位置，一律使用 .NET 預設路徑：

```text
bin\Release\net9.0-windows\win-x64\publish\
```

要交付給使用者時，壓縮這個資料夾的內容即可。

`dotnet publish` 可以用 `-o` 指定其他位置，但本專案刻意不使用，讓 publish 產物和 `dotnet build` 的輸出留在同一棵目錄樹下，少記一組規則。

## Framework-dependent 與 Self-contained

### Self-contained

指令：

```powershell
dotnet publish -c Release -r win-x64 --self-contained true
```

特性：

- 使用者不需要另外安裝 .NET runtime。
- 輸出檔案較大。
- 適合一般散佈。

### Framework-dependent

指令：

```powershell
dotnet publish -c Release -r win-x64 --self-contained false
```

特性：

- 使用者電腦必須已安裝相容的 .NET runtime。
- 輸出檔案較小。
- 適合內部環境或你能控制 runtime 安裝狀態的情境。

## 建議流程

日常開發時：

```powershell
dotnet build
```

準備發佈前：

```powershell
dotnet build -c Release
```

產生可交付版本：

```powershell
dotnet publish -c Release -r win-x64 --self-contained true
```

## 自動發佈（推 tag 就出 Release）

`.github/workflows/release.yml` 會在推送 `v` 開頭的 tag 時自動建置並發佈 GitHub Release：

```bash
git tag v1.1.0
git push origin v1.1.0
```

workflow 做的事：

1. 在 `windows-latest` 上 checkout（WPF 只能在 Windows 建置）。
2. 從 tag 取版本號，`v1.1.0` -> `1.1.0`，格式不符會直接失敗。
3. `dotnet publish -c Release -r win-x64 --self-contained true -p:Version=<版本>`。
4. 檢查產出的 `LetMeSee.exe` 版本戳記與 tag 相符，不符就失敗。
5. 把 publish 資料夾壓成 `LetMeSee-v1.1.0-win-x64.zip`。
6. 用 Inno Setup 編譯安裝程式（runner 上沒有的話會先用 choco 安裝）。
7. 用 `gh release create` 建立 Release，附上 zip 與 setup.exe，release notes 由 `--generate-notes` 自動產生。

### 版本號的來源

`LetMeSee.csproj` 只保留一個 `<Version>`，`FileVersion` 與 `AssemblyVersion` 由它衍生。
CI 發佈時會用 tag 覆寫這個值，所以**正式版的版本號以 tag 為準**，本機建置則用 csproj 裡的值。
兩者不需要時時一致，但推 tag 前把 csproj 的 `<Version>` 一併更新，本機建置出來的版本才不會誤導。

### 想先確認再公開

把 workflow 最後一步的 `gh release create` 加上 `--draft`，Release 會先以草稿建立，確認後再手動發佈。

## 安裝程式（Inno Setup）

`installer/LetMeSee.iss` 是 Inno Setup 腳本，會把 publish 輸出打包成單一 `setup.exe`。

本機編譯（需先 publish）：

```powershell
dotnet publish -c Release -r win-x64 --self-contained true
& "C:\Program Files\Inno Setup 7\ISCC.exe" installer\LetMeSee.iss
```

產出在 `installer\Output\LetMeSee-<版本>-setup.exe`（已列入 `.gitignore`）。
約 43 MB，比 publish 資料夾的 135 MB 小很多，因為用 LZMA2 壓縮。

版本號用 `/DAppVersion` 指定，不給就用腳本裡的預設值：

```powershell
& "C:\Program Files\Inno Setup 7\ISCC.exe" /DAppVersion=1.2.0 installer\LetMeSee.iss
```

### 安裝程式的行為

- **全機器安裝**（`PrivilegesRequired=admin`），裝到 `C:\Program Files\LetMeSee`，安裝時會跳 UAC。
  注意 app 的檔案關聯寫的是 HKCU，所以每個使用者要各自從「設定 > 檔案關聯...」建立。
- **不會**建立檔案關聯，那是 app 內「設定 > 檔案關聯...」的工作。
- 反安裝時會清掉 app 寫進去的 per-user 關聯（`LetMeSee.Image`、`Applications\LetMeSee.exe`、圖片右鍵選單、`RegisteredApplications`），避免留下指向已刪除執行檔的關聯。因為那些鍵在 HKCU，只清得掉執行反安裝那個使用者的部分。
- 反安裝會刪掉 `%LOCALAPPDATA%\LetMeSee`（診斷紀錄），但保留 `%APPDATA%\LetMeSee` 的 `settings.json`。
- 介面語言為繁體中文。語言檔 `installer/ChineseTraditional.isl` 放在 repo 裡，不是用 `compiler:` 路徑引用，
  因為 CI 上 choco 裝的是 Inno Setup 6（目前最新 6.7.1），它沒有內建這個語言檔。
  曾經用 `#if FileExists` 做條件式引用，結果是本機編出繁中、CI 安靜地降級成英文；改成隨附檔案後兩邊一致。
  該檔案取自 Inno Setup 7 的內建語言檔，標頭註明相容 6.5.0 以上，作者與出處註記保留在檔案內。
- `AppId` 是固定的 GUID，決定升級與反安裝的識別，**永遠不要改**。

## Git 注意事項

本專案的 `.gitignore` 已排除：

```text
bin/
obj/
publish/
```

因為使用預設輸出位置，publish 產物落在 `bin/` 底下，已經被排除。`publish/` 這條保留著，只是在有人臨時用 `-o` 改路徑時當保險。

因此 Debug、Release、publish 產物都不會被提交到 GitHub。

GitHub repository 應提交原始碼與專案設定，不提交編譯輸出。若要提供可下載執行檔，建議使用 GitHub Releases 上傳 `publish` 輸出壓縮檔。
