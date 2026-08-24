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
