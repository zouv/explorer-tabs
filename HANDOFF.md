# 会话接力 · ExplorerTabs

状态日期：2026-11-13；发布 0.13.0 中。本轮 7 项体验修复已完成自动验证与打包，等待用户实机复验；不自动启动真实交互宿主或操作用户 Explorer。

源码根 D:/Git/zgithub/explorer-tabs；分支 main；origin https://github.com/zouv/explorer-tabs；正式入口 `dist/release/unpack/ExplorerTabs.exe`（0.13.0）；当前正式版本 0.13.0，Release 待推送；上一正式版 v0.12.0（`d0468ec`）历史保留。

最后验证：2026-11-13 完整 `build.ps1 -NativeSmoke -CrossProcessSmoke -TabUiSmoke -CreationUiSmoke -WorkspaceUiSmoke` 通过 1138 项安全断言（核心 536、原生 63、跨进程 17、标签 UI 38、创建 UI 43、工作区 416、隔离打包 25），零警告 / 零错误，日志 `.runtime/verification-v0.13.0.txt`；正式包原始二进制在隔离目录复跑 1113 项可执行检查通过，日志 `.runtime/verification-packaged-v0.13.0.txt`；dev / release zip 与 `SHA256SUMS.txt` 已生成。未启动真实交互宿主，真实 Shell 键鼠行为与弹窗体验待用户复验。

## 待用户反馈（直接在对话询问，不写人工回归文档）
- 2026-11-13 本轮 7 项：① 标签右键菜单出现“在右侧新建”，应在该标签右侧打开设置默认目录的新标签且不复制目录；② 关闭活动标签应立即切到左邻（无左邻切右邻），不再等待 Explorer 关闭确认；③ “关闭标签”为普通文字色；④ 菜单备注（快捷键、“追加”等）在右侧列对齐；⑤ 收藏菜单标签组项显示“追加「名称（N标签）」”并直接追加，保存标签组默认名为“我的标签组”；⑥ 应用内弹窗打开且切到后台时，按全局快捷键应把主窗口和弹窗带回前台；⑦ 工具栏空白 / 分隔线按住可拖动窗口，标签栏被占满时也能拖动。
- 2026-11-11 0.12.0 四项仍待实机复验：退出并保留窗口后重开不再提示旧会话；“加入全部窗口”；`+` / Ctrl+T 等待页与失败恢复；托管 Explorer 内 F2 重命名 Ctrl+A 全选。
- 更早版本的菜单 / 弹窗 / DPI / 托盘 / 打包确认等观感项若仍有问题，直接在对话反馈，不新增人工回归文档。

## 本轮关键改动位置
- 标签右键“在右侧新建”：`MainForm.PopulateTabMenu` → `QueueCreateAtRightFromMenu` → `CreateTabAtRightAsync` / `CreateTabCoreAsync(source, insertAfter)`；`AttachCreatedCore` 只按锚点插入，默认目录来自设置，不复制来源。
- 关闭立即切换：`MainForm.Commands.CloseTabAsync` 在目录读取和 `RequestClose` 之前调用 `TabManagementPolicy.CloseSuccessor` + `controller.Activate`，仍保留关闭中标签与恢复日志直到身份消失。
- 菜单备注：`MainForm.WorkspaceUi.ShowShortcut / ShowNote` 统一使用 `ShortcutKeyDisplayString` 右侧列；“恢复上次会话”改为文字 + 备注“追加”；组项为“追加「名称（N标签）」”。
- 保存标签组默认名：`MainForm.WorkspacePersistence.SaveGroupAsync` → `AskName("保存当前标签组", "我的标签组")`。
- 全局快捷键 + 模态弹窗：`MainForm.Chrome.WndProc` 不再因 `modalDepth > 0` 丢弃 WM_HOTKEY；`MainForm.GlobalHotkey.ToggleHostVisibility` 在模态时只允许 `ShowHost`；`ShowHostFromHotkey` 同时激活 `OwnedForms` 中的可见弹窗。
- 工具栏拖动：`MainForm.WorkspaceUi.InitializeWorkspaceUi` 给左右工具栏与 `ToolbarDivider` 挂 `ChromeMouseDown`；空白左键按住拖动窗口、双击最大化 / 还原、右键打开应用菜单。

## 已确认的取舍：模态弹窗期间不切后台
- 实测（`.runtime/probe` 探针）：宿主 `WindowState = Minimized` 时 Windows 会隐藏 owned 弹窗，WinForms 的 `ShowDialog` 因弹窗不可见而提前返回（`DialogResult=Cancel`）；只 `Hide()` 宿主则弹窗仍留在桌面，成为孤儿窗口。
- 因此模态弹窗打开时全局快捷键只把宿主与弹窗带到前台，不最小化 / 隐藏；要支持切后台需把应用内弹窗改成非模态或自管模态循环，改动面大，本轮不做。

## 接续入口与安全
- 测试/校验：`powershell -ExecutionPolicy Bypass -File scripts/build.ps1 -NativeSmoke -CrossProcessSmoke -TabUiSmoke -CreationUiSmoke -WorkspaceUiSmoke`；文档机械体检 `sh check-docs.sh`。
- 构建/运行：开发期 `dev.bat` 构建并启动 `.runtime/build/ExplorerTabs-dev` 快照；正式包由 `package.bat` / `sh manager.sh package` 产出；运行中宿主仅在用户输入 Y 后关闭，非交互拒绝。
- 安全铁律：不得自动启动交互宿主、接管或关闭用户已有 Explorer；自动测试限自建窗口和 fake Shell；窗口身份必须精确匹配，禁止按标题 / 最新窗口猜测；恢复日志先持久化再接管；不得覆盖运行中的发布包。
- DPI 契约：跨进程 `SetParent` 仅在 Explorer 低 awareness + 宿主 PerMonitor/PMv2 + 有效 DPI 相等时允许；`MatchThreadDpiToExplorer` 不得采纳 Unaware/System 候选，不要改成“无条件放开”。
- 关闭语义：每 session 去重，先持久化，只 `PostMessage(WM_CLOSE)`，不 detach / Quit / kill；6 秒未消失保留标签并可重试。
- 打包：唯一 `.runtime/staging/package-<Guid>` + 独占文件锁，版本 / zip 校验后事务替换 dev / release，失败回滚；历史包与 stage 不清理。
- 身份 / 数据：`ProductIdentity` 的 LegacyIdentity 是兼容契约（旧 LocalAppData 目录与 per-user Local mutex），不自动迁移 / 覆盖；旧 dist、docs/manual-test.md、docs/v0.2-test.md 原样保留。
- 仓库卫生：提交 / 推送前检查 `git status --porcelain`，用 `git grep` 扫真实姓名 / 邮箱 / 用户名 / 本机路径 / 主机名；`.runtime/`、日志、缓存、profile 不入库。不得自动提交 / 推送 / 终止旧宿主，除非用户明确要求。
