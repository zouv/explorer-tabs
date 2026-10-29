# 会话接力 · ExplorerTabs

状态日期：2026-10-29；无进行中开发任务；0.10.0 已打包并上传 GitHub Release v0.10.0（release zip + debug zip + SHA256SUMS）；0.9.0 已发布：GitHub Release v0.9.0 已上传；2026-10-29 完成弹窗实色 1px 圆角环、设置左导航 / 目录预设 / “此电脑”、全局快捷键（呼出 / 隐藏 + 托盘），并保留此前 Wrap Tabs / `+` / dev.bat 修复；875 项安全检查通过，待真机观感、真实 Shell 此电脑与全局热键 / 托盘验证。
源码根 D:/Git/zgithub/explorer-tabs；分支 main；origin 关联 https://github.com/zouv/explorer-tabs；0.6.3 / 0.6.4 / 0.7.0 / 0.8.0 / 0.8.1 / 0.9.0 / 0.10.0 Release 均已上传。
最后验证：2026-10-29 完整 `build.ps1 -TreatWarningsAsErrors -NativeSmoke -CrossProcessSmoke -TabUiSmoke -CreationUiSmoke -WorkspaceUiSmoke` 通过，875 项（核心 471、原生 63、跨进程 17、标签 UI 38、创建 UI 40、工作空间 / 窗框 UI 246）；新增弹窗背景环 / 圆角 region、设置预设 / 全局快捷键保存与冲突、This PC token / PIDL 流程、Wrap 滚动条、`+` 不独自换行等断言；`bash check-docs.sh` 通过（ARCHITECTURE ≤ 400 行、HANDOFF ≤ 150 行）。验证时无运行中的真实宿主；真实 Shell 的此电脑创建 / 匹配、真实 RegisterHotKey / 托盘 / 隐藏呼出无法在隐藏测试中覆盖，需人工验证。构建输出在 `.runtime/build/ExplorerTabs-verify`。0.10.0 包：`dist/dev/ExplorerTabs_0.10.0_x64-unpack-debug.zip`、`dist/release/ExplorerTabs_0.10.0_x64-unpack.zip`、`dist/release/SHA256SUMS.txt`，已上传 GitHub Release v0.10.0（tag 指向 aa5959c）；`dist/release/unpack` 已刷新为 0.10.0。

