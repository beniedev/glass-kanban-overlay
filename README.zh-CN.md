# Glass Kanban Overlay

[English](README.md) | 简体中文

把选定的 Obsidian Kanban 列作为透明组件常驻在 Windows 桌面上。

Glass Kanban Overlay 是一款本地优先的 Windows WPF/.NET 8 应用。你可以明确选择要显示的 Markdown 看板文件和 `##` 列，然后在汇总窗口中查看这些列，或把它们分别放到桌面上。

它不是 Obsidian 插件，不会扫描整个仓库，不使用 AI，不进行云同步，也不发送遥测数据。

## 使用示例

![透明看板汇总窗口与桌面分窗](docs/assets/glass-kanban-overlay-preview.png)

汇总窗口会集中显示所选列；点击**分窗到桌面**，可以把同一组看板拆成独立桌面组件。截图数据来自 [`examples/`](examples/) 中可公开使用的 Markdown 看板。

## 真实的人机协作场景

Glass Kanban Overlay 可以把普通 Markdown 作为人与 Agent 共享的工作界面：人在 Windows 看板上操作，获得授权的 Agent 或其他应用更新同一份文件。透明看板本身不运行 Agent、不连接云盘，也不负责执行定时任务。

### 1. 本地 Agent 与 Obsidian 协作

**流程：** 人 ↔ 本地 Agent → Markdown 看板 → Obsidian / Glass Kanban Overlay

本地 Agent 可以直接修改用户明确授权的 `.md` 看板文件。如果文件位于安装了 Kanban 插件的 Obsidian 仓库中，人也可以在 Obsidian 里读写同一块看板。Glass Kanban Overlay 检测到本地文件变化后，会在 Windows 桌面上刷新所选列。

### 2. 云端 AI 通过云盘同步

**流程：** 人 ↔ 云端 AI 应用 → 云盘 → Windows 同步客户端 → 本地 Markdown → Glass Kanban Overlay

具有云盘访问权限的云端 AI 应用可以读取或更新其中的 Markdown 看板。云盘客户端把修改同步到 Windows 后，Glass Kanban Overlay 读取本地副本并刷新看板。云端访问、凭据和文件同步仍由外部 AI 应用与云盘客户端负责。

### 3. 每日计划与周期复盘

**流程：** Agent 定时任务 → 每日更新 TODO → 同步到 Windows 看板 → 每周 / 每月复盘

外部 Agent 定时任务可以通过上述任一流程每天更新 TODO 看板。人在 Windows 上直接看到结果，并通过完成或移动卡片记录进度。之后，Agent 可以读取 Markdown 中的完成状态，根据实际完成情况整理每周和每月复盘。

## 最省事的安装方式：把仓库交给本地 Agent

把下面这一句话发给这台 Windows 电脑上你信任的本地 coding agent：

> 请从 https://github.com/beniedev/glass-kanban-overlay 安装 Glass Kanban Overlay，启动它并教我基本用法。按照 README 中给安装 Agent 的说明执行。

这种方式由 Agent 完成便携版源码构建，不代表项目提供了安装器或自动更新。

<details>
<summary>给安装 Agent 的执行说明</summary>

1. 确认电脑是 Windows x64。核对仓库 owner 和 Git remote 均为 `beniedev`；来源不符时停止安装。
2. 用户没有指定位置时，默认安装到 `%LOCALAPPDATA%\Programs\GlassKanbanOverlay`。
3. 如果目标目录已经存在，不要合并或覆盖。保留 `Data\config.json`，升级前先向用户说明并取得确认。
4. 检查 .NET 8 SDK。若未安装，先解释用途，再征得用户同意并从微软官方来源安装。
5. 把仓库克隆到工作目录，然后运行：

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\scripts\New-PortableRelease.ps1
   ```

6. 将新生成的 `dist\GlassKanbanOverlay-win-x64-portable` 目录复制到安装位置。不要复制仓库中的 `Data` 或 `Log` 内容。
7. 为 `GlassKanbanOverlay.exe` 创建桌面快捷方式，把工作目录设为安装目录，并启动一次。
8. 安装过程中不要扫描 Vault，也不要擅自添加 Markdown 文件；每块看板都应让用户明确选择。
9. 启动后向用户说明如何新建看板、添加现有 `.md` 看板、选择列、分窗到桌面、打开源文件，以及在哪里找到本地配置 `Data\config.json`。

</details>

## 项目状态

**源码已公开——维护者实际试用已完成。**

当前源码可以正常构建，服务层回归测试全部通过。维护者已在真实 Windows 桌面上试用新建看板、添加和移除看板、卡片操作、汇总与分窗、配置页面，以及当前工具栏和菜单布局。中性界面测试还覆盖缺失列恢复、外部编辑冲突、单实例唤起和窗口位置恢复。

GitHub 工作流负责验证构建和服务层测试。桌面界面、输入法特定行为和不同显示器布局仍属于依赖实际环境的人工兼容性检查。项目尚未发布二进制版本；带标签的二进制发布仍属于单独的发布决策。

## 可以做什么

- 从 `TODO / DONE` 或 `TODO / DOING / DONE` 模板新建 Markdown 看板。
- 添加现有 Markdown 看板，并选择其中一个 `##` 列。
- 在汇总窗口中并排查看选定的列。
- 把选定的列拆分为独立桌面组件。
- 添加、编辑、完成、置顶、同列排序、归档或删除卡片。
- 使用系统默认编辑器打开原 Markdown 文件。
- 选择桌面、始终置顶或普通窗口模式。
- 把移出屏幕的窗口恢复到最近的可用显示区域。
- 编辑过程中遇到外部文件刷新时保留尚未提交的草稿。

