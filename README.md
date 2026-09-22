# Lumen 音乐

一款轻量、便携的 Windows 本地音乐播放器，基于 **.NET 9 + WPF** 编写。

- 无广告、无联网、无遥测，数据完全保存在本地。
- 支持常见音频格式，播放进度按分组独立记忆。
- 内置歌词解析与桌面歌词，支持媒体键与系统托盘。
- 整个项目**可离线还原与构建**（依赖包缓存在仓库内的 `local-feed\`）。

---

## 功能特性

### 播放
- 播放 / 暂停、上一首 / 下一首。
- 播放模式：顺序播放 / 列表循环 / 单曲循环。
- 可点击跳转的进度条、音量与静音。
- 按分组**独立记忆**播放进度（切分组 / 重启后仍在原位置）。
- 全局媒体键（键盘上的播放/暂停/上一首/下一首）。

### 曲库
- 拖拽导入音频文件或整个文件夹。
- 元数据自动补全（时长、标题、艺术家、专辑，可从文件名推导）。
- 搜索：按标题 / 艺术家 / 专辑，空格分隔多个关键词。
- 排序：最近添加、标题 ↑↓、时长 ↑↓、文件格式。
- 打乱顺序（一次性操作，结果会被记住，重新选择排序即恢复有序）。
- 定位到当前播放位置。
- 分组管理：新建 / 重命名 / 删除 / 切换分组。
- 右键菜单：播放、从列表移除、在资源管理器中显示。

### 歌词
- 自动查找同目录同名 `.lrc` 歌词（支持多编码、带时间轴同步）。
- 内置歌词区，点击歌词行可跳转播放位置。
- **桌面歌词**：独立置顶歌词条，白字黑描边，锁定后鼠标完全穿透，可拖动位置、拖上下边缘改高度、双击锁定。

### 界面
- 深浅色主题、自绘标题栏与无边框窗口。
- 关闭主窗口最小化到系统托盘（常驻后台）。
- 单实例运行：再次启动会把文件转交给已运行的实例。

---

## 技术栈

| 组件 | 说明 |
|------|------|
| .NET SDK | 9.0（目标框架 `net9.0-windows`） |
| UI 框架 | WPF（`UseWPF`），仅托盘图标用 WinForms（`UseWindowsForms`） |
| 音频 | [NAudio 2.2.1](https://github.com/naudio/NAudio) —— 纯托管，解码走 Windows Media Foundation，输出用 WASAPI 共享模式 |
| 依赖还原 | NuGet，配置了仓库内 `local-feed` 本地源，可完全离线还原 |

> **说明**：NAudio 2.2.1 是纯托管实现，不含原生 DLL。解码依赖 Windows 自带的 Media Foundation（MP3 / AAC / M4A / WMA / MP4 容器等），WAV / AIFF 由 NAudio 自己解析。

---

## 环境要求

- **操作系统**：Windows 10 / 11（x64）。
- **.NET SDK 9.0**（需包含 Windows Desktop 工作负载，WPF/WinForms 项目必需）。
  - 如果还没有，请从 <https://dotnet.microsoft.com/download/dotnet/9.0> 安装 **SDK 9.0.x**（不是纯 Runtime）。
- 音频解码依赖 Windows 自带的 **Media Foundation**（Windows 10/11 默认已内置）。

### 验证环境

```powershell
dotnet --version        # 应显示 9.0.x
dotnet --list-sdks      # 确认有 9.0 的 SDK
```

---

## 从零开始：安装依赖到构建

### 1. 安装 .NET SDK

确保 `dotnet --version` 输出 9.0.x。缺少的话先装 SDK（见上方「环境要求」）。

### 2. 还原依赖

项目根目录有 `NuGet.config`，把 NuGet 源清空后只指向仓库内的 `local-feed`，因此**无需联网**即可还原：

```powershell
dotnet restore Lumen.csproj
```

> 还原后的包会被解包到仓库内的 `packages\`（由 `Lumen.csproj` 里的
> `RestorePackagesPath` 指定），而不是 `%USERPROFILE%\.nuget`。
> 所以整个还原过程不依赖、也不污染用户全局 NuGet 缓存。

### 3. 构建（Release）

```powershell
dotnet build Lumen.csproj -c Release
```

构建产物位于：

```
bin\Release\net9.0-windows\Lumen.exe
```

> 目标平台固定为 `x64`。如果之前手动改过 `-p:PlatformTarget`，注意保持一致。

### 4. 运行

```powershell
dotnet run -c Release
# 或直接运行产物
.\bin\Release\net9.0-windows\Lumen.exe
```

---

## 常见构建问题

- **NETSDK1022（重复的 Page 项）**：本项目已在 `Lumen.csproj` 中关闭 WPF 默认的 Page / ApplicationDefinition 包含，并显式声明 `App.xaml` 与 `ui\**\*.xaml`。请勿手工改动这两类 `ItemGroup`。
- **还原报找不到 NAudio**：确认 `NuGet.config` 与 `local-feed\` 目录在同一目录树内且完整；`local-feed\` 必须包含 `naudio.2.2.1.nupkg` 等 11 个包。
- **首次构建很慢**：正常，首次要还原并编译 WPF XAML。后续增量构建会快很多。

---

## 命令行参数

```text
Lumen.exe [选项] [音频文件或文件夹 ...]

  --selftest [秒数]   内置自检，不显示界面（默认 8 秒）
  --smoke [秒数]      正常启动界面，若干秒后自动退出（默认 6 秒，用于实机冒烟）
  --data-dir <目录>   覆盖数据目录
  --help              显示帮助
