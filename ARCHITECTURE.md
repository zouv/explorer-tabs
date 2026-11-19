# 代码链路图谱 · ExplorerTabs

## 0. 边界
源码根 D:/Git/zgithub/explorer-tabs；旧备份 F:/Developer/AI/vibe/agent_codex/ExplorerTabHost 只读；原 D:/Git/zgithub/explorer_tab_host 已整体重命名，不再作为工作副本。
C# / WinForms / net48 / x64，无第三方 NuGet。每个标签对应常驻真实 Explorer；切换只显隐 / 布局，不重新导航、注入或杀共享进程。
图谱用于定位，函数名优先于行号；入口失配先检索源码并订正本表。

## 0.5 任务作用域路由
> 先用本表确定范围；具体函数与约束查 [`arch/tasks.md`](arch/tasks.md)（§2 分册），重点链路细节查 [`arch/links.md`](arch/links.md)（§3 分册）。定位任务后只读对应分册，不整包通读 `arch/`。
| 任务 | 首读文件 | 不必读 |
|---|---|---|
| 新建 / 复制 / Shell ABI | MainForm.cs、TabStrip.cs、ExplorerCreation.cs、ShellExplorerBackend.cs、ShellInteropTests.cs、ResponsivenessTests.cs | 工作区库、UI 配色 |
| 菜单 / 弹窗生命周期 | MainForm.WorkspaceUi.cs、MainForm.Chrome.cs、MainForm.GlobalHotkey.cs、MainForm.Commands.cs、MainForm.cs、MenuDismissMonitor.cs、LibraryManagerDialog.cs、WorkspaceUiTests.cs、MenuConsistencyTests.cs | Shell ABI 与原生恢复 |
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
| src/ExplorerTabs.App/MainForm.Chrome.cs | 无独立标题栏、原生 thick frame（薄 DWM 边框 + 不可见缩放边距）、顶部边框并入客户区并用同色带上报缩放 / 最大化工作区、标签栏 / 工具栏空白拖动、窗口按钮、托管应用菜单与图标资源 |
| src/ExplorerTabs.App/ModernControls.cs | GDI+ 矢量图标、图标 / 文字按钮、菜单配色、SidebarButton / ThemedCheckBox / FieldHost / PresetButton、DialogStyle / DialogTitleBar 无边框弹窗、同一 GraphicsPath 生成的外 / 内圆角、实色 1px 边框环、欢迎页 ResizeRedraw、PendingCreationPanel 等待转圈；不依赖 SVG 运行库 |
| src/ExplorerTabs.App/AboutDialog.cs | 应用介绍：嵌入图标、程序集版本、作者 @zouv、简短说明与关闭按钮；沿用 ShellDialog 边框 / 圆角 |
| src/ExplorerTabs.App/MainForm.cs | 接管、激活、分离、创建、轮询、模态范围与 UI 状态同步；新建立即显示等待页并在失败 / 取消时恢复原活动窗口；在右侧新建只按锚点插入、使用默认目录而不复制来源；退出并保留窗口成功后清除已保存会话 |
| src/ExplorerTabs.App/MainForm.WorkspaceUi.cs | 工具栏 / 加入 / 分离 / 收藏 / 会话菜单、共享能力判断 / 退出项 / 动态标题；“加入全部窗口”批量加入、快照直接打开、管理入口；菜单所有权 / 延后释放模态 scope、失效原窗口 / 显式重开会话自适应横条与 QueueUi；恢复进度 / 等待重试 / 停止 / 结果横幅 |
| src/ExplorerTabs.App/LibraryManagerDialog.cs | “管理收藏与标签组”：收藏目录 / 标签组两页；左选标签组、右侧维护组内标签（添加目录、编辑目录、重命名别名、上移 / 下移 / 移除）并保持 1–100 与 ActiveIndex 合法；修改留在私有副本，保存或打开时由宿主统一校验持久化，取消丢弃 |
| src/ExplorerTabs.App/MenuDismissMonitor.cs | 托管菜单打开期间安装 WH_MOUSE_LL 只读取点击坐标；点击菜单 / 子菜单外（含嵌入 Explorer）关闭菜单，不吞事件；非可见 probe 菜单不安装，失败降级为标准关闭 |
| src/ExplorerTabs.App/MainForm.WorkspacePersistence.cs | 按标签数预算的有界串行目录捕获、完整会话保存、串行显式恢复（Shell 未就绪时可见等待与退避重试、可停止）、自动保存静默、退出独立 30 秒上限 |
| src/ExplorerTabs.App/MainForm.Commands.cs | 精确关闭、150ms 目录等待、75ms 指定目标确认、读取前立即左邻聚焦 / 消失后清理重开栈、标签搜索、命令面板与路由 |
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
| src/ExplorerTabs.Core/WorkspaceRestorePolicy.cs | 会话恢复失败的瞬时 / 永久分类、1 / 2 / 3 / 5 秒退避与可读原因；目录不可用专用异常（`ExplorerDirectoryUnavailableException`） |
| src/ExplorerTabs.Core/HostShortcuts.cs | 23 操作（Tab1–9 = Ctrl+1–9、LastTab = Ctrl+0）、默认与候选组合、旧配置补命令、重复验证、物理 down/up / 注入 / repeat 策略 |
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
| AGENTS.md、CLAUDE.md、ARCHITECTURE.md、ROADMAP.md、CHANGELOG.md、HANDOFF.md | 规则、总览图谱、计划、发布、进行中接力 |
| arch/tasks.md、arch/links.md | §2 任务→函数入口、§3 重点链路与易踩坑分册；由 §0.5 路由表指向 |
| README.md、check-docs.sh、.claude/skills/ | 使用入口、模板机械体检与显式文档技能 |

