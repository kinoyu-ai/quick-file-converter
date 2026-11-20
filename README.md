# QuickConvert

Windows 11 の**最初の右クリックメニュー（「その他のオプションを表示」の前）**から、拡張子に応じたファイル変換を**常駐プロセス 0**で実行する軽量ツールです。

2 つの導入方式に対応しています：

| 方式 | 外部メニューアプリ | 待機時リソース | Smart App Control オン環境 | おすすめ |
| :--- | :---: | :---: | :---: | :--- |
| **方法A：ネイティブ アプリ拡張機能版（単独動作）** | **不要** | 極小（COM サロゲートのみ・常駐 0） | △（オフ または 署名が必要） | 外部ツールを入れずに Windows 標準の「アプリ拡張機能」だけで使いたい場合 |
| **方法B：Custom Context Menu 連携版** | Custom Context Menu（Store 版） | 極小（常駐 0） | **○ そのまま動く** | Smart App Control をオンのまま使いたい場合 |

---

## 特徴

- **常駐プロセス 0（待機時 CPU 0% / メモリ 0 MB）**  
  右クリックメニューから選んだ瞬間だけ変換ウィンドウが起動し、完了から約 1 秒後に自動で完全終了します。
- **Windows 11 モダンコンテキストメニューに直接表示**  
  「その他のオプションを表示」を押さずに最初の右クリックメニューから呼び出せます。
- **拡張子ごとの候補自動出し分け**  
  たとえば `.mp4` なら MP3 / MOV / 圧縮 / GIF 等、`.png` なら JPG / WebP / ICO / PDF 等だけを表示し、対象外のファイル（`.txt`, `.exe` 等）ではメニュー自体を隠します。
- **複数ファイル一括変換に対応**  
  複数選択しても 1 プロセスで順番に安全処理します（ネイティブ版では選択全ファイルに共通する候補のみ自動表示）。
- **上書き事故防止 & キャンセル時クリーンアップ**  
  同名ファイルが既にある場合は自動で `(1)`, `(2)` を付与し、途中キャンセル時は一時ファイルを自動削除します。

---

## 対応フォーマット（デフォルト）

| 右クリックしたファイル | 表示される変換候補 |
| :--- | :--- |
| `.mp4` | MP3 / MOV / MP4 圧縮 (軽量化) / GIF / WAV / M4A (AAC) |
| `.mov` | MP3 / MP4 / MP4 圧縮 (軽量化) / GIF / WAV / M4A (AAC) |
| `.mkv` / `.avi` / `.webm` 等 | MP3 / MOV / MP4 / MP4 圧縮 / GIF / WAV / M4A |
| `.mp3` / `.wav` / `.flac` / `.m4a` 等 | 他の音声形式（MP3 / WAV / M4A / FLAC） |
| `.png` | JPG / WebP / ICO (マルチサイズアイコン) / PDF |
| `.jpg` / `.jpeg` | PNG / WebP / ICO / PDF |
| `.webp` | PNG / JPG / ICO / PDF |

> 基本の画像変換（PNG / JPG / BMP / ICO / PDF）は FFmpeg なしでも動作します（透過PNG→JPG時の白背景合成・EXIF回転補正対応）。  
> 動画・音声および WebP / AVIF / HEIC の変換には FFmpeg を使用します。

---

## 方法A：ネイティブ アプリ拡張機能版（Custom Context Menu 不要）

ネイティブ C++ の `IExplorerCommand` COM DLL（`QuickConvertShell.dll`）と MSIX パッケージ（`AppxManifest.xml`）により、**Windows 11 の「設定 → 個人用設定 → コンテキストメニュー（アプリ拡張機能）」に単独で直接登録**します。

### 1. ビルド済みパッケージを入手（または自分でビルド）
- **GitHub Actions / Releases から入手**：`QuickConvert-Native-win-x64.zip` をダウンロードして任意のフォルダ（例：`C:\Tools\QuickConvert`）に展開
- **ローカルでビルドする場合**（Visual Studio C++ Build Tools + Windows 10/11 SDK が必要）：
  ```powershell
  powershell -ExecutionPolicy Bypass -File .\build-native.ps1
  ```

### 2. アプリ拡張機能として登録
Windows 11 の **設定 → システム → 開発者向け → 開発者モード** をオンにした状態で、展開フォルダにて次を実行します：
```powershell
powershell -ExecutionPolicy Bypass -File .\install-native.ps1
```
（※自己署名済み MSIX で入れたい場合は管理者 PowerShell で `.\install-native.ps1 -Mode Msix` を実行）

> **注意**：Windows の **Smart App Control** が「オン（強制）」の場合、未署名／自己署名のネイティブ DLL は OS によりブロックされます。Smart App Control をオンのまま使いたい場合は下の **方法B** をご利用ください。

### 3. メインメニューにピン留め（任意）
エクスプローラー右クリック → **メニューのカスタマイズ**（または **設定 → 個人用設定 → コンテキストメニュー**）の **アプリ拡張機能** で `QuickConvert` をメインメニュー表示にすると、1 クリック目の右クリックに「ファイル変換 ▶」が直接表示されます。

### 4. FFmpeg を導入（動画・音声・WebP等の変換用）
```powershell
winget install Gyan.FFmpeg
```
※`QuickConvert` は `PATH` に加えて WinGet のインストール先や同階層の `tools\ffmpeg.exe` も自動検出します。

### プリセットのカスタマイズ（ネイティブ版）
展開フォルダの `presets.json`（または `%LOCALAPPDATA%\QuickConvert\presets.json`）を編集して保存するだけで、**再ビルド不要**で右クリックメニューの候補が即更新されます。

### アンインストール（ネイティブ版）
```powershell
powershell -ExecutionPolicy Bypass -File .\uninstall-native.ps1
```
（または Windows の「設定 → アプリ → インストールされているアプリ → QuickConvert」からアンインストール）

---

## 方法B：Custom Context Menu 連携版（Smart App Control オンのまま動作）

1. [Custom Context Menu (`ikas-mc/ContextMenuForWindows11`)](https://github.com/ikas-mc/ContextMenuForWindows11) を Microsoft Store 等から導入し、一度起動して閉じる
2. このリポジトリを取得して次を実行：
   ```powershell
   powershell -ExecutionPolicy Bypass -File .\install-menus.ps1
   ```
3. 動画・音声変換用に `winget install Gyan.FFmpeg` を導入

アンインストール：
```powershell
powershell -ExecutionPolicy Bypass -File .\uninstall-menus.ps1
```

---

## プロジェクト構成

- `src/QuickConvert.cs` — 常駐ゼロの変換エンジン & ダークUI進捗ウィンドウ（C# / .NET Framework 4.x）
- `src/IconGen.cs` — マルチ解像度 `.ico` および MSIX 用 PNG アセット生成ツール
- `shell-ext/QuickConvertShell.cpp` / `.def` — Windows 11 モダンコンテキストメニュー用ネイティブ C++ COM DLL（`IExplorerCommand` + `IEnumExplorerCommand`）
- `packaging/AppxManifest.xml` — Windows 11 アプリ拡張機能マニフェスト（`windows.fileExplorerContextMenus` + `com:SurrogateServer`）
- `presets.json` — 拡張子別の変換候補定義
- `.github/workflows/build-native.yml` — Windows ランナーでの自動ビルド・スモークテスト・Artifact/Release 生成

## License

MIT License
