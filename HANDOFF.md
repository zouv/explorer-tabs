# 会话接力 · ExplorerTabs

状态日期：2026-11-19；无进行中任务。0.14.0 已发布：恢复等待 / 重试与状态横幅、菜单外部点击关闭、Ctrl+9·Ctrl+0、标签组内容编辑。真实鼠标、快捷键与重启恢复场景待用户复验。当前正式版本 0.14.0；不自动启动真实交互宿主或操作用户 Explorer。

源码根 D:/Git/zgithub/explorer-tabs；分支 main；origin https://github.com/zouv/explorer-tabs；正式入口 `dist/release/unpack/ExplorerTabs.exe`（0.14.0）；Release https://github.com/zouv/explorer-tabs/releases/tag/v0.14.0（标签 `v0.14.0` 指向 bdc7f92，上一正式版 v0.13.0 历史保留）。

最后验证：2026-11-19 发布前完整 `build.ps1 -NativeSmoke -CrossProcessSmoke -TabUiSmoke -CreationUiSmoke -WorkspaceUiSmoke` 通过 1181 项安全断言（核心 551、原生 63、跨进程 17、标签 UI 38、创建 UI 43、工作区 444、隔离打包 25），零警告 / 零错误；`sh manager.sh package` 产出 dev / release zip 并通过包内版本与 zip 全项校验；三个 Release 附件的远端 digest 与下载后 SHA-256 均匹配。未启动真实交互宿主，真实鼠标菜单关闭 / 快捷键 / 重启恢复场景待用户复验。

## 待用户反馈（直接在对话询问，不写人工回归文档）
- 2026-11-19 三项交互（0.14.0）：① 会话 / 工具栏菜单打开后点击 Explorer 内容区域应自动关闭菜单，且该点击仍正常作用于 Explorer；② Ctrl+9 应跳到第 9 个标签、Ctrl+0 跳到最后一个；③ “管理收藏与标签组 → 标签组”可添加 / 编辑目录、改别名、上移下移与移除组内标签，保存后追加打开应反映新顺序。
- 2026-11-19 恢复体验（0.14.0）：异常重启后点“重新打开上次会话”，应看到顶部恢复横幅（当前目录 / 总数）；Shell 未就绪时显示“Shell 尚未就绪（HRESULT 0x……）”并自动按 1 / 2 / 3 / 5 秒重试，可点“停止恢复”转为手动新建；恢复结束保留结果摘要与未打开原因。
- 2026-11-13 本轮 7 项：① 标签右键菜单出现“在右侧新建”，应在该标签右侧打开设置默认目录的新标签且不复制目录；② 关闭活动标签应立即切到左邻（无左邻切右邻），不再等待 Explorer 关闭确认；③ “关闭标签”为普通文字色；④ 菜单备注（快捷键、“追加”等）在右侧列对齐；⑤ 收藏菜单标签组项显示“追加「名称（N标签）」”并直接追加，保存标签组默认名为“我的标签组”；⑥ 应用内弹窗打开且切到后台时，按全局快捷键应把主窗口和弹窗带回前台；⑦ 工具栏空白 / 分隔线按住可拖动窗口，标签栏被占满时也能拖动。
- 2026-11-11 0.12.0 四项仍待实机复验：退出并保留窗口后重开不再提示旧会话；“加入全部窗口”；`+` / Ctrl+T 等待页与失败恢复；托管 Explorer 内 F2 重命名 Ctrl+A 全选。
- 更早版本的菜单 / 弹窗 / DPI / 托盘 / 打包确认等观感项若仍有问题，直接在对话反馈，不新增人工回归文档。

## 本轮关键改动位置
- 菜单外部点击关闭：`MenuDismissMonitor`（WH_MOUSE_LL，仅可见菜单注册）→ `MainForm.RegisterMenuLifetime` 的 Opened/Closed/Disposed；关闭入口 `MainForm.DismissMenusOutside`（菜单 / 子菜单矩形外 `Close(AppClicked)`，不吞点击）。
- 快捷键：`HostCommand.LastTab`、`HostShortcutCatalog.Defaults / Name / Upgrade`、`AppSettingsStore.Read` 旧配置补齐、`MainForm.ExecuteCommandAsync` 的 Ctrl+9 / Ctrl+0 映射。
- 标签组内容编辑：`LibraryManagerDialog` 的 `BuildGroupsPage / RefreshGroupTabs / UpdateGroupTabButtons / AddGroupTab / ChangeSelectedGroupTabDirectory / RenameSelectedGroupTab / MoveSelectedGroupTab / RemoveSelectedGroupTab`，维护 1–100 与 ActiveIndex。
- 恢复等待 / 重试：`MainForm.OpenWorkspaceAsync` → `CreateRestoredTabAsync` / `WaitForShellIdleAsync`；分类与退避 `WorkspaceRestorePolicy.IsRetryable / RetryDelay / Describe`；目录不可用 `ExplorerDirectoryUnavailableException`。
- 恢复横幅：`MainForm.WorkspaceUi.BeginRestoreBanner / ShowRestoreProgress / ShowRestoreWaiting / ShowRestoreBusy / CompleteRestoreBanner / StopWorkspaceRestore`；`restoringWorkspace` 保护 `ShowSessionOffer`，停止与工具栏取消共用 `workspaceOperation`。
- 测试：`WorkspaceTests` 策略分类；`WorkspaceUiTests.RestoreRetryUiTests`（瞬时失败重试、等待期间停止、永久失败继续）；`WorkspaceService.CreateFailures` fake 注入、`RestoreRetryDelay` 测试注入。
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
