# Glass Kanban Overlay

[English](README.md) | 简体中文

把选定的 Obsidian Kanban 列作为透明组件常驻在 Windows 桌面上。

![透明看板预览](docs/assets/glass-kanban-overlay-preview.png)

Glass Kanban Overlay 是一款本地优先的 Windows WPF/.NET 8 应用。你可以明确选择要显示的 Markdown 看板文件和 `##` 列，然后在汇总窗口中查看这些列，或把它们分别放到桌面上。

它不是 Obsidian 插件，不会扫描整个仓库，不使用 AI，不进行云同步，也不发送遥测数据。

## 项目状态

**预发布——等待维护者验收。**

当前源码可以正常构建，服务层回归测试全部通过。中性 Windows 界面测试已经覆盖新建看板、添加现有看板、卡片操作、缺失列恢复、外部编辑冲突、单实例唤起、窗口位置恢复，以及当前工具栏和菜单布局。

仍需人工验收微软拼音候选词状态下的 Enter 行为和真实多显示器布局。项目尚未发布二进制版本。GitHub 工作流只验证构建和服务层测试，不验证桌面界面行为。

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
3. **刷新**
4. **分窗到桌面**

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

## 从源码开始使用

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