```

从文件关联打开（把音频文件或文件夹作为参数传入）会自动导入并播放。

---

## 内置自检

自检不依赖界面，可用来快速验证「界面能建、音频能解码、歌词能同步、穿透能生效」等核心链路：

```powershell
dotnet run -c Release -- --selftest
```

- 退出码：`0` 表示全部通过，`1` 表示存在失败项。
- 结果写入数据目录下的 `selftest-result.json`，控制台也会打印逐项 PASS/FAIL。

---

## 数据目录与配置

应用采用**便携优先**策略，数据目录按以下顺序确定：

1. 环境变量 `LUMEN_DATA_DIR`（若设置且可写）。
2. 程序旁边的 `data\`（便携模式）。
3. 源码运行时的工作目录下的 `data\`。
4. 回退 `%APPDATA%\Lumen`。

主要数据文件：

| 文件 / 目录 | 说明 |
|-------------|------|
| `library.json` | 曲库与配置（分组、播放进度、排序、打乱顺序、桌面歌词位置等） |
| `covers\` | 专辑封面缓存 |
| `selftest-result.json` | 自检结果 |
| `data\selftest\` | 自检生成的临时测试文件 |

---

## 目录结构

```
Lumen/
├── App.xaml / App.xaml.cs        应用入口（OnStartup → Bootstrap）
├── Lumen.csproj                  项目文件（x64、离线还原、WPF）
├── NuGet.config                  本地源配置（local-feed）
├── app.manifest                  DPI / 长路径感知清单
├── assets\lumen.ico              应用图标
├── local-feed\                   缓存的 NuGet 包（离线还原用）
├── packages\                     还原后解包的依赖（RestorePackagesPath）
├── core\                         启动装配、配置、路径、命令行、单实例、媒体键
├── audio\                        播放器（NAudio + WASAPI）
├── media\                        媒体扫描、元数据补全
├── lyrics\                       歌词文档、解析、查找、编码
├── models\                       Track / Group / TrackSorter
├── ui\                           主窗口、列表、歌词、底部控制、桌面歌词、主题等
├── native\                       Win32 封装（穿透、置顶、工作区等）
├── selftest\                     内置自检
└── tools\make_icon\              图标生成工具（不参与主项目编译）
```

---

## 支持的音频格式

`.mp3` `.flac` `.wav` `.m4a` `.aac` `.ogg` `.opus` `.wma` `.ape` `.aiff` `.aif` `.aifc` `.mp4` `.m4b` `.wv` `.mpc`

> 某些格式（如部分 M4A / WMA）在 `AudioFileReader` 不支持时会自动回退到 Windows Media Foundation 解码。

---

## 许可证

本项目仅供学习与自用。依赖的第三方组件（NAudio 等）遵循各自的许可证。
