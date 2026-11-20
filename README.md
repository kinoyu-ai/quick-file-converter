# QuickConvert

Windows 11 の**最初の右クリックメニュー（「その他のオプションを表示」の前）**から、拡張子に応じたファイル変換を**常駐プロセス 0**で実行する軽量ツールです。

[Custom Context Menu (`ikas-mc/ContextMenuForWindows11`)](https://github.com/ikas-mc/ContextMenuForWindows11) と連携して動作します。

## 特徴

- **常駐プロセス 0（待機時 CPU 0% / メモリ 0 MB）**  
  右クリックメニューから選んだ瞬間だけ起動し、変換完了から約 1 秒後に自動で完全終了します。
- **Windows 11 モダンコンテキストメニューに直接表示**  
  「その他のオプションを表示」を押さずに最初の右クリックメニューから呼び出せます。
- **拡張子ごとの候補自動出し分け**  
  たとえば `.mp4` なら MP3 / MOV / 圧縮 / GIF 等、`.png` なら JPG / WebP / ICO / PDF 等だけを表示し、対象外のファイル（`.txt`, `.exe` 等）ではメニュー自体を隠します。
- **複数ファイル一括変換に対応**  
  複数選択しても 1 プロセスで順番に安全処理します。
- **上書き事故防止 & キャンセル時クリーンアップ**  
  同名ファイルが既にある場合は自動で `(1)`, `(2)` を付与します。途中キャンセル時は一時ファイルを自動削除します。
- **依存ゼロのビルド & Smart App Control 対応**  
  Windows 標準の `.NET Framework 4.x` / `powershell.exe` のみで動作します。デフォルトでは署名済み `powershell.exe` 経由（コンソール非表示）で起動するため、未署名 EXE がブロックされる環境でも動作します。

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

## セットアップ手順

### 1. このリポジトリを取得
```powershell
git clone https://github.com/kinoyu-ai/quick-file-converter.git
cd quick-file-converter
```
（または GitHub の `Code` → `Download ZIP` で展開）

### 2. Custom Context Menu を導入
[Custom Context Menu (`ikas-mc/ContextMenuForWindows11`)](https://github.com/ikas-mc/ContextMenuForWindows11) を Microsoft Store または [GitHub Releases](https://github.com/ikas-mc/ContextMenuForWindows11/releases) からインストールします。

1. 一度だけアプリを起動して初期フォルダを作成
2. 設定画面で親メニュー名を「ファイル変換」など好みの名前に変更
3. **アプリを閉じる**（常駐させる必要はありません）

### 3. メニューを自動生成・登録
```powershell
powershell -ExecutionPolicy Bypass -File .\install-menus.ps1
```
自動でアイコン生成（`dist\QuickConvert.ico`）と 13 個の JSON メニュー生成を行い、Custom Context Menu の `LocalState\custom_commands` へ配置します。

> **ヒント**：Smart App Control が無効な環境でさらに高速起動したい場合は、`.\install-menus.ps1 -UseExe` で単一バイナリ（`dist\QuickConvert.exe`）直起動モードに切り替えられます。

### 4. FFmpeg を導入（動画・音声・WebP等の変換用）
```powershell
winget install Gyan.FFmpeg
```
※`QuickConvert` は `PATH` に加えて WinGet のインストール先や `dist\tools\ffmpeg.exe` も自動検出します。

---

## カスタマイズ

[`presets.json`](presets.json) を編集して `install-menus.ps1` を再実行するだけで、右クリックメニューの候補を一括更新できます。

## アンインストール

```powershell
powershell -ExecutionPolicy Bypass -File .\uninstall-menus.ps1
```
本ツールが追加した `qc_*.json` だけが削除されます（他のカスタムメニューは消えません）。

## 動作要件

- Windows 11（.NET Framework 4.x / Windows PowerShell 5.1 以上・標準搭載）
- [Custom Context Menu](https://github.com/ikas-mc/ContextMenuForWindows11)
- [FFmpeg](https://ffmpeg.org/)（動画・音声・WebP 変換時）

## License

MIT License
