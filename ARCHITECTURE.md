# 代码链路图谱 · ExplorerTabs

## 0. 边界
源码根 D:/Git/zgithub/explorer-tabs；旧备份 F:/Developer/AI/vibe/agent_codex/ExplorerTabHost 只读；原 D:/Git/zgithub/explorer_tab_host 已整体重命名，不再作为工作副本。
C# / WinForms / net48 / x64，无第三方 NuGet。每个标签对应常驻真实 Explorer；切换只显隐 / 布局，不重新导航、注入或杀共享进程。
图谱用于定位，函数名优先于行号；入口失配先检索源码并订正本表。

## 0.5 任务作用域路由
| 任务 | 首读文件 | 不必读 |
|---|---|---|
| 新建 / 复制 / Shell ABI | MainForm.cs、TabStrip.cs、ExplorerCreation.cs、ShellExplorerBackend.cs、ShellInteropTests.cs、ResponsivenessTests.cs | 工作区库、UI 配色 |
| 菜单 / 弹窗生命周期 | MainForm.WorkspaceUi.cs、MainForm.Chrome.cs、MainForm.GlobalHotkey.cs、MainForm.Commands.cs、MainForm.cs、LibraryManagerDialog.cs、WorkspaceUiTests.cs、MenuConsistencyTests.cs | Shell ABI 与原生恢复 |
| 会话 / 常用目录 / 标签组 | MainForm.WorkspacePersistence.cs、MainForm.WorkspaceUi.cs、WorkspaceLibrary.cs、WorkspaceUiTests.cs | 原生恢复与 ABI |
| 关闭 / 重开目录 | MainForm.Commands.cs、TabStrip.cs、HostController.cs、WindowClosing.cs、WorkspaceTests.cs、ResponsivenessTests.cs | TaskbarService、创建 ABI |
| 搜索 / 命令 / 快捷键 | MainForm.Commands.cs、QuickPickerDialog.cs、TabKeyboardHook.cs、EmbeddedInputPolicy.cs、HostShortcuts.cs | 原生恢复、文档模板 |
| 浏览器式窗框 / 图标工具栏 | MainForm.Chrome.cs、ModernControls.cs、MainForm.WorkspaceUi.cs、ChromeUiTests.cs | Shell ABI、恢复库 |
| 灰框 / 空标题区 | MainForm.Chrome.cs、NativeWindowApi.cs 的 MeasureEmbeddedTitleInset / Layout、ChromeUiTests.cs、EmbeddedChromeTests.cs | Shell ABI、恢复呈现内部链路 |
| 多行 / 排序 / 设置 | TabStrip.cs、SettingsDialog.cs、AppSettings.cs、WorkspaceUiTests.cs | ShellExplorerBackend |
| 全局快捷键 / 托盘 / 此电脑 | MainForm.GlobalHotkey.cs、GlobalHotkeys.cs、GlobalHotkey.cs、GlobalHotkeyPolicy.cs、WindowPreferencesTests.cs、FolderTarget.cs、ExplorerCreation.cs、ShellViewReference.cs、SettingsDialog.cs | 菜单内部实现、工作区库内部 |
| 窗口尺寸 / 最大化记忆 | MainForm.WindowPreferences.cs、WindowPlacementTracker.cs、AppSettings.cs、WindowPreferencesTests.cs | Shell ABI、工作区库 |
| 分离 / 事故恢复 / 重启会话引导 | HostController.cs、NativeWindowApi.cs、RestorePresentation.cs、RecoveryJournal.cs、Program.cs、MainForm.WorkspaceUi.cs、MainForm.cs（ClearRestoredSessionOnExit）、Program.JournalTests / WorkspaceUiTests | 收藏管理、快捷键设置 UI |
| 产品 / 工程改名 / 旧版兼容 | ProductIdentity.cs、Program.cs、BrandingTests.cs、工程文件、scripts/build.ps1 | Shell ABI、原生恢复执行 |
| 发布 / 自动验证 / 打包 | scripts/build.ps1、scripts/package.ps1、build.bat / test.bat / verify.bat / clean.bat / run.bat / dev.bat / ci.bat、package.bat、manager.sh、Directory.Build.props、tests/ExplorerTabs.Tests | 用户历史反馈、界面实现细节 |