看板长标题和组件说明会在可用宽度内自动换行，操作按钮始终保留在独立的固定列中。

## 主要操作

汇总窗口工具栏按以下顺序显示：

1. **新建看板**
2. **添加现有看板**
3. **刷新看板**
4. **配置看板**
5. **分窗到桌面**

每个看板菜单按以下顺序显示：

1. **分窗到桌面**
2. **打开原 Markdown 文件**
3. **配置窗口**
4. **移除看板**

从应用中移除看板会关闭对应分窗并清除其窗口状态，但不会删除或改写原 Markdown 文件。

## Obsidian Kanban 兼容范围

解析器支持 [Obsidian Kanban 插件](https://github.com/obsidian-community/obsidian-kanban)所使用的 Markdown 子集：

- `##` 标题代表看板列。
- `- [ ] 编写 README` 这类复选任务代表卡片。
- 保留 frontmatter、普通段落、块 ID 和 Kanban 设置块。
- [`examples/`](examples/) 提供可以公开使用的中性示例。

最小设置块如下：

````markdown
%% kanban:settings
```
{"kanban-plugin":"board"}
```
%%
````

### 归档行为

归档不等于删除。删除会移除卡片行；归档会把卡片移出当前列，写入文件末尾附近的 Markdown 归档区域。

应用支持标准的 `## Archive` 标题，也支持紧跟在 `***` 归档分隔线后的简体中文标题 `## 归档`。

````markdown
***

## 归档

- [ ] 已归档卡片

%% kanban:settings
```
{"kanban-plugin":"board"}
```
%%
````

存在以下 Obsidian Kanban 归档设置时，应用会原样保留：

- `archive-with-date`
- `archive-date-format`
- `archive-date-separator`
- `append-archive-date`
- `max-archive-size`

## 安全边界

Markdown 写入采取保守策略：

- 只读写你在应用中明确添加的看板文件。
- 默认阻止看起来像归档或备份的路径。
- 每次写入前重新读取源文件。
- 如果组件加载后目标列发生变化，则拒绝写入。
- 编辑卡片时还会确认原任务行仍在预期位置。
- 同一个看板的写入会在应用实例之间串行执行，并通过同目录原子替换提交。
- 卡片文字必须保持单行，不能注入额外 Markdown 行。
- 添加卡片只插入一行任务；其他操作只修改目标行、目标列或归档区域。

列不存在时，应用会明确提供恢复选项：重新选择现有列、确认后创建缺失标题、打开源文件，或从应用中移除看板。

## 界面语言

目前维护的界面语言只有：

- English
- 简体中文

**Auto / 跟随 Windows** 会在中文 Windows 区域使用简体中文，其他区域使用英文。旧的繁体中文地区代码会迁移到简体中文；已经移除的语言值会回到自动选择，不会导致配置加载失败。

README 同样只维护两种语言：本简体中文文件和 [英文 README](README.md)。

## 手动从源码运行

环境要求：

- Windows
- .NET 8 SDK

构建并运行回归测试：

```powershell
dotnet build .\DesktopOverlayBoard.sln
dotnet run --project .\Tests\DesktopOverlayBoard.Tests.csproj
```

从源码或已有本地构建启动：

```powershell
.\run-glass-kanban-overlay.ps1
```

生成新的 win-x64 自包含便携候选包：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\New-PortableRelease.ps1
```

打包脚本拒绝覆盖已有候选包。它会构建并测试应用、发布自包含程序、加入许可证、NOTICE 和中英文 README，然后生成 zip。脚本不会打包本地配置或日志，也不会替换当前平铺的本地试用程序。

## 本地配置

运行配置保存在：

```text
Data\config.json
```

该文件包含机器相关路径，因此不会提交到仓库。公开示例请使用 [`Data/config.sample.json`](Data/config.sample.json)。测试会创建临时的中性看板文件，不依赖维护者的真实仓库。

## 当前限制

Glass Kanban Overlay 暂不提供：

- 自动扫描整个仓库；
- 跨列拖放或批量选择；
- 自动归档清理；
- 对所有复杂嵌套 Obsidian Kanban 卡片格式的完整兼容保证；
- 安装器、自动更新、Microsoft Store 包、代码签名或多架构发布矩阵。

## 仓库结构

```text
glass-kanban-overlay\
  App.xaml                          共用玻璃样式
  MainWindow.xaml(.cs)              汇总窗口、托盘和分窗调度
  SingleBoardWindow.xaml(.cs)       单个看板列桌面组件
  SettingsWindow.xaml(.cs)          看板、语言和启动设置
  Services\MarkdownKanbanService.cs Markdown 解析和写入安全边界
  Services\LocalizationService.cs   英文和简体中文界面文案
  Services\ConfigService.cs         配置加载、保存和迁移
  Services\SingleInstanceService.cs 单实例互斥与唤起信号
  Services\WindowPlacementService.cs 窗口模式和显示区域恢复
  Models\*.cs                       配置和 Kanban 数据模型
  Tests\Program.cs                  服务层回归测试
  Data\config.sample.json           公开配置示例
  docs\assets\                     可公开的预览图
  scripts\New-PortableRelease.ps1  便携候选包生成脚本
```

## 开发说明

编辑仓库前请先阅读 [AGENTS.md](AGENTS.md)，尤其注意：

- 不要把应用扩展成仓库扫描器。
- 不要加入 AI、云同步、遥测或后台网络行为。
- 不要削弱冲突检查，也不要静默吞掉写入错误。
- 代码改动必须通过解决方案构建和回归测试。
- 界面改动必须在真实 Windows 环境中启动并截图验证。

## 报告问题

请提供 Windows/.NET 版本、中性示例看板和失败操作。不要附加 `Data/config.json`、日志、私人仓库文件或凭据。
