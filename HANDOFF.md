# 会话接力 · ExplorerTabs

状态日期：2026-10-28；无进行中开发任务；本轮 DPI / 取消按钮修复已实现并通过 860 项安全检查，版本号 0.8.1，已打包并上传 GitHub Release（v0.8.1）；待用户真机复验。
源码根 D:/Git/zgithub/explorer-tabs；分支 main；origin 关联 https://github.com/zouv/explorer-tabs；0.6.3 / 0.6.4 / 0.7.0 / 0.8.0 / 0.8.1 Release 均已上传。
最后验证：2026-10-28 完整 `build.ps1 -TreatWarningsAsErrors -NativeSmoke -CrossProcessSmoke -TabUiSmoke -CreationUiSmoke -WorkspaceUiSmoke` 通过，860 项（核心 455、原生 63、跨进程 17、标签 UI 38、创建 UI 39、工作空间 / 窗框 UI 248）；新增 DPI 白名单反射测试、Unaware helper 跨进程嵌入 / 拒绝分支、自动保存静默与取消按钮延迟显示测试；`bash check-docs.sh` 通过（ARCHITECTURE 172 行、HANDOFF ≤150 行）。0.8.1 release / debug 包已由 `package.bat` 产出并上传：`dist/release/ExplorerTabs_0.8.1_x64-unpack.zip`、`dist/dev/ExplorerTabs_0.8.1_x64-unpack-debug.zip`、`dist/release/SHA256SUMS.txt`；`dist/release/unpack` 已刷新为 0.8.1。

## 待用户反馈（直接在对话询问，不写人工回归文档）
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
