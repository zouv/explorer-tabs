# ExplorerTabs

<p align="center">
  <img src="assets/icons/explorer-tabs-a.svg" width="88" alt="ExplorerTabs icon" />
</p>

<p align="center">
  把 Windows 文件资源管理器变成带标签页的浏览体验。
</p>

<p align="center">
  <a href="https://github.com/zouv/explorer-tabs/releases/latest"><img alt="GitHub Release" src="https://img.shields.io/github/v/release/zouv/explorer-tabs?style=flat-square" /></a>
  <img alt="Platform" src="https://img.shields.io/badge/platform-Windows%2010%2B%20x64-0078D6?style=flat-square" />
  <img alt=".NET Framework" src="https://img.shields.io/badge/.NET%20Framework-4.8-512BD4?style=flat-square" />
  <img alt="Status" src="https://img.shields.io/badge/status-experimental-orange?style=flat-square" />
</p>

![ExplorerTabs 主界面](assets/screenshots/main-window.png)

> 离屏渲染截图：4 个标签的单行模式。文件区域为测试用空白，不含真实文件信息。

ExplorerTabs 是一个独立的 C# / WinForms 桌面程序。每个标签对应一个**真实存活的 Windows Explorer 窗口**，切换标签只调整布局与显隐，不重新导航目录、不注入 DLL、不关闭 Explorer。它没有修改 QTTabBar，也没有复制其 GPL 代码。

## 功能

- **真实标签页**：每个标签是一个原生 Explorer 窗口；地址栏、目录树、文件视图与浏览状态保持原样。
- **浏览器式界面**：无独立标题栏，标签与最小化 / 最大化 / 关闭同栏；支持拖动排序、重命名、多行标签与横向滚动；悬停标签显示关闭按钮。
- **多种新建方式**：新建标签、Ctrl+T、复制当前目录，或从“加入窗口”菜单中选择已有 Explorer 窗口加入。
- **工作空间**：会话自动保存与显式追加恢复、收藏目录、标签组、标签搜索与命令面板；收藏 / 标签组直接点击打开，编辑集中到管理界面。
- **全局快捷键**：可设置系统级组合键呼出 / 切后台；默认最小化并保留任务栏，勾选“切后台时隐藏到系统托盘”后才隐藏，托盘可呼出窗口或退出。
- **设置与目录预设**：左导航设置面板；新建标签默认目录支持用户目录、我的电脑、文档、下载与自定义选择（“此电脑”可随会话 / 收藏保存和恢复）。
- **精确的关闭与分离**：关闭只向目标 HWND 发送 `WM_CLOSE`，不杀 `explorer.exe`；分离后窗口恢复为独立 Explorer。
- **安全恢复**：接管前先持久化恢复日志；X / Alt+F4 / “关闭所有标签并退出”保存会话后请求关闭标签窗口，未关完保留宿主；“退出并保留窗口”将标签恢复为独立 Explorer，恢复失败会阻止退出。
- **窗口记忆**：正常退出后保留最后普通尺寸和最大化状态，最小化或隐藏不会覆盖保存尺寸。
- **可配置快捷键**：22 个操作可重绑或禁用，只在宿主 / 当前受管 Explorer 前台生效。
- **两种模式**：默认实验性嵌入模式；可回退到顶层窗口分组模式。

<details>
<summary>多行标签截图</summary>

![多行标签](assets/screenshots/multi-row.png)

</details>

## 系统要求

| 项目 | 要求 |
|---|---|
| 操作系统 | Windows 10 1703+ x64（开发 / 测试机为 Windows 10 22H2 19045；其他版本未验收） |
| 运行时 | .NET Framework 4.8（Windows 10 / 11 通常已内置） |
| 权限 | 普通用户即可，无需管理员权限 |
| 安装 | 免安装，解压后双击即可 |

## 下载与运行