## 1. 文件职责
| 文件 | 职责 |
|---|---|
| src/ExplorerTabs.Core/ProductIdentity.cs | 产品名 ExplorerTabs；集中旧数据目录与跨版本单实例身份，不执行迁移 |
| tests/ExplorerTabs.Tests/BrandingTests.cs、Fixtures/v0.6.1/ | 旧程序集生成的合成 JSON，字节级重序列化、改名元数据 / 资源与独立测试用户互斥 |
| src/ExplorerTabs.App/Program.cs | 单实例 / DPI / 事故恢复，独立读取 settings.json 与 workspace.json；坏库禁用持久化 |
| src/ExplorerTabs.App/MainForm.Chrome.cs | 无独立标题栏、原生 thick frame（薄 DWM 边框 + 不可见缩放边距）、顶部边框并入客户区并用同色带上报缩放 / 最大化工作区、标签空白拖动、窗口按钮、托管应用菜单与图标资源 |
| src/ExplorerTabs.App/ModernControls.cs | GDI+ 矢量图标、图标 / 文字按钮、菜单配色、SidebarButton / ThemedCheckBox / FieldHost / PresetButton、DialogStyle / DialogTitleBar 无边框弹窗、同一 GraphicsPath 生成的外 / 内圆角、实色 1px 边框环、欢迎页 ResizeRedraw、PendingCreationPanel 等待转圈；不依赖 SVG 运行库 |
| src/ExplorerTabs.App/AboutDialog.cs | 应用介绍：嵌入图标、程序集版本、作者 @zouv、简短说明与关闭按钮；沿用 ShellDialog 边框 / 圆角 |
| src/ExplorerTabs.App/MainForm.cs | 接管、激活、分离、创建、轮询、模态范围与 UI 状态同步；新建立即显示等待页并在失败 / 取消时恢复原活动窗口；退出并保留窗口成功后清除已保存会话 |
| src/ExplorerTabs.App/MainForm.WorkspaceUi.cs | 工具栏 / 加入 / 分离 / 收藏 / 会话菜单、共享能力判断 / 退出项 / 动态标题；“加入全部窗口”批量加入、快照直接打开、管理入口；菜单所有权 / 延后释放模态 scope、失效原窗口 / 显式重开会话自适应横条与 QueueUi |
| src/ExplorerTabs.App/LibraryManagerDialog.cs | “管理收藏与标签组”：收藏目录 / 标签组两页；打开、收藏目录、重命名、移除、删除；修改留在私有副本，保存或打开时由宿主统一校验持久化，取消丢弃 |
| src/ExplorerTabs.App/MainForm.WorkspacePersistence.cs | 按标签数预算的有界串行目录捕获、完整会话保存、串行显式恢复、自动保存静默、退出独立 30 秒上限 |
| src/ExplorerTabs.App/MainForm.Commands.cs | 精确关闭、150ms 目录等待、75ms 指定目标确认、请求后左邻聚焦 / 消失后清理重开栈、标签搜索、命令面板与路由 |
| src/ExplorerTabs.App/QuickPickerDialog.cs | 双行搜索列表，AND 分词、键盘导航、无匹配与 Esc |
| src/ExplorerTabs.App/TabStrip.cs | 标签按钮 / 菜单按会话复用、即时新建占位 / 活动占位 / 关闭中状态；本地点击 / 拖动 / 悬停关闭按钮；自行测量与定位单行 / 多行标签，4 逻辑 px 自绘圆角滚动条（单行仅横向、多行超过行上限仅纵向），不使用原生 AutoScroll；缓存目录 Tooltip；尾部新建按钮紧贴最后一个标签（空间不足时优先微缩最后标签、否则随其一起换行），不参与标签测宽 / 排序；不接管文件拖放 |
| src/ExplorerTabs.App/SettingsDialog.cs、RenameTabDialog.cs | 左导航设置（常规 / 标签栏 / 快捷键）、统一主题控件、目录预设（用户 / 此电脑 / 文档 / 下载 / 自定义）、全局快捷键捕获与校验、切后台隐藏到托盘勾选项、快捷键页滚动容器；标签别名 |
| src/ExplorerTabs.App/GlobalHotkeys.cs、MainForm.GlobalHotkey.cs | RegisterHotKey / UnregisterHotKey / 占用探测；前台感知的呼出 / 最小化 / 可选托盘隐藏、托盘菜单 Opening 刷新并复用退出项、WM_HOTKEY 处理 |
| src/ExplorerTabs.App/TabKeyboardHook.cs | 专用消息线程；前台 / 菜单判定，复制不可变 scope，回调只排队；托管 Explorer 焦点编辑框的 Ctrl+A 补发 EM_SETSEL |
| src/ExplorerTabs.Core/EmbeddedInputPolicy.cs | 仅识别“物理 Ctrl+A + 活动嵌入 Explorer + 编辑框类名”的补发条件；不消费原始按键 |
| src/ExplorerTabs.Core/HostController.cs、Models.cs | 身份与恢复日志先行的标签生命周期；Activate 可见性与回滚；PruneTargets 精确确认指定目标；关闭请求保留日志直到消失 |
| src/ExplorerTabs.Core/ExplorerCreation.cs | 独立创建与只读目录 STA 单飞；CreatedExplorer 呈现恢复 lease / 取消交接 |
| src/ExplorerTabs.App/ShellExplorerBackend.cs | COM STA 后端、精确 Shell view / PIDL 匹配、原生 dual ABI；仅核验本次新建 HWND 后 Visible=false |
| src/ExplorerTabs.Core/WorkspaceLibrary.cs | 会话 / 收藏 / 标签组模型、深复制、验证、原子库存储、搜索和行数策略 |
| src/ExplorerTabs.Core/HostShortcuts.cs | 22 操作、默认与候选组合、重复验证、物理 down/up / 注入 / repeat 策略 |
| src/ExplorerTabs.Core/WindowClosing.cs | identity 校验后 PostMessage(WM_CLOSE)，不 detach / Quit / kill |
| src/ExplorerTabs.Core/FolderTarget.cs、AppSettings.cs | 普通目录 / “此电脑”token 验证；设置默认值兼容、GlobalHotkey / HideToTrayOnGlobalHotkey 可选字段、深复制与原子写盘 |
| src/ExplorerTabs.App/MainForm.WindowPreferences.cs、src/ExplorerTabs.Core/WindowPlacementTracker.cs | 首次 OnLoad 缩放后恢复物理 Bounds；记住最后普通矩形 / 非最小化状态；最小化不写图标坐标，呼出保留最大化 |
| src/ExplorerTabs.Core/GlobalHotkeyPolicy.cs、tests/ExplorerTabs.Tests/WindowPreferencesTests.cs | 热键显隐 / 前台 / 托盘纯决策；偏好 JSON 兼容、布局捕获、隐藏窗口关闭 / 重开与设置 DPI 可达性回归 |
| src/ExplorerTabs.Core/GlobalHotkey.cs | 全局快捷键字符串解析 / 规范化 / 校验 / 与宿主快捷键冲突检测；A–Z、0–9、F1–F12 + Ctrl/Alt/Shift |
| src/ExplorerTabs.Core/NativeWindowApi.cs、TaskbarService.cs、RestorePresentation.cs、RecoveryJournal.cs | Win32 / Shell 操作、DPI 兼容白名单、恢复结构 / 显示顺序、只读呈现采样、事故日志 |
| tests/ExplorerTabs.Tests/DeferredCreationTests.cs、ChromeUiTests.cs | fake 创建取消 / 失败 / lease 并发与隐藏真实窗框 / 紧凑布局 / 图标 tooltip；不运行真实 Explorer |
| tests/ExplorerTabs.Tests/EmbeddedChromeTests.cs | 自注册 Ribbon 类与隐藏自建 HWND 测标题区测量 / 根窗口裁切 / 未知布局回退 / 非 Explorer 恢复；不操作真实 Shell |
| assets/icons/、assets/screenshots/ | 三版原创 SVG、对比预览、暂用 A 的多尺寸 ICO / 嵌入资源；README 引用的离屏单行 / 多行截图（`main-window.png` 由 `--render-tabs <png> 1100`、`multi-row.png` 由 `--render-workspace <png> 1100` 生成，不含真实 Explorer 内容） |
| tests/ExplorerTabs.Tests/WorkspaceTests.cs、WorkspaceUiTests.cs | 库 / 快捷键 / close / STA 策略；隐藏 UI 会话、迟到结果、多行跨行拖动、设置 / 搜索与管理副本提交 |
| tests/ExplorerTabs.Tests/MenuConsistencyTests.cs | 菜单文案 / 空态 / 忙碌 / 只读 / 快照 / 身份 / 容量 / 快捷键 / 托盘刷新；可选三档缩放离屏菜单 |
| tests/ExplorerTabs.Tests/ResponsivenessTests.DuplicatePlacementUiTests / WorkspaceUiTests.StartupRecoveryOfferTests / Program.AllMissingJournalTests / StartupRecoveryTests | 复制锚点 / 尾插控件复用、全失效仅清日志 / 启动分流与横条显式会话重开；fake 与独立日志，不读实际用户数据 |
| tests/ExplorerTabs.Tests/Program.cs、FeatureTests.cs、CreationUiTests.cs、ShellInteropTests.cs | 基线断言、fake Shell、自建透明窗口、隐藏 UI 与原生 ABI stub |
| scripts/build.ps1、scripts/package.ps1、build.bat / test.bat / verify.bat / clean.bat / run.bat / dev.bat / ci.bat、package.bat、manager.sh、Directory.Build.props | net48 / x64 / 版本单一来源、受保护开发快照、可选安全 smoke；.bat 仅做参数透传与前置检查；dev.bat 构建后启动开发快照，运行中宿主需用户确认后才关闭；package 产出 dev zip + release unpack/zip，运行中宿主需用户确认后才关闭 |
| scripts/PackageTransaction.ps1、scripts/test-package.ps1 | 包路径约束、双目录发布 / 回滚、zip 全项校验；fake 进程 / 构建与独立 fixture 的打包故障注入，不写真实 dist |
| src/ExplorerTabs.Core/WorkspaceCapturePolicy.cs、tests/ExplorerTabs.Tests/ResponsivenessTests.cs | 会话捕获单项 / 总量 / 退出预算；隐藏可见性、占位绑定、控件复用、快速关闭及菜单回归 |
| dist/、artifacts/ | Git 忽略的发布与验证产物：`dist/dev` 版本化 debug zip、`dist/release/unpack` + release zip；历史 `dist/vX-release` 原样保留；artifacts/ 存离屏图像与验证日志 |
| docs/ | 使用 / 历史反馈；不改用户已填清单 |
| AGENTS.md、CLAUDE.md、ARCHITECTURE.md、ROADMAP.md、CHANGELOG.md、HANDOFF.md | 规则、图谱、计划、发布、进行中接力 |
| README.md、check-docs.sh、.claude/skills/ | 使用入口、模板机械体检与显式文档技能 |