## 2. 任务 → 代码入口

已拆分为 [`arch/tasks.md`](arch/tasks.md)：按任务名查函数与约束。定位任务后只读该分册，不整包通读 `arch/`。

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
| AppSettings | 版本 1 向后兼容缺字段：WrapTabs=false、MaximumTabRows=3、RememberSession=true、GlobalHotkey=null、HideToTrayOnGlobalHotkey=false（两者默认不写入 JSON）、23 默认 KeyBindings（旧文件读取时按命令补 LastTab / Ctrl+0）；Bounds / Maximized 保存最后普通布局 / 非最小化状态；首次 OnLoad 缩放后经 SettingsLayoutPolicy 工作区夹取恢复 |
| FolderTarget.ThisPc / Normalize / IsThisPc / Display | `shell:MyComputerFolder` 与 CLSID 规范化为 token；其它 shell 虚拟目录仍拒绝；Display 返回“此电脑”；Same 对 token 生效 |
| GlobalHotkeyDefinition.Normalize / TryParse / Conflicts | 仅 Ctrl/Alt/Shift + A–Z/0–9/F1–F12，至少一个修饰键；空值=停用；非法格式抛 ArgumentException；与宿主快捷键重复时拒绝 |
| EmbeddedInputPolicy.IsSelectAll / IsEditClass | 仅物理 Ctrl+A key-down、非注入、精确 Ctrl 修饰且活动宿主可用；类名包含 Edit / RichEdit 才补发 EM_SETSEL；不改变快捷键策略与原始按键消费 |
| GlobalHotkeyPolicy.Decide / WindowPlacementTracker.Capture | 前者纯决策 ShowHost / MinimizeHost / HideToTray；模态弹窗打开时仍允许 ShowHost（宿主在后台）并激活宿主拥有的弹窗，但不最小化 / 隐藏：宿主最小化会隐藏 owned 弹窗并使 WinForms 提前结束 ShowDialog（实测 DialogResult=Cancel），仅隐藏宿主则弹窗留在桌面；后者最小化时完全保留上次布局，最大化只改状态，普通状态只收有效非微型矩形 |
| GlobalHotkeyRegistration.Apply / Probe / Unregister | RegisterHotKey 绑定 HWND（MOD_NOREPEAT）；失败回滚旧组合；Probe 用独立 ID 试探占用；Dispose / HandleDestroyed 注销 |
| IExplorerCreationBackend.IsAt / ShellViewReference.CurrentFolderIs | 虚拟目标创建后用当前视图 PIDL 与目标 PIDL 的 `ILIsEqual` 精确匹配；文件系统目标继续用 LocationURL 比对 |
| HostShortcutPolicy.Handle | 物理 down/up 配对，repeat 不重触发，注入不消费；scope / create / switch / active 权限限制 |
| MainForm.managedMenuCleanups / closedManagedMenus / disposingUi | 临时与持久标签菜单归宿主所有；Closed 下一 UI turn 释放 scope，临时菜单另 Dispose；持久标签菜单随按钮移除或宿主销毁才 Dispose，不执行迟到命令 / 恢复可见性 |
| MainForm.ShowManagedMenuAtScreenPoint / CreateAppMenu / CreateAddWindowsMenu / AddExitMenuItems | 与锚点菜单共用模态 / 延后释放；关闭标签窗口与分离保留窗口明确区分；应用 / 托盘共用退出状态；加入候选仍按 Handle 去重并走身份校验，仅显示标题与动作 Tooltip |
| LibraryManagerDialog | 接收 WorkspaceLibrary 私有副本，Dirty 标记未提交编辑；标签组页右侧编辑组内标签（目录 / 别名 / 顺序 / 移除）并同步 ActiveIndex，始终保留 1–100 个；保存或打开均返回 Result，打开同时返回 FavoriteToOpen / GroupToOpen；宿主先提交 Dirty 修改再排队打开，取消丢弃；弹窗不写盘、不操作 Explorer |
| MenuDismissMonitor.Register / Unregister / IsInsideMenuTree | 仅可见托管菜单注册；WH_MOUSE_LL 只读取左 / 右键按下坐标，不做系统改动、不吞输入；菜单及可见子菜单内的点击放行，其余点击排队关闭对应菜单；钩子安装失败只记录并降级；Dispose 兜底卸载 |
| HostShortcutCatalog.Upgrade / LastTab | 读取旧 settings.json 时按命令补齐默认绑定（如 Ctrl+0），保留已有自定义 / 禁用项，再执行数量与重复校验；Ctrl+1–9 严格对应第 1–9 个标签，LastTab 才跳最后一个 |
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
| WorkspaceRestorePolicy.IsRetryable / RetryDelay / Describe | 创建阶段异常由 `ExplorerCreationTransientException` 标记为可重试；未包装的 Shell 激活类 COM HRESULT（E_FAIL、CO_E_SERVER_EXEC_FAILURE、REGDB_E_CLASSNOTREG、RPC_E_*、RPC_S_* 等）与单项 15 秒超时也可重试；目录不可用、身份变化不重试；延迟 1/2/3/5 秒封顶；描述区分“Shell 尚未就绪”“Shell 调用失败”“文件夹不存在或无法访问” |
| ExplorerDirectoryUnavailableException | 创建前目录校验失败专用，继承 InvalidOperationException 并携带 Directory；恢复策略不重试，调用方逐项报告后继续 |
| MainForm.OpenWorkspaceAsync / RestoreRetryDelay | 恢复期间 `restoringWorkspace` 独占横幅与 `workspaceOperation`；`RestoreRetryDelay` 仅供测试注入短退避；停止按钮与工具栏取消共用同一 CancellationTokenSource；等待 Shell 单飞 gate 释放时不并发创建 |
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
