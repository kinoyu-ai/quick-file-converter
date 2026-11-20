# QuickConvert

[![Build Native Extension](https://github.com/kinoyu-ai/quick-file-converter/actions/workflows/build-native.yml/badge.svg)](https://github.com/kinoyu-ai/quick-file-converter/actions/workflows/build-native.yml)
![Platform](https://img.shields.io/badge/platform-Windows%2011-0078D6)
![Resident](https://img.shields.io/badge/background%20process-0%20(Zero%20Resident)-22c55e)
![License](https://img.shields.io/badge/license-MIT-blue)

**常駐プロセス 0** で動く、Windows 11 エクスプローラー右クリック専用の軽量ファイル変換ツールです。  
「その他のオプションを表示」を押さずに、**最初の右クリックメニュー**からファイル形式に応じた変換候補だけを呼び出せます。

```text
movie.mp4 を右クリック                   photo.png を右クリック
 └─ ファイル変換 ▶                        └─ ファイル変換 ▶
      ├─ MP3 に変換                            ├─ JPG に変換
      ├─ MOV に変換                            ├─ WebP に変換
      ├─ MP4 に圧縮 (軽量化)                    ├─ ICO (アイコン) に変換
      ├─ GIF に変換                            └─ PDF に変換
      ├─ WAV に変換
      └─ M4A (AAC) に変換                ※ .txt や .exe など対象外では非表示
```

---

## 特長

- **常駐ゼロ**：クリックした瞬間だけ起動し、変換完了から約 1 秒で自動終了（待機時 CPU 0%・メモリ 0 MB）
- **拡張子ごとの自動出し分け**：同じ形式への変換や対象外ファイルでは項目を自動で非表示
- **形式別のモノトーンアイコン**：変換先（MP3 / MOV / MP4 / 圧縮 / GIF / PNG / JPG / ICO / PDF 等）ごとに専用の単色ピクトグラムアイコンを表示
- **複数ファイル一括対応**：複数選択しても 1 プロセスで順番に安全処理
- **上書き事故を防止**：同名ファイルがあれば自動で `(1)`, `(2)` を付与／キャンセル時は一時ファイルを自動削除
- **基本の画像変換は FFmpeg 不要**：PNG / JPG / BMP / ICO / PDF は Windows 標準機能のみで高速変換（透過背景の白合成・EXIF回転補正対応）

---

## 対応フォーマット

| カテゴリ | 入力できる形式 | 変換先（出力） | 必要な外部ツール |
| :--- | :--- | :--- | :--- |
| **動画** | `.mp4` `.mov` `.mkv` `.avi` `.webm` `.m4v` `.wmv` `.flv` `.gif` | MP4 / MOV / MP4軽量圧縮 / GIF / 音声抽出 | [FFmpeg](https://ffmpeg.org/) |
| **音声** | `.mp3` `.wav` `.flac` `.m4a` `.aac` `.ogg` `.opus` `.wma` | MP3 / WAV / M4A (AAC) / FLAC | [FFmpeg](https://ffmpeg.org/) |
| **画像** | `.png` `.jpg` `.jpeg` `.bmp` `.tif` `.tiff` `.gif` | PNG / JPG / ICO (マルチサイズ) / PDF | **不要** |
| **画像（拡張）** | `.webp` `.avif` `.heic` / WebP 出力 | PNG / JPG / WebP / ICO / PDF | [FFmpeg](https://ffmpeg.org/) |

---

## 導入方法（2パターン）

環境に合わせてどちらか選んでください。

| | ネイティブ版（おすすめ・単独動作） | Custom Context Menu 連携版 |
| :--- | :--- | :--- |
| **外部メニューアプリ** | **不要**（Windows 標準「アプリ拡張機能」のみ） | [Custom Context Menu](https://github.com/ikas-mc/ContextMenuForWindows11)（Store 版） |
| **Smart App Control** | オフ（または正規署名）が必要 | **オンのまま動作** |

---

### パターン A：アプリ拡張機能だけで使う（ネイティブ版）

外部アプリを入れず、Windows 11 の **「設定 → 個人用設定 → コンテキストメニュー（アプリ拡張機能）」** に単独で直接登録します。

#### 1. ビルド済み ZIP をダウンロードして展開
[Actions](https://github.com/kinoyu-ai/quick-file-converter/actions)（または [Releases](https://github.com/kinoyu-ai/quick-file-converter/releases)）から **`QuickConvert-Native-win-x64.zip`** をダウンロードし、任意のフォルダ（例：`C:\Tools\QuickConvert`）に展開します。

#### 2. アプリ拡張機能として登録
Windows の **設定 → システム → 開発者向け → 開発者モード** をオンにして、展開フォルダで実行します：

```powershell
powershell -ExecutionPolicy Bypass -File .\install-native.ps1
```

#### 3. 最初の右クリックメニューに固定（任意）
エクスプローラーで右クリック → **メニューのカスタマイズ**（または **設定 → 個人用設定 → コンテキストメニュー**）を開き、**アプリ拡張機能** の `QuickConvert` をメインメニュー表示にすると 1 クリック目で直接表示されます。

#### 4. FFmpeg を導入（動画・音声・WebP 変換を使う場合）
```powershell
winget install Gyan.FFmpeg
```

> [!NOTE]
> Windows の **Smart App Control** が「オン（強制）」の場合、未署名 DLL が OS にブロックされます。Smart App Control をオンのまま使いたい場合は下の **パターン B** をご利用ください。

---

### パターン B：Custom Context Menu 連携版（Smart App Control オン環境向け）

1. [Custom Context Menu](https://github.com/ikas-mc/ContextMenuForWindows11) を Microsoft Store 等から導入し、一度起動してから閉じる（常駐不要）
2. このリポジトリをダウンロード（または `git clone`）して以下を実行：
   ```powershell
   powershell -ExecutionPolicy Bypass -File .\install-menus.ps1
   ```
3. 動画・音声・WebP 変換を使う場合は `winget install Gyan.FFmpeg` を実行

> [!TIP]
> `QuickConvert` は `PATH` に加えて WinGet のインストール先やアプリフォルダ内の `tools\ffmpeg.exe` も自動検出します。

---

## 候補のカスタマイズ

[`presets.json`](presets.json) を編集するだけで、表示する候補や対象拡張子を自由に変更できます。

- **ネイティブ版**：展開フォルダの `presets.json`（または `%LOCALAPPDATA%\QuickConvert\presets.json`）を書き換えて保存するだけ（**再ビルド不要・即反映**）
- **Custom Context Menu 版**：`presets.json` を編集後、`install-menus.ps1` を再実行

---

## アップデート方法（常駐なし・ワンコマンド）

バックグラウンド常駐は一切行わず、更新したいときだけ次のいずれかを実行します。**編集済みの `presets.json` は自動で保護されます。**

- **フォルダ内の `update.bat` をダブルクリック**
- **または PowerShell から 1 行で最新リリースを導入／更新**：
  ```powershell
  irm https://raw.githubusercontent.com/kinoyu-ai/quick-file-converter/main/update.ps1 | iex
  ```
- **更新があるか確認だけする場合**：
  ```powershell
  powershell -ExecutionPolicy Bypass -File .\update.ps1 -CheckOnly
  ```

> [!TIP]
> **新しい Release を発行するとき（開発者向け）**：[`VERSION`](VERSION) ファイル（例：`v1.0.1`）を書き換えて `main` にプッシュするだけ（または Actions の「Run workflow」で `release_tag` 指定）で、ビルド・SHA-256 生成・GitHub Release 公開まで全自動で完了します。

---

## アンインストール

- **ネイティブ版**：
  ```powershell
  powershell -ExecutionPolicy Bypass -File .\uninstall-native.ps1
  ```
  （または Windows の「設定 → アプリ → インストールされているアプリ → QuickConvert」から削除）
- **Custom Context Menu 版**：
  ```powershell
  powershell -ExecutionPolicy Bypass -File .\uninstall-menus.ps1
  ```

---

<details>
<summary>開発者向け情報（ローカルビルド・ファイル構成）</summary>

### ローカルでネイティブ版をビルドする
Visual Studio C++ Build Tools (x64) と Windows 10/11 SDK が入っている環境で実行します：

```powershell
powershell -ExecutionPolicy Bypass -File .\build-native.ps1
```

自己署名 MSIX でインストールする場合（管理者 PowerShell）：
```powershell
powershell -ExecutionPolicy Bypass -File .\install-native.ps1 -Mode Msix
```

### ファイル構成
| パス | 役割 |
| :--- | :--- |
| `src/QuickConvert.cs` | 変換エンジン本体・ダークUI進捗ウィンドウ（C# / .NET Framework 4.x） |
| `src/IconGen.cs` | マルチ解像度 `.ico`・MSIX 用 PNG アセット生成 |
| `shell-ext/QuickConvertShell.cpp` | Windows 11 モダンコンテキストメニュー用ネイティブ C++ COM DLL（`IExplorerCommand`） |
| `packaging/AppxManifest.xml` | Windows 11 アプリ拡張機能マニフェスト（`windows.fileExplorerContextMenus`） |
| `presets.json` | 拡張子別の変換候補マスター定義 |
| `QuickConvert.ps1` | Custom Context Menu 版向け署名済み PowerShell ランチャー |
| `.github/workflows/build-native.yml` | GitHub Actions による自動ビルド・テスト・Artifact/Release 生成 |

</details>

## License

[MIT License](LICENSE)