从 [GitHub Releases](https://github.com/zouv/explorer-tabs/releases/latest) 下载：

- `ExplorerTabs_<版本>_x64-unpack.zip`：推荐，Release 免安装版；
- `ExplorerTabs_<版本>_x64-unpack-debug.zip`：Debug 版，含 PDB、构建日志与 `build-info.txt`，用于排障。

解压后双击 `ExplorerTabs.exe`。若旧版本仍在运行，请先在旧版中分离全部窗口并正常退出，再启动新版；新旧版本共用同一单实例标识，不能同时接管窗口。

首次使用建议：

1. 不要用有未完成复制、移动、删除等重要操作的 Explorer 窗口做首轮测试。
2. 启动后点 **＋ 新建标签**，或点 **加入窗口** 从菜单直接选择窗口（底部 **选择窗口…** 可批量选择）；程序不会自动捕获任何窗口。
3. 标签可拖动排序；悬停后点右侧 **×** 关闭，或右键收藏、重命名、恢复名称或分离。
4. 右上角 **X**、**Alt+F4** 或 **应用菜单 → 关闭所有标签并退出** 会关闭标签的 Explorer 窗口；若要保留独立窗口，请使用应用菜单、分离菜单或托盘里的 **退出并保留窗口**。

## 默认快捷键

| 快捷键 | 操作 |
|---|---|
| <kbd>Ctrl</kbd>+<kbd>T</kbd> | 新建标签 |
| <kbd>Ctrl</kbd>+<kbd>W</kbd> | 关闭当前标签的 Explorer（非分离） |
| <kbd>Ctrl</kbd>+<kbd>P</kbd> / <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>P</kbd> | 标签搜索 / 命令面板 |
| <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>T</kbd> | 重新打开最近关闭的标签目录（含“此电脑”） |
| <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>D</kbd> | 复制当前标签目录 |
| <kbd>Ctrl</kbd>+<kbd>Tab</kbd> / <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>Tab</kbd> | 下一 / 上一标签 |
| <kbd>Ctrl</kbd>+<kbd>1</kbd>…<kbd>8</kbd> / <kbd>Ctrl</kbd>+<kbd>9</kbd> | 第 N 个 / 最后一个标签 |
| <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>R</kbd> | 重命名当前标签（<kbd>F2</kbd> 保留给 Explorer） |
| <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>S</kbd> | 保存当前会话 |
| <kbd>Ctrl</kbd>+<kbd>,</kbd> | 设置 |

全部快捷键可在 **设置 → 快捷键** 中重绑或留空禁用；重复组合不能保存。全局快捷键默认停用，同页可启用并选择是否切后台隐藏到托盘。

## 安全模型与已知限制

ExplorerTabs 是实验性项目，**不是生产级窗口管理器**。

- 嵌入模式通过跨进程 `SetParent` 调整真实 Explorer 窗口。Shell 未必能正确适应所有焦点、拖放与 DPI 语义；上下文不一致时，仅在 Explorer 为较低 DPI awareness、宿主为 PerMonitor/PerMonitorV2 且双方有效 DPI 相同时允许嵌入；其他情况仍拒绝并提示使用顶层分组模式。跨进程 `SetParent` 可能让子窗口进程的 DPI awareness 被 Windows 重置。
- 关闭标签只请求目标临时窗口关闭，不结束 `explorer.exe`，也不操作其他 Explorer 窗口。
- 接管前先写恢复日志；关闭 / 分离、失败回滚与部分事故恢复有保护，但**硬退出、进程被杀或 Explorer 崩溃不是完全可恢复的**。
- 恢复快照包含 HWND、PID、线程 ID、进程启动时间、窗口类、样式与布局；启动恢复前会重新核对身份，但不消除所有句柄重用竞态。
- 尚未实现：自动接管、任务栏统一、跨宿主拖标签、完整浏览历史恢复、看门狗。
- 新建标签时 Shell 激活瞬间仍可能短暂显示独立窗口；不保证零闪现。
- 不会伪造前台权限，也不会用 `AttachThreadInput` 强行抢焦点。

## 数据与兼容性

为兼容已有版本，程序继续使用旧版数据目录与单实例标识：

```text
%LOCALAPPDATA%\ExplorerTabHost\
├── host.log
├── recovery.json
├── settings.json
└── workspace.json
```

- **不要删除仍有未恢复窗口的 `recovery.json`。**
- 不自动迁移或覆盖旧版设置、会话、收藏、标签组与恢复数据。
- 新旧版本共用 `Local\ExplorerTabHost.<用户>` 互斥标识，不能同时接管窗口。
- 历史发布包原文件名与内容保持不变。

## 从源码构建

需要 .NET SDK 与 .NET Framework 4.8 Developer Pack。项目不依赖第三方 NuGet 包。

```powershell
# 构建并运行基础检查
powershell -ExecutionPolicy Bypass -File scripts/build.ps1

# 完整安全自动检查（不启动真实 Explorer / 交互宿主 / 全局钩子）
powershell -ExecutionPolicy Bypass -File scripts/build.ps1 `
  -NativeSmoke -CrossProcessSmoke -TabUiSmoke -CreationUiSmoke -WorkspaceUiSmoke
```

测试是独立 console runner，不是 `dotnet test` / xUnit。默认构建输出到 `.runtime\build\ExplorerTabs-dev`（已 gitignore）；可用 `-OutputDirectory` 指定其他位置。

Git Bash 入口：

```bash
bash manager.sh build      # 构建 + 基础检查
bash manager.sh test       # 完整安全自动检查
bash manager.sh package    # 打包
bash manager.sh check-docs # 文档机械体检
```

## manager.sh 命令一览

所有命令通过 `sh manager.sh <命令>` 执行（Windows 用 Git Bash），与 `sh manager.sh help` 一致：

| 命令 | 说明 |
|---|---|
| `build` | 构建 Debug / Release 产物 |
| `test` | 完整安全自动检查（不启动真实 Explorer / 交互宿主 / 全局钩子） |
| `package` | 打包免安装 zip 到 `dist/` |
| `run` | 启动已打包的免安装版（显式交互，仅用户要求时） |
| `check-docs` | 文档体系机械体检 |
| `help` | 显示本帮助 |

Windows 批处理入口（`build` / `test` / `package` / `run` 与 `manager.sh` 对应）：

```bat
build.bat    :: 构建 + 基础检查
test.bat     :: 完整安全自动检查
verify.bat   :: 警告即错误构建 + 完整安全检查 + 文档体检
package.bat  :: 打包（检测到运行中宿主时先询问，输入 Y 才关闭）
run.bat      :: 手动启动 dist\release\unpack（run.bat dev 启动开发快照）
dev.bat      :: 构建后直接启动开发快照（检测到运行中宿主时先询问，输入 Y 才关闭）
clean.bat    :: 清理 bin / obj / .runtime（保留 dist 与 artifacts）
ci.bat       :: clean -> verify -> package
```

`clean.bat` 不会结束运行中的宿主；`dev.bat` 仅在输入 Y 确认后关闭运行中的 ExplorerTabs 并继续构建 / 启动；`run.bat` 仅用于你手动启动真实交互宿主。

## 打包

```bat
package.bat
```

版本号单一来源是 `Directory.Build.props` 的 `<Version>`。产物：

| 路径 | 内容 |
|---|---|
| `dist\dev\ExplorerTabs_<版本>_x64-unpack-debug.zip` | Debug + PDB + 构建日志 + `build-info.txt`，zip 内有同名顶层目录 |
| `dist\release\unpack\` | Release 免安装可运行目录（不含 PDB，含 README 与截图资源） |
| `dist\release\ExplorerTabs_<版本>_x64-unpack.zip` | 从 `unpack` 内部打包，解压后 exe 直接在 zip 根下 |

打包先在唯一暂存目录校验程序集版本与 zip 内容，成功后事务更新 `dist\dev` / `dist\release`，失败回滚；仅清理本次暂存目录，历史包与其他暂存目录保留。不生成 `setup.exe`；若检测到运行中的 ExplorerTabs，会列出进程并询问，只有输入 `Y` 才关闭宿主（先尝试正常关闭，12 秒后仍未退出则强制结束）并继续，其它输入取消；不会擅自终止宿主或操作用户 Explorer。

## 项目结构

```text
src/ExplorerTabs.App/     WinForms 界面、窗口外壳、Shell COM 后端
src/ExplorerTabs.Core/    生命周期、恢复、会话库、策略与 Win32 封装
tests/ExplorerTabs.Tests/ 独立测试 runner、fake Shell、隐藏 UI 与原生 stub
scripts/                  构建与打包脚本
assets/                   图标、截图与嵌入资源
docs/                     使用说明、技术栈与历史验证记录
```

更细的代码入口与链路见 [ARCHITECTURE.md](ARCHITECTURE.md)。

## 文档

- [使用说明](docs/02-使用说明.md)
- [技术栈说明](docs/01-技术栈说明.md)
- [变更记录](CHANGELOG.md)
- [路线图](ROADMAP.md)
- [代码链路图谱](ARCHITECTURE.md)
- [历史验证记录](docs/verification.md)

## 反馈

问题、建议与真机反馈请通过 [GitHub Issues](https://github.com/zouv/explorer-tabs/issues) 提交。请附上 Windows 版本、DPI 缩放、挂载模式、复现步骤，以及可选的本地诊断包（可能包含目录路径，请先检查敏感信息）。

## 许可证

当前仓库尚未添加 `LICENSE` 文件；在补充许可证前，保留所有权利。

---

ExplorerTabs 是独立项目，与 Microsoft 无关联。Windows 与 Windows Explorer 是 Microsoft Corporation 的商标或产品名称。