## 待用户反馈（直接在对话询问，不写人工回归文档）
- 2026-10-29 dev.bat 运行中宿主确认待手动复验：先启动一次 ExplorerTabs，再运行 `dev.bat`。应列出 PID / 路径并询问；输入 N 或直接回车应取消、不关闭进程，输入 Y 应先尝试正常关闭（12 秒超时强制结束）后继续构建并启动。重定向 / 非交互输入会直接拒绝。
- 2026-10-29 弹窗 chrome 待真机复验：退出旧实例后运行 `dev.bat` 或 `.runtime/build/ExplorerTabs-verify/ExplorerTabs.exe`。所有自建弹窗的四条直边应为完全一致的 1px 灰蓝边框，无描边发虚、无右下投影；圆角是 1px 像素台阶（已确认的取舍）。请重点看左上 / 右下圆角是否连续、四边是否同粗细。
- 2026-10-29 设置面板 / 目录预设待复验：左导航三页（常规 / 标签栏 / 快捷键）对齐与控件风格；点击“我的电脑 / 用户目录 / 文档 / 下载 / 自定义…”应更新路径并在保存后用于新建标签。请分别在 100% / 125% / 150% DPI 下检查对齐和截断。
- 2026-10-29 此电脑待真机验证：在设置中选择“我的电脑”并新建标签，应打开“此电脑”；随后保存会话、重启恢复、收藏此目录并打开，均应保持虚拟位置。若失败请提供 host.log（关注 `Creation source directory provider=IPersistFolder2/ThisPC` 及创建匹配失败信息）。
- 2026-10-29 全局快捷键待真机验证：在设置 → 快捷键中点击输入框并按下组合键（如 Ctrl+Alt+E），保存后应能在任意程序前台呼出 / 隐藏宿主；隐藏后任务栏按钮消失、托盘图标出现，右键可显示 / 关闭并分离 / 关闭；再按快捷键应恢复窗口与活动标签。请测试与其它程序冲突时的内联提示、以及分组模式下的隐藏 / 恢复；捕获时按当前组合键不应隐藏窗口。
- 2026-10-29 Wrap Tabs / 滚动条 / `+` 待真机复验：先退出旧宿主再运行 `dev.bat`。开启 Wrap 后标签栏不应再同时出现横 / 纵原生滚动条；单行溢出时只在标签栏底部显示 4px 细条，多行超过行数上限时只在右侧显示，可悬停、拖动、滚轮滚动；标签行将满时 `+` 应紧贴最后一个标签（最后一个标签可能略微收窄或与其一起换行），不再独自占一行。请确认观感与滚动手感。
- 2026-10-28 窗口 / 标签栏体验待复验：退出旧宿主后运行 `dev.bat`（构建并启动 `.runtime/build/ExplorerTabs-dev/ExplorerTabs.exe`）。顶部视觉空白已缩至 3px，标签栏 / 标签顶部 4px 仍可缩放（顶部总命中约 7px），侧边 / 底部 / 四角不变；标签栏空白双击切换最大化 / 还原；无标签时调整窗口大小，欢迎页图标 / 标题 / 按钮应重新居中。请分别确认观感、顶部缩放手感与双击行为。检查时启动的探针实例已正常关闭，探针改动的 settings 窗口状态已恢复为 Bounds (0,0,800,607)、Maximized=false。
- 窗口尺寸记忆：正常退出时已保存 Bounds / Maximized，下次启动按工作区夹取恢复（本轮补了隐藏 UI 回归测试）。若你实际遇到不恢复，请告知关闭方式（X / Alt+F4 / 应用菜单 / 分离后退出）与当时的窗口状态。
- 2026-10-28 打包确认流程待复验：`package.bat` 在检测到 ExplorerTabs 运行时列出 PID / 路径并询问；输入 Y 才先尝试正常关闭（12 秒超时强制结束）并继续打包，其它输入取消；非交互输入直接拒绝。请在实际运行宿主时试一次 Y / 非 Y 两条路径。dev.bat 的 LF 行尾问题已改为 CRLF，验证时启动的开发实例已正常关闭。
- 2026-10-28 修复待复验：重启系统后打开新标签，预期不再弹「DPI awareness 不一致」；host.log 应出现 `attach.dpi-mismatch ... hostContext=PerMonitorV2(96) targetContext=Unaware(96) allowed=True`。空闲 ≥20 秒确认工具栏不再周期性闪 X；快速新建不闪，长操作约 0.5 秒后才出现取消按钮。125% / 150% 下 rundll32 Unaware 仍会拒绝并提示顶层分组模式（有意保留的安全边界），请确认窗口保持独立、可分离。
- 0.8.0 已发布：应用菜单“关于”移到最下并单独分组、“关闭并还原窗口”改为“关闭（分离所有窗口）”；“加入”按钮提示改为“加入窗口…”，候选项改为“加入【标题】窗口”并带悬浮说明；“选择要加入的…”改为“打开“加入”管理界面”；分离菜单四项改为窗口措辞；收藏菜单改为“收藏当前目录… / 收藏其他目录… / 保存当前标签组”，底部新增“打开“收藏”管理界面”（目录管理 + 组管理两页）；菜单快捷键统一靠右。请从 GitHub Release 下载或用 `dist/release/unpack` 实测，重点看长标题候选菜单宽度、快捷键对齐与管理弹窗操作。
- 0.7.0 已发布：弹窗 1px 边框、活动标签关闭后聚焦左邻、加入窗口候选菜单、应用菜单“设置 / 关于 / 关闭并还原窗口 / 关闭”（关闭显示 Alt+F4 与提示）、工具栏设置按钮移除、标签悬停 × 与缓存目录 Tooltip；右上角 X / Alt+F4 / 菜单“关闭”关闭所有会话窗口后退出，恢复退出走“关闭并还原窗口”或“分离窗口 → 恢复窗口并退出宿主”。请从 GitHub Release 下载或用 `dist/release/unpack` 实测观感、退出流程与 10 秒未关闭时的保留行为。
- 0.7.0 修复：重启后新建的 ShellBrowserWindow 由注册的 rundll32 宿主托管时，恢复 / 加入 / 关闭按精确身份接管；需下次开机出现同场景时用 0.7.0 包实测“恢复上次会话”，并在 host.log 查看 `Creation inspect; ... image=...` 确认实际宿主。
- 0.6.4 已发布：中键 / Ctrl+W 关闭、右键菜单快捷键、工具栏分组与图标、`+` 跟随最后一个标签、弹窗无边框自定义标题栏与小圆角；可从 GitHub Release 下载，或退出旧宿主后用本地 `dist/release/unpack` 刷新版本实测。
- 新入口 `dist/release/unpack/ExplorerTabs.exe`（由 package.bat 产出）：先在旧宿主分离全部并正常退出，再手动运行；未自动关闭任何宿主或 Explorer。
- 0.6.1 灰框 / Ribbon 空行、自适应标签与多行 / 单行切换、跨行排序及 125% / 150% DPI 仍待真机复验；离屏检查不等于人工验收。
- 图标仍暂用 A；assets/icons/icon-choices.png 已更新 ExplorerTabs 名称，A / B / C 选择尚未确定。