## 2. 任务 → 代码入口
| 任务 | 文件 / 函数 | 约束 |
|---|---|---|
| 产品 / 旧版身份 | ProductIdentity.GetStateDirectory / GetMutexName、Program.Main、BrandingUnitTests | 继续旧 LocalAppData 目录与 per-user Local mutex；仅测试合成身份，不争用实际宿主 |
| 灰框 / 空栏 | MainForm.Chrome.cs 的 CreateParams / WndProc、NativeWindowApi.MeasureEmbeddedTitleInset / EmbeddedViewportBounds / Layout | 保留原生 thick frame 与默认 NC 计算；仅精确 Explorer 嵌入根窗口裁切，不改 Ribbon / 恢复快照 |
| 新建 / 复制 | MainForm.CreateTabAsync / QueueCreateFromMenu / AttachCreated / AttachCreatedCore、PendingCreationPanel、ExplorerCreationWorkflow.Run / Verify、IExplorerCreationBackend.IsAt | 15 秒；立即把占位标签设为活动并显示等待转圈，旧活动窗口先隐藏；成功切到真实窗口，失败 / 取消恢复原活动窗口；复制来源引用锚点、当前右侧插入、来源消失退尾，普通 / workspace 尾插；排除已有与受管 HWND，核验后延迟呈现；结果 using / Dispose，成功 MarkAttached；迟到不接管；“此电脑”token 跳过 DirectoryExists，创建后用 PIDL `ILIsEqual` 精确匹配 |
| 全局快捷键 / 托盘 | MainForm.ApplyGlobalHotkey / ToggleHostVisibility / MinimizeHostForHotkey / HideHostForHotkey / ShowHostFromHotkey / ShowTrayIcon、GlobalHotkeyRegistration.Apply / Probe、MainForm.Chrome.WndProc | Shown 后注册、ApplySettings 重注册 / 失败回滚、Dispose 注销；WM_HOTKEY 仅在非模态 / 非退出时排队；GlobalHotkeyPolicy.Decide 按可见 / 最小化 / 精确前台身份决策；默认最小化、可选托盘隐藏；Show 恢复最后非最小化状态与活动标签 |
| 托盘菜单 | MainForm.CreateTrayMenu / PopulateTrayMenu / AddExitMenuItems | Opening 重建条目并刷新退出可用状态；“显示 ExplorerTabs”与共用退出项；不改全局热键注册契约 |
| DPI / 嵌入兼容 | NativeWindowApi.ValidateAttach / AllowDpiMismatch / IsDpiContext / MatchThreadDpiToExplorer / PreferredThreadDpiContext、MainForm.SetCreationControls | 宿主保持 PerMonitor/PMv2；仅低 awareness 子窗口 + PerMonitor/PMv2 宿主 + 有效 DPI 相等时允许跨 context SetParent；其余 mismatch / context 不可读仍拒绝并记录 host/target；自动保存不显示取消 |
| 窗框 / 图标工具栏 | MainForm.InitializeChrome / CreateParams / WndProc / SyncTopResizeBand、InitializeWorkspaceUi / UpdateToolbarHints、TabStrip.PreferredChromeHeight | 无 WS_CAPTION 但保留原生 thick frame / 缩放样式；左右 / 下边框留在客户区外，顶部边框并入客户区，视觉带仅 3 逻辑 px；TabStrip / TabButton 顶部 4px 转发 HTTOP，使顶部命中区仍约 7px；`WM_NCACTIVATE` 保留原边框状态、`WM_GETMINMAXINFO` 按边框外扩使最大化客户区等于工作区；最大化时顶部带收为 0；最小窗口 736×516 保持旧内容宽度；右上角 X / Alt+F4 关闭所有会话窗口后退出；100% 单行栏各 36 px |
| 工具栏 / 标签栏布局 | MainForm.InitializeWorkspaceUi、MainForm.PaintToolbarDivider、TabStrip.SetTrailingControl / MeasureWrapped / UpdateRows / PositionChildren、MainForm.ChromeMouseDown | 加入窗口与分离窗口相邻在左组；右组只保留搜索（设置移入应用菜单）；新建按钮紧跟最后一个标签且不参与测宽 / 拖动排序，行满时优先微缩最后标签、仍不够则随最后标签一起下移；标签栏空白双击切换最大化 / 还原（MouseDown Clicks=2，不依赖被 HTCAPTION 吞掉的 MouseDoubleClick） |
| 弹窗样式 / 标题栏 | DialogStyle.Apply / ShapeChildren、DialogTitleBar、SidebarButton / ThemedCheckBox / FieldHost / PresetButton、QuickPickerDialog / SettingsDialog / RenameTabDialog / WindowPicker / AboutDialog / LibraryManagerDialog | 无边框自定义标题栏（标题 / 关闭 / 拖动）替代原生厚边框；Form.BackColor=DialogBorder 形成实色 1px 边框环，窗口 region 与子控件裁剪共用同一 GraphicsPath（外 / 内圆角相差 1px）；无描边、无 CS_DROPSHADOW；四边厚度一致，圆角为 1px 台阶；不改系统文件 / 文件夹选择框 |
| 应用菜单 / 关于 | MainForm.CreateAppMenu / ShowAppMenu / ShowAbout / AddExitMenuItems、AboutDialog | 图标 / 标签栏空白右键统一菜单：设置… / 退出并保留窗口 / 关闭所有标签并退出 / 关于…；关闭项显示 Alt+F4；退出结果 Tooltip 与托盘共用；不提供移动 / 大小 / 最大最小化 |
| 加入已有窗口 | MainForm.ShowAddWindowsMenu / CreateAddWindowsMenu / MenuText / OpenWindowPicker / AttachSelectedWindows、WindowPicker | 直接列出未接管候选标题，完整标题与加入说明放 Tooltip，不显示 PID / HWND；分隔线后依次“加入全部窗口”（当前候选快照，无候选时禁用）与“选择窗口…”；转义 & 并按文本元素截断；仍走精确身份校验 |
| 标签右键菜单 / 中键 / 悬停关闭 | MainForm.CreateTabMenu / PopulateTabMenu / ShowShortcut、TabStrip.MiddleClose / TabButton.HitClose、MainForm.CloseTabAsync | 关闭 / 复制 / 重命名 / 分离文案与状态统一；“收藏此目录…”绑定右键目标而非活动标签；快捷键仅右侧展示实际绑定；中键 / Ctrl+W / × 仍走精确 WM_CLOSE |
| 新建呈现所有权 | BrowserReference.DeferPresentation、CreatedExplorer.BindCancellation / MarkAttached / Dispose、HostController.Attach | 恢复回调不携 COM 跨线程；新建隐藏窗日志记录独立可见恢复；失败 / 取消仅恢复精确身份的未嵌入窗 |
| 源目录 / 当前目录 | ShellExplorerBackend.ReadDirectory、StaExplorerTabService.ReadDirectoryAsync、MainForm.ReadTabDirectoryAsync | 自己的 Shell view HWND 子孙关系，前后 identity 核验；只读 STA，不导航 |
| 精确关闭 | MainForm.CloseTabAsync、HostController.RequestClose、NativeWindowCloser.RequestClose | 每 session 去重；先持久化；只 WM_CLOSE；不早删日志 |
| 关闭完成 / 重开 | MainForm.CloseTabAsync / PollClosingWindows / CompleteClosedTabs / ReopenTabAsync、TabManagementPolicy.CloseSuccessor、HostController.PruneTargets / PruneLost | 目录等候最多 150ms，75ms 定时只查待关闭目标；发送请求后立即切左邻 / 右邻；消失并成功清日志才移除标签 / 入最多 20 项重开栈，慢目录不使用过期缓存；6 秒拒绝保留并可重试 |
| 捕获 / 保存 | MainForm.CaptureWorkspaceAsync / SaveSessionAsync / SaveGroupAsync、WorkspaceCapturePolicy.TimeoutMilliseconds | 每项 2.5 秒；会话预算随标签数增长最高 265 秒，退出独立最高 30 秒；读取失败 / 排序 / 别名 / 活动变化保留旧库，内容相同不重写 |
| 会话 / 组 / 收藏打开与启动提示 | MainForm.OpenWorkspaceAsync / ConfigureStartupRecovery / ShowSessionOffer / LayoutSessionOffer / RefreshSessionOfferControls | 显式串行创建、逐项 15 秒、可取消、成功保留、最后激活保存索引；总量不超过 100；启动失效提示不自动创建 |
| 菜单能力 / 会话 | MainForm.MenuIdle / CanUseTab / CanReadDirectory / CanDetachTabs / CanCaptureWorkspace / CanOpenWorkspace / CreateSessionMenu / ClearSavedSession | 忙碌 / 关闭中 / 读取中 / 恢复态 / 100 标签上限禁冲突项；MenuIdle 不含菜单自身 modalDepth；恢复快照深复制后追加，清除不删收藏 / 组 / 标签 |
| 菜单生命周期 | MainForm.ShowManagedMenu / ShowManagedMenuAtScreenPoint / RegisterManagedMenu / RegisterMenuLifetime / QueueManagedMenuCleanup / ReleaseClosedManagedMenus / DisposeManagedMenus / PopulateTabMenu / QueueMenuAction | Closed 只排队；临时菜单延后 Dispose，持久标签菜单只释放 scope 并在 Opening 更新；快速重开复用旧 scope；按钮移除 / 宿主销毁才释放持久菜单，销毁不恢复窗口 |
| 收藏 / 组管理 | MainForm.CreateLibraryMenu / ShowLibraryMenu / ShowLibraryManager / AddFavoriteAsync / AddBookmark / CommitLibrary、LibraryManagerDialog | 条目深复制后直接打开 / 追加；修改集中管理弹窗；只读库禁修改不禁打开；持久化成功才换内存，删除记录不关窗口；保存或打开时提交副本修改，取消丢弃 |
| 搜索 / 命令 | MainForm.ShowTabPickerAsync / ShowCommandPicker / CreateCommandEntries / CanExecuteMenuCommand / ExecuteCommandAsync、QuickPickerDialog.Filter | 搜索前有界读取目录，失败只显示缓存标签；命令面板统一术语 / 实际快捷键 / 管理入口并筛选可执行项，收藏与组携深复制快照 |
| 键盘 | TabKeyboardHook.OnKey / Eligible / UpdateScope / SelectAllInActiveEdit、HostShortcutPolicy.Handle、EmbeddedInputPolicy.IsSelectAll / IsEditClass | 不消费其他程序、菜单 / 独立对话框、注入 / Win 组合；UI 执行时再验范围；仅物理 Ctrl+A 且焦点属于活动嵌入 Explorer 的 Edit / RichEdit 时补发 EM_SETSEL，不消费原始按键 |
| 多行 / 拖动 / 悬停关闭 | TabStrip.ConfigureLayout / MeasureTabWidth / MeasureWrapped / UpdateRows / PositionChildren / ApplyScroll / Reveal / UpdateInsertion / TooltipFor、TabButton.HitClose | 按各标题实际宽度自行分行定位，最多 2–8 可见行；滚动条为 4 逻辑 px 自绘圆角浮条，单行溢出只出横向、多行超行上限只出纵向，两轴不会同时出现，横向条不增加标签栏高度；尾部按钮空间不足时优先微缩最后标签、达到最小宽仍放不下才随最后标签下移；为 × 固定预留 20 逻辑 px；跨行不重挂窗口 |
| 窗口尺寸记忆 | MainForm.InitializeWindowPlacement / OnLoad / CaptureWindowPlacement / RestoreHostWindowState、WindowPlacementTracker.Capture、SettingsLayoutPolicy.Restore | 构造只预定位到目标屏；OnLoad 一次性恢复物理矩形；Resize / LocationChanged 捕获，最小化不覆盖普通矩形 / 最大化状态；退出统一经 CurrentSettings 保存 |
| 设置 | SettingsDialog、MainForm.ShowSettings / ApplySettings / CurrentSettings / SaveSettingsOnExit、GlobalHotkeyDefinition、SettingsLayoutPolicy、AppSettingsStore | 入口在应用菜单、Ctrl+, 与命令面板；左导航三页；wrap 默认关闭；目录预设含“此电脑”；全局快捷键仅 Ctrl/Alt/Shift + A–Z/0–9/F1–F12，默认停用；切后台隐藏到托盘默认不勾选，快捷键页可滚动；重复宿主快捷键 / 已被占用时内联拒绝；22 操作可下拉重绑 / 留空禁用；保存成功才应用；正常退出保存最后普通 Bounds / 非最小化 Maximized，首次 OnLoad 完成缩放后恢复，后续显示不重置尺寸 |
| 正常退出 / 关闭 | MainForm.OnClosingWithSession → OnClosing、CloseSessionsForExit / CompleteExit / RequestExitByClosingSessions / CloseRestoringWindows / ClearRestoredSessionOnExit | 预保存普通目录；用户关闭（X / Alt+F4 / 应用菜单关闭）先对所有 Attached 会话发精确 WM_CLOSE，全部消失才退出，10 秒未关闭则取消退出并保留宿主；只有分离菜单的“恢复窗口并退出宿主”走 detach / restore；成功保留窗口后清除 LastSession，避免下次启动重复提示；都不 kill 进程 |
| 分离 / 事故恢复 | HostController.Detach / DetachAll、Program.RecoverBeforeStartup / ExecuteStartupRecovery / StartupRecoveryDetails、RecoveryJournal.TryClearAllMissing | 全失效只原子清日志、零窗口操作；存活 / 未知仍确认，失败阻止启动；主界面显式重开目录 |
| 打包 / 出包目录 | scripts/package.ps1 的 Invoke-Build / Copy-AppFiles / Write-BuildInfo / New-Zip、PackageTransaction.ps1 的 Assert-PackagePath / Test-PackageZip / Publish-PackageSet、package.bat | 唯一暂存目录 + 文件锁；程序集版本 / zip 完整性通过才事务发布 dev / release，失败回滚、保留历史；不生成 setup.exe；运行中宿主仍须明确 Y 确认 |

