# Sinophone

[English](README.en.md) · [简体中文](README.zh-CN.md) · [繁體中文](README.md)

一个 [Dalamud](https://github.com/goatcorp/Dalamud) 插件，为 **Final Fantasy XIV** 显示游戏文字的官方简体（CN）或繁体（TC）中文翻译。涵盖：

- 对话窗口上方的对话翻译，或直接写入游戏本身的对话框。
- 过场字幕在字幕栏上方显示，或直接写入游戏本身的字幕栏。
- 战斗台词：首领、副本旁白或任务 NPC 在画面顶部显示的大字。
- NPC 头上的对话气泡。
- 选择对话框，例如是/否、选项列表和图标选项。
- NPC 和物体上方的姓名板。
- 物品提示中的物品名称。

覆盖窗口不需要字体模组：它由插件自行绘制，并从 Windows 加载中文字体。只有原生模式需要中文字体，因为它会把中文写入游戏本身的窗口（见[字体设置](#字体设置)）。

## 游戏语言支持

游戏客户端可以使用 **英文、日文、德文或法文**，翻译一律为中文。

工作表文字、名称、物品名称与过场字幕都以游戏自己的数据行 ID 比对，因此在任何客户端语言下都能运作。任务脚本台词在游戏提供 ID 时会用 ID 比对；没有 ID 时，仍以英文脚本导出作为比对依据，所以日文、德文或法文客户端可能漏掉该行。选择对话框有少数数值是以英文或日文的格式从画面上读取，因此德文或法文的提示也可能保持原文。

## 兼容性

Sinophone 只改变画面上显示的内容，不会修改磁盘上的游戏文件，也不会修改其他插件读取到的文字，因此其他插件看到的仍是游戏自己的文字。可以和其他插件一起使用。

## 需求

- Dalamud API 15（`Dalamud.NET.SDK/15.0.0`）
- .NET 10（`net10.0-windows7.0`）
- 可选：原生模式需要的中文字体（见[字体设置](#字体设置)）

## 安装

Sinophone 不在 Dalamud 官方插件列表中。请把它加入自定义插件源：

1. 打开 Dalamud 设置（`/xlsettings`），前往 **Experimental** → **Custom Plugin Repositories**。
2. 加入 `https://raw.githubusercontent.com/raydenhui/Sinophone/main/pluginmaster.json` 并保存。
3. 打开插件安装器，搜索 **Sinophone** 并安装。

首次加载时会自动下载翻译数据（约 60 MB），因此不用更新插件也能取得新的翻译数据。若下载失败，运行 `/sinophone` 并点击 **Download / update data**。

## 字体设置

覆盖窗口不需要这一步：它会从 Windows 加载中文字体（或使用插件设置中的字体路径）。以下设置只给原生模式使用：原生模式会把翻译写入游戏本身的窗口，需要游戏自己画出中文。

游戏的字体文件没有中文字形，因此要用含中文字形的字体文件替换。从 [AtmoOmen/FFXIVFonts](https://github.com/AtmoOmen/FFXIVFonts) 挑一个：该仓库有多种字体，每个文件夹内包含一个 `.ttmp2` 模组包。把模组包交给插件，或用下列其中一个模组工具安装。

### Sinophone

插件可以自己使用模组包，不需要模组管理工具。

1. 打开 `/sinophone`，前往 **Components → Talk**。
2. 打开 **Replace the game font with a font pack**，并把路径设为 `.ttmp2` 文件，或包含字体文件的文件夹。
3. 点一下 **Reload the game font**。

插件只从你的磁盘读取模组包。它不附带字体数据，也不会修改模组包。如果 Penumbra 已经替换游戏字体，插件会把字体交给 Penumbra 处理。

### TexTools

1. 安装 [TexTools](https://github.com/TexTools/FFXIV_TexTools_UI/releases)。
2. 导入 `.ttmp2`，让它修改游戏文件。

### Penumbra

1. 安装 [Penumbra](https://github.com/xivdev/Penumbra) 并启用。
2. 导入同一个 `.ttmp2` 并启用。如果字体没有变化，打开 Penumbra 的 **Settings** 分页，前往 **Advanced**，点击 **Reload Fonts**，不用重启游戏。

### 装好之后

原生模式默认关闭。在 `/sinophone` 中打开你要用的原生模式。如果没有字体包或模组，原生模式的每一行都会显示缺字符号，而不是中文。

## 状态

Sinophone 现在已可正常使用。偶尔还是会遇到错误和缺漏：CN 与 TC 的数据导出进度落后于线上游戏，因此新增或改写的台词可能没有翻译，部分界面也仍是游戏原文。设置和翻译数据格式在版本之间仍可能变动。

如果某一行没有翻译，或发现显示异常，请打开 [issue](https://github.com/raydenhui/Sinophone/issues)。也欢迎在同一个地方提出功能需求。

## 致谢

Sinophone 建立在他人的成果之上。感谢：

- [Dalamud](https://github.com/goatcorp/Dalamud)：插件框架、addon 生命周期事件和 ImGui 封装。
- [FFXIVClientStructs](https://github.com/aers/FFXIVClientStructs)：原生模式读写的游戏结构。
- [ImGui](https://github.com/ocornut/imgui)：覆盖窗口。
- [ffxiv-datamining-cn](https://github.com/thewakingsands/ffxiv-datamining-cn) 和
  [ffxiv-datamining-tc](https://github.com/thewakingsands/ffxiv-datamining-tc)：中文游戏数据。
- [xivapi/ffxiv-datamining](https://github.com/xivapi/ffxiv-datamining)：英文 CSV 和脚本导出。
- [OpenCC](https://github.com/BYVoid/OpenCC)：简繁转换表。见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。
- [AtmoOmen/FFXIVFonts](https://github.com/AtmoOmen/FFXIVFonts)：让游戏能画出中文字体的模组包。
- [TexTools](https://github.com/TexTools/FFXIV_TexTools_UI) 和
  [Penumbra](https://github.com/xivdev/Penumbra)：安装该字体模组包的两种方式。
- [FFXIVChnTextPatch-GP](https://github.com/GpointChen/FFXIVChnTextPatch-GP)：工作表列表，以及哪些游戏文字值得翻译的参考。
- [Echoglossian](https://github.com/lokinmodar/Echoglossian)：这个插件的想法来源。
- [fftrans](https://github.com/raydocs/fftrans)：另一个独立的 Talk 处理器，确认了相同的重新写入方式。

中文文字本身来自游戏官方的简体与繁体本地化内容。

## 授权

Apache-2.0 — 见 [LICENSE](LICENSE) 与 [NOTICE](NOTICE)。