## 接续入口与安全
- 本轮 DPI 修复的关键约束：跨进程 `SetParent` 在 awareness 不同时会让 Windows 强制重置子窗口 awareness；只在「Explorer 为 Unaware/UnawareGdiScaled/System + 宿主 PerMonitor/PMv2 + 双方 GetDpiForWindow 相等」时允许，反向 / 高 DPI mismatch 仍拒绝。`MatchThreadDpiToExplorer` 不得再采纳 Unaware/System 候选，避免宿主 UI 被降级。详见 ARCHITECTURE §3 / §4；不要改成“无条件放开”。
- 0.6.3 发布、README 规范化与打包规则见 CHANGELOG；图谱 ARCHITECTURE §3.6 / §3.7，计划 ROADMAP P16 / P17 / P18。不要重做已完成的改名 / 界面 / 打包 / 发布工作。
- Windows 入口：`build.bat` / `test.bat` / `verify.bat` / `clean.bat` / `run.bat` / `ci.bat` 与 `manager.sh` 对应；提交前跑 `verify.bat`。`run.bat` / `ci.bat` 会启动真实宿主或重建 dist，仅用户明确要求时手动执行。
- ProductIdentity 的 LegacyIdentity 是兼容契约，不是漏改：继续旧 LocalAppData 目录与 per-user Local mutex；新旧宿主互斥，不自动迁移 / 覆盖数据。BrandingTests 只读旧程序集生成的合成 JSON，并使用独立 Guid 测试用户。
- `scripts/build.ps1` 默认输出 `.runtime/build/ExplorerTabs-dev`（临时快照）；正式包用 `package.bat` / `sh manager.sh package`，版本取 Directory.Build.props。测试可追加 -NativeSmoke -CrossProcessSmoke -TabUiSmoke -CreationUiSmoke -WorkspaceUiSmoke。
- 打包只清 `dist/dev`、`dist/release`、`.runtime/staging`，历史 `dist/vX-release` 原样保留；不生成 setup.exe；目标包运行中会报错，绝不 taskkill 宿主。
- `assets/screenshots/` 是 README 引用的离屏截图（单行 / 多行），不含真实 Explorer 内容；详细离屏日志仍在 artifacts/ui/v0.6.2/。
- 所有旧 dist 原文件名 / 内容和 docs/manual-test.md / docs/v0.2-test.md 原文保留；历史 CHANGELOG / verification.md 中旧名是事实，不全局替换。
- 原 D:/Git/zgithub/explorer_tab_host 已整体重命名；旧 F:/Developer/AI/vibe/agent_codex/ExplorerTabHost 备份不改；GitHub 仓库为 zouv/explorer-tabs。