## 3. 重点链路与易踩坑

详见 [`arch/links.md`](arch/links.md)：浏览器式 UI 与新建延迟呈现、产品名称与兼容身份、打包与出包目录、响应性与隐藏宿主。

## 4. 接口契约
| 接口 | 契约 |
|---|---|
| ProductIdentity.GetStateDirectory / GetMutexName | 纯路径 / 名称计算；共享旧版存储与 per-user session-local 互斥；不创建 / 迁移目录 |
| IExplorerTabService.CreateAsync | source 可空，受管 HWND 快照；返回精确 WindowIdentity / Directory；Dispose 仅释放 COM |
| IExplorerDirectoryService.ReadDirectoryAsync / IsReading | 复制 identity、只读单飞 STA；取消及时但不提前放 gate；无新建 / 导航 fallback |
| IWindowCloser.RequestClose | 明确 session 身份，异步关闭单 HWND；成功仅表示消息入队，不保证窗口已关闭 |
| WindowSession.CloseRequestedUtc | 仅本次请求时间；不持久化为跨运行 HWND 状态，不代替恢复快照 |
| IWorkspaceLibraryStore / WorkspaceLibrary | 版本 1；100 收藏 / 50 组 / 每会话 1–100 标签；1 MB；验证、深复制、原子替换 |
| WorkspaceLibrary.SameSession | 比较目录 / 别名 / active index；忽略新生成 ID，用于避免周期重复写盘 |
| AppSettings | 版本 1 向后兼容缺字段：WrapTabs=false、MaximumTabRows=3、RememberSession=true、GlobalHotkey=null、HideToTrayOnGlobalHotkey=false（两者默认不写入 JSON）、22 默认 KeyBindings；Bounds / Maximized 保存最后普通布局 / 非最小化状态；首次 OnLoad 缩放后经 SettingsLayoutPolicy 工作区夹取恢复 |
| FolderTarget.ThisPc / Normalize / IsThisPc / Display | `shell:MyComputerFolder` 与 CLSID 规范化为 token；其它 shell 虚拟目录仍拒绝；Display 返回“此电脑”；Same 对 token 生效 |
| GlobalHotkeyDefinition.Normalize / TryParse / Conflicts | 仅 Ctrl/Alt/Shift + A–Z/0–9/F1–F12，至少一个修饰键；空值=停用；非法格式抛 ArgumentException；与宿主快捷键重复时拒绝 |
| EmbeddedInputPolicy.IsSelectAll / IsEditClass | 仅物理 Ctrl+A key-down、非注入、精确 Ctrl 修饰且活动宿主可用；类名包含 Edit / RichEdit 才补发 EM_SETSEL；不改变快捷键策略与原始按键消费 |
| GlobalHotkeyPolicy.Decide / WindowPlacementTracker.Capture | 前者纯决策 ShowHost / MinimizeHost / HideToTray；后者最小化时完全保留上次布局，最大化只改状态，普通状态只收有效非微型矩形 |
| GlobalHotkeyRegistration.Apply / Probe / Unregister | RegisterHotKey 绑定 HWND（MOD_NOREPEAT）；失败回滚旧组合；Probe 用独立 ID 试探占用；Dispose / HandleDestroyed 注销 |
| IExplorerCreationBackend.IsAt / ShellViewReference.CurrentFolderIs | 虚拟目标创建后用当前视图 PIDL 与目标 PIDL 的 `ILIsEqual` 精确匹配；文件系统目标继续用 LocationURL 比对 |
| HostShortcutPolicy.Handle | 物理 down/up 配对，repeat 不重触发，注入不消费；scope / create / switch / active 权限限制 |
| MainForm.managedMenuCleanups / closedManagedMenus / disposingUi | 临时与持久标签菜单归宿主所有；Closed 下一 UI turn 释放 scope，临时菜单另 Dispose；持久标签菜单随按钮移除或宿主销毁才 Dispose，不执行迟到命令 / 恢复可见性 |
| MainForm.ShowManagedMenuAtScreenPoint / CreateAppMenu / CreateAddWindowsMenu / AddExitMenuItems | 与锚点菜单共用模态 / 延后释放；关闭标签窗口与分离保留窗口明确区分；应用 / 托盘共用退出状态；加入候选仍按 Handle 去重并走身份校验，仅显示标题与动作 Tooltip |
| LibraryManagerDialog | 接收 WorkspaceLibrary 私有副本，Dirty 标记未提交编辑；保存或打开均返回 Result，打开同时返回 FavoriteToOpen / GroupToOpen；宿主先提交 Dirty 修改再排队打开，取消丢弃；弹窗不写盘、不操作 Explorer |
| TabManagementPolicy.CloseSuccessor | 关闭请求发送后 / 活动标签消失后优先左邻再右邻；跳过恢复中 / 已请求关闭 / 同轮消失；关闭后台标签返回当前活动 |
| TabStrip.SetTrailingControl / ShowPendingCreation / CompletePendingCreation / SetActivePending / MiddleClose / TooltipFor / TabButton.HitClose | 尾部动作控件不参与标签测宽 / 插入点，换行时与最后一个标签同排（优先微缩最后标签、必要时一起下移）；新建占位是唯一活动标签，完成 / 移除时清除活动占位；× 固定预留列、仅 Attached 可命中且不启动拖动；中键 / × 只触发关闭回调，由宿主复用精确 `WM_CLOSE`，不分离 / 不持久化；Tooltip 只读缓存目录 |
| TabStrip 滚动 / 布局 | 不使用 AutoScroll 与原生 WS_HSCROLL / WS_VSCROLL；自绘 4 逻辑 px 圆角浮条，横向仅单行溢出、纵向仅多行超过 MaximumTabRows，两轴不会同时出现；ApplyScroll 统一钳制，Reveal / 拖动自动滚动 / 滚轮共用；滚动条按下由标签栏捕获处理，不启动窗口拖动 |
| DialogStyle.Apply / ShapeChildren / DialogTitleBar | 仅作用于宿主自建 `ShellDialog`；无边框自定义标题栏、关闭按钮与拖动；窗口 region 与子控件裁剪共用同一 GraphicsPath（外 / 内圆角相差 1px），只裁掉贴近窗口四条边的角；`Form.BackColor=DialogBorder` 形成实色 1px 环，无描边 / 无 CS_DROPSHADOW；不接管系统对话框、不改变窗口身份 |
| WindowIdentity / NativeWindowApi.Matches / MatchQuerySucceeded | HWND + PID / TID / 进程启动 ticks / 映像 / 类名，不仅 HWND；查询失败时只有 HWND 确认消失才 Missing，否则 UNKNOWN 保留记录 |
| NativeWindowApi.IsExplorer | 仅 CabinetWClass / ExploreWClass，宿主映像限 `%WINDIR%\explorer.exe` 或 `%WINDIR%\System32\rundll32.exe`（ShellBrowserWindow 注册的 LocalServer32）；创建、加入、关闭、任务栏、事故恢复共用，不按标题 / PID / 枚举顺序猜测 |
| NativeWindowApi.ValidateAttach / AllowDpiMismatch | 嵌入前读取 host/target DPI context 与 `GetDpiForWindow`；context 不可读、反向 mismatch、或有效 DPI 不等时抛含双方 context/DPI 的错误；仅低 awareness → PerMonitor/PMv2 且有效 DPI 相等时允许并记录 `attach.dpi-mismatch`；Grouped 不检查 |
| NativeWindowApi.MatchThreadDpiToExplorer / PreferredThreadDpiContext | 仅在创建任何 WinForms HWND 前调用；只采纳 PerMonitorV2（其次 PerMonitor）候选，使用伪句柄 -4/-3；无 per-monitor 候选时保留 manifest 的 PerMonitorV2，绝不降级为 Unaware/System |
| NativeWindowApi.ChangeParent | 跨进程 SetParent 以实际 `GetParent` 复核，避免把成功调用留下的非零 last-error 误判为失败；不改变恢复日志与身份校验 |
| MainForm.CanPresentExplorer / HostController.Activate | 不可见 / 最小化 / 模态 / 退出状态不呈现 Explorer；visible=false 的激活与失败回滚均保持隐藏 |
| MainForm.MenuIdle / CanUseTab / CanCaptureWorkspace / CanOpenWorkspace | 菜单自身 modalDepth 不参与能力判断；Busy / 关闭 / 单飞目录 / 只读 / 容量按动作限制，延后执行再次验证；动态收藏 / 组 / 会话使用深复制目标，不从活库重取 |
| TabStrip.Render / ShowPendingCreation / CompletePendingCreation / RemovePendingCreation / SetCloseReading / TabButton.PendingSource | 按会话复用控件 / 菜单；占位来源引用控制右侧位置、来源消失退尾，禁用选择 / 关闭 / 菜单 / 拖动；成功原位绑定并清锚点；读取 / 请求关闭期间隐藏 ×；仅真实消失后移除 |
| MainForm.SetCreationControls / UpdateCancelAffordance | 取消按钮只反映新建或非静默 workspace 操作；显示延迟约 450 ms，结束立即隐藏；`workspaceOperationSilent` 仅由自动会话保存设置，`PollWindows` 不重启计时 |
| MainForm.ConfigureStartupRecovery / ShowSessionOffer | 仅本次成功静默清理的失效数量；不写 JSON；与 LastSession 目录数分开展示，单一可换行横条；点击前零创建，点击快照复制后排队再验能力，暂不打开不删库且本次不再邀请 |
| RecoveryJournal.TryClearAllMissing | 死 owner + 全部完整身份不匹配才原子写保留 owner 的空副本；不修改输入，不 Restore / Recover；未知 / 存活回人工，写失败阻止启动 |
| RecoveryJournal / AppSettingsStore / WorkspaceLibraryStore | 原子写；破坏性窗口操作前日志先行；坏原文件保留并禁用本次持久化 |

