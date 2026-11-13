# LiteGlass

LiteGlass is a lightweight, portable fork of [ImageGlass](https://github.com/d2phap/ImageGlass) by Dương Diệu Pháp (d2phap). It keeps the GPL-3.0 license. The goal is a fast Windows image viewer: open a picture immediately, honor color profiles, and read as many formats as ImageGlass already can, plus PDF-compatible Illustrator files and a best-effort AutoCAD preview.

This repository is not an official ImageGlass download. Official ImageGlass builds are published only by d2phap.

## Download and run

The Windows x64 build is a zip. No installer.

1. Open the latest release: <https://github.com/ghostltx/LiteGlass/releases/latest>
2. Download `LiteGlass-win-x64-portable.zip` (and, if you want to check it yourself, `LiteGlass-win-x64-portable.zip.sha256`).
3. Extract the zip to a writable folder.
4. Double-click `LiteGlass.exe`.

An empty `.igportable` file next to the executable stores settings in that same folder (`igconfig.json`, cache, logs). Move the folder and the settings move with it. Do not put it in a read-only location such as `Program Files`.

### Updates

- After the first image is shown, LiteGlass asks GitHub, at most once per day, whether `ghostltx/LiteGlass` has a newer portable zip. The request is only the public releases API. No telemetry, news, or store call is made.
- If a newer build exists, the zip is downloaded in the background and checked against the release size and SHA-256.
- The next launch applies that zip before the window opens, then starts the new build. `igconfig.json`, `.igportable`, and the cache/log folders are kept.
- You can apply it immediately from the main menu or the About window: **Install update and restart**. There is no popup.
- **Check for updates** is still in the Help menu, About, and Settings, if you want to look now.
- Settings → **Check for update automatically** turns the daily check off. It is on by default.

The release asset names the updater expects are:

- `LiteGlass-win-x64-portable.zip`
- `LiteGlass-win-x64-portable.zip.sha256` (hex SHA-256, optionally followed by the file name)

GitHub's own asset `digest` (`sha256:…`) is used when it is present, so the sidecar is only a fallback. The version compared with the running build is the release tag, such as `v1.0.0.1`, without the leading `v`. That version is compiled into the executable from `source/Directory.Build.props` (`IgVersion`).

## What changed

- User-visible name is LiteGlass. Namespaces in the code stay `ImageGlass` so the fork can track upstream. The About window credits ImageGlass and d2phap.
- Startup no longer checks ImageGlass servers, sends usage statistics, opens a welcome image, or runs the first-run wizard. Store, donation, and Pro pitches are not shown.
- Color management defaults to the current monitor profile, and that profile is also applied to images that have no embedded profile. Embedded ICC profiles, including JPEG, TIFF, PSD, and CMYK (through the existing Magick path), are still honored.
- `.ai`, `.eps`, `.pdf`, and `.dwg` are registered with the built-in formats.
- The portable zip and its hash are built by `.github/workflows/liteglass-portable.yml`.

Core viewing is unchanged: zoom and pan, folder browsing, rotate and flip, copy, open with, and basic metadata.

## Formats

Everything ImageGlass 10 already opens is still registered, including PSD, RAW, HEIC, AVIF, JPEG XL, WebP, SVG, and TIFF. Codecs that ImageGlass loads from optional plugins (for example a separate AVIF plugin) behave as they do upstream: the extension is listed, and a plugin is required when the built-in decoders cannot read that file.

Added in LiteGlass:

| Extension | How it opens | Limit |
| --- | --- | --- |
| `.pdf` | PDFium, bundled, BSD license | First page only |
| `.ai` | PDFium, when the file contains the PDF stream Illustrator writes by default | Legacy PostScript-only `.ai` is refused with an error |
| `.eps` | PDFium when a PDF stream is present; otherwise the TIFF or WMF preview in a DOS EPS header | Pure PostScript, with no preview, is refused. Ghostscript is not bundled |
| `.dwg` | LibreDWG `dwg2SVG`, bundled, then drawn as SVG | Best effort. Complicated drawings can be incomplete. The converter is stopped after 20 seconds so the viewer does not hang |

PDFium is downloaded at build time from [bblanchon/pdfium-binaries](https://github.com/bblanchon/pdfium-binaries) and placed next to `LiteGlass.exe`. LibreDWG is taken from the MSYS2 UCRT64 package and placed in `libredwg\`. If that tool is missing, opening a DWG shows an error instead of crashing.

## Build

The workflow runs on GitHub:

1. `dotnet test` for version comparison, release JSON parsing, the updater script's preserved files, PDF/EPS sniffing, and the format list.
2. A self-contained Windows x64 publish of `ImageGlass.Win32` (the executable name is `LiteGlass.exe`), then `source/__assets/win/script-pack-win-zip.ps1`.
3. A GitHub Release on this repository whose assets use the names above.

Locally, from `source`, on Windows with the .NET 11 SDK:

```powershell
pwsh __assets/win/script-pack-win-zip.ps1 -Platform x64
```

Put `pdfium.dll` in `__assets/win/native/x64/` and `dwg2SVG.exe` plus its DLLs in `__assets/win/native/x64/libredwg/` before packing if you want those formats in a local zip.

## License

GPL-3.0-or-later, the same license as ImageGlass. See [LICENSE](LICENSE). Third-party pieces keep their own licenses; they are listed in the About window (Avalonia, SkiaSharp, Magick.NET, PDFium, LibreDWG, and the rest of the ImageGlass stack).

---

# LiteGlass（简体中文）

LiteGlass 是 [ImageGlass](https://github.com/d2phap/ImageGlass)（作者 Dương Diệu Pháp / d2phap）的一个轻量便携分支，许可证仍是 GPL-3.0。目标是尽快打开图片、默认做好色彩管理，并在 ImageGlass 已支持的格式之外，尽量打开 PDF 兼容的 Illustrator 文件，以及对 AutoCAD 图纸做尽力而为的预览。

本仓库不是 ImageGlass 的官方下载站。官方安装包只由 d2phap 发布。

## 下载与运行

Windows x64 版本是一个压缩包，不需要安装程序。

1. 打开最新发布：<https://github.com/ghostltx/LiteGlass/releases/latest>
2. 下载 `LiteGlass-win-x64-portable.zip`。需要自行校验时，一并下载 `LiteGlass-win-x64-portable.zip.sha256`。
3. 解压到可写文件夹。
4. 双击 `LiteGlass.exe`。

可执行文件旁边的空文件 `.igportable` 会让设置保存在同一目录（`igconfig.json`、缓存、日志）。整个文件夹可以拷走，设置跟着走。不要放进 `Program Files` 这种只读位置。

### 更新

- 第一张图片显示之后，LiteGlass 每天最多向 GitHub 查询一次 `ghostltx/LiteGlass` 是否有更新的便携包。请求只访问公开的 Releases API，不发送统计、新闻或商店信息。
- 若有新版本，会在后台下载压缩包，并按发布页上的文件大小和 SHA-256 校验。
- 下次启动时，在主窗口出现之前完成替换，然后启动新版本。`igconfig.json`、`.igportable` 以及缓存、日志目录会保留。
- 也可以立刻安装：主菜单或“关于”窗口里的 **Install update and restart**（安装更新并重启）。没有弹窗。
- 帮助菜单、关于窗口和设置里仍有 **检查更新**，需要时可以手动查看。
- 设置中的 **自动检查更新** 默认开启。关掉之后就不会再访问网络。

更新程序认这些资源名：

- `LiteGlass-win-x64-portable.zip`
- `LiteGlass-win-x64-portable.zip.sha256`（十六进制 SHA-256，后面可以跟文件名）

如果 GitHub 在资源上提供了 `digest`（`sha256:…`），会优先使用它。用来比较的版本号是发布标签，例如 `v1.0.0.1`（比较时去掉开头的 `v`）。这个版本在编译时写入程序，来源是 `source/Directory.Build.props` 的 `IgVersion`。

## 相对 ImageGlass 的改动

- 界面上的名称是 LiteGlass。代码命名空间仍是 `ImageGlass`，方便跟随上游。关于窗口保留对 ImageGlass 与 d2phap 的署名。
- 启动时不再访问 ImageGlass 的服务器，不发送使用统计，不显示欢迎图，也不再跑首次向导。商店、捐赠和 Pro 提示已去掉。
- 色彩管理默认使用当前显示器配置文件；没有内嵌配置文件的图片也会套用它。JPEG、TIFF、PSD 的内嵌 ICC，以及 CMYK（仍走原来的 Magick 路径）会继续被识别。
- `.ai`、`.eps`、`.pdf`、`.dwg` 已加入支持格式列表。
- 便携包和校验文件由 `.github/workflows/liteglass-portable.yml` 构建并发布。

看图所需的功能还在：缩放与平移、文件夹浏览、旋转与翻转、复制、用其他程序打开，以及基本元数据。

## 格式

ImageGlass 10 原本登记的格式都还在，包括 PSD、RAW、HEIC、AVIF、JPEG XL、WebP、SVG 和 TIFF。上游靠可选插件才能解码的格式（例如单独的 AVIF 插件）行为不变：扩展名在列表里，内置解码器读不了时仍需要插件。

LiteGlass 新增：

| 扩展名 | 打开方式 | 限制 |
| --- | --- | --- |
| `.pdf` | 随程序附带的 PDFium（BSD） | 只渲染第一页 |
| `.ai` | 文件里有 Illustrator 默认写入的 PDF 流时，用 PDFium | 只有旧式 PostScript、没有 PDF 的 `.ai` 会提示错误 |
| `.eps` | 有 PDF 流时用 PDFium；否则读取 DOS EPS 头里的 TIFF 或 WMF 预览 | 没有预览的纯 PostScript 会提示错误。不附带 Ghostscript |
| `.dwg` | 附带的 LibreDWG `dwg2SVG` 转成 SVG 再绘制 | 尽力而为。复杂图纸可能不完整。转换超过 20 秒会被中止，避免查看器卡住 |

构建时会从 [bblanchon/pdfium-binaries](https://github.com/bblanchon/pdfium-binaries) 下载 PDFium，并放在 `LiteGlass.exe` 旁边。LibreDWG 来自 MSYS2 的 UCRT64 包，放在 `libredwg\`。如果找不到该工具，打开 DWG 会显示错误，而不是崩溃。

## 构建

GitHub 上的工作流会：

1. 运行 `dotnet test`，覆盖版本比较、发布 JSON 解析、更新脚本要保留的文件、PDF/EPS 识别，以及格式列表。
2. 以自包含方式发布 Windows x64 的 `ImageGlass.Win32`（可执行文件名为 `LiteGlass.exe`），再运行 `source/__assets/win/script-pack-win-zip.ps1`。
3. 在本仓库创建 GitHub Release，资源名如上。

在已安装 .NET 11 SDK 的 Windows 上，于 `source` 目录执行：

```powershell
pwsh __assets/win/script-pack-win-zip.ps1 -Platform x64
```

若希望本地压缩包包含新格式，打包前请把 `pdfium.dll` 放到 `__assets/win/native/x64/`，并把 `dwg2SVG.exe` 及其 DLL 放到 `__assets/win/native/x64/libredwg/`。

## 许可证

GPL-3.0 或更高版本，与 ImageGlass 相同。见 [LICENSE](LICENSE)。第三方组件保留各自的许可证，并在关于窗口中列出（Avalonia、SkiaSharp、Magick.NET、PDFium、LibreDWG，以及 ImageGlass 其余依赖）。