## 5. 构建与安全验证
Windows + .NET SDK + .NET Framework 4.8 Developer Pack，无第三方 NuGet。
入口：powershell -ExecutionPolicy Bypass -File scripts/build.ps1 -NativeSmoke -CrossProcessSmoke -TabUiSmoke -CreationUiSmoke -WorkspaceUiSmoke。
scripts/build.ps1 基础单元检查后自动运行 scripts/test-package.ps1 的 25 项隔离打包断言；完整 smoke 加 ResponsivenessTests。打包测试以独立 Guid fixture 与 fake 进程 / 构建做异常注入，绝不读取真实宿主或替换真实 dist。
测试不启动真实 Shell、不 Show 交互宿主、不注册全局钩子；原生 COM stub 只本机 PIDL 解析。--workspace-ui-smoke 包含工作空间 / 窗框 / 菜单一致性隐藏 UI 测试；设置 EXPLORERTABS_TEST_MENU_RENDER_DIRECTORY 可额外生成 100% / 125% / 150% 的 15 张离屏菜单与布局断言，不打开真实菜单；--chrome-ui-smoke 可单独运行。--render-ui / --render-tabs / --render-tabs-hover / --render-workspace / --render-picker / --render-settings / --render-settings-keys / --render-rename / --render-about 生成隐藏实际控件图像。
--cross-process-smoke 现在还启动 `--native-helper-unaware` 子进程：子进程在创建窗口前设为 DPI Unaware，主进程验证 context 读取、白名单判定、有效 DPI 相等时的跨 context SetParent / 布局 / Detach 恢复；有效 DPI 不同则验证拒绝路径。同进程跨 context 仍失败，不在覆盖范围。
0.6.2 发布包 dist/v0.6.2-release/ExplorerTabs.exe 原样保留；当前打包入口与 dist 规则见 §3.7；保留所有旧 dist 原文件名 / 内容，清理仅限已核验项目 bin / obj；最终验证数量以 CHANGELOG / HANDOFF 为准。
真实关闭、会话批量创建、新快捷键前台边界及多行实际 DPI 体验留用户在对话验收，不写新回归清单。
