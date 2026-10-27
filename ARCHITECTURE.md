# 代码链路图谱 · ExplorerTabs

## 0. 边界
源码根 D:/Git/zgithub/explorer-tabs；旧备份 F:/Developer/AI/vibe/agent_codex/ExplorerTabHost 只读；原 D:/Git/zgithub/explorer_tab_host 已整体重命名，不再作为工作副本。
C# / WinForms / net48 / x64，无第三方 NuGet。每个标签对应常驻真实 Explorer；切换只显隐 / 布局，不重新导航、注入或杀共享进程。
图谱用于定位，函数名优先于行号；入口失配先检索源码并订正本表。

## 0.5 任务作用域路由
| 任务 | 首读文件 | 不必读 |
|---|---|---|
| 新建 / 复制 / Shell ABI | MainForm.cs、ExplorerCreation.cs、ShellExplorerBackend.cs、ShellInteropTests.cs | 工作区库、UI 配色 |
| 菜单 / 弹窗生命周期 | MainForm.WorkspaceUi.cs、MainForm.cs、LibraryManagerDialog.cs、Program.cs、WorkspaceUiTests.cs | Shell ABI 与原生恢复 |
| 会话 / 常用目录 / 标签组 | MainForm.WorkspacePersistence.cs、MainForm.WorkspaceUi.cs、WorkspaceLibrary.cs、WorkspaceUiTests.cs | 原生恢复与 ABI |
| 关闭 / 重开目录 | MainForm.Commands.cs、HostController.cs、WindowClosing.cs、WorkspaceTests.cs | TaskbarService、创建 ABI |
| 搜索 / 命令 / 快捷键 | MainForm.Commands.cs、QuickPickerDialog.cs、TabKeyboardHook.cs、HostShortcuts.cs | 原生恢复、文档模板 |
| 浏览器式窗框 / 图标工具栏 | MainForm.Chrome.cs、ModernControls.cs、MainForm.WorkspaceUi.cs、ChromeUiTests.cs | Shell ABI、恢复库 |
| 灰框 / 空标题区 | MainForm.Chrome.cs、NativeWindowApi.cs 的 MeasureEmbeddedTitleInset / Layout、ChromeUiTests.cs、EmbeddedChromeTests.cs | Shell ABI、恢复呈现内部链路 |
| 多行 / 排序 / 设置 | TabStrip.cs、SettingsDialog.cs、AppSettings.cs、WorkspaceUiTests.cs | ShellExplorerBackend |
| 分离黑块 / 任务栏 / 事故恢复 | HostController.cs、NativeWindowApi.cs、RestorePresentation.cs、RecoveryJournal.cs | 收藏库、快捷键设置 UI |
| 产品 / 工程改名 / 旧版兼容 | ProductIdentity.cs、Program.cs、BrandingTests.cs、工程文件、scripts/build.ps1 | Shell ABI、原生恢复执行 |
| 发布 / 自动验证 / 打包 | scripts/build.ps1、scripts/package.ps1、build.bat / test.bat / verify.bat / clean.bat / run.bat / ci.bat、package.bat、manager.sh、Directory.Build.props、tests/ExplorerTabs.Tests | 用户历史反馈、界面实现细节 |

## 1. 文件职责
| 文件 | 职责 |
|---|---|
| src/ExplorerTabs.Core/ProductIdentity.cs | 产品名 ExplorerTabs；集中旧数据目录与跨版本单实例身份，不执行迁移 |
| tests/ExplorerTabs.Tests/BrandingTests.cs、Fixtures/v0.6.1/ | 旧程序集生成的合成 JSON，字节级重序列化、改名元数据 / 资源与独立测试用户互斥 |
| src/ExplorerTabs.App/Program.cs | 单实例 / DPI / 事故恢复，独立读取 settings.json 与 workspace.json；坏库禁用持久化 |
| src/ExplorerTabs.App/MainForm.Chrome.cs | 无独立标题栏、系统样式 / 最大化工作区 / resize hit-test / 非客户区绘制抑制、标签空白拖动、窗口按钮、托管应用菜单与图标资源 |
| src/ExplorerTabs.App/ModernControls.cs | GDI+ 矢量图标、图标 / 文字按钮、菜单配色、DialogStyle / DialogTitleBar 无边框弹窗、1px 圆角边框与小圆角；不依赖 SVG 运行库 |
| src/ExplorerTabs.App/AboutDialog.cs | 应用介绍：嵌入图标、程序集版本、简短说明与关闭按钮；沿用 ShellDialog 边框 / 圆角 |
| src/ExplorerTabs.App/MainForm.cs | 接管、激活、分离、创建、轮询、模态范围与 UI 状态同步 |
| src/ExplorerTabs.App/MainForm.WorkspaceUi.cs | 工具栏左右分组、加入窗口候选菜单、分离菜单、临时菜单所有权 / 延后释放、启动恢复横条、收藏与组 CRUD、收藏管理弹窗入口、QueueUi |
| src/ExplorerTabs.App/LibraryManagerDialog.cs | “收藏”管理：目录管理 / 组管理两页；打开、收藏目录、重命名、移除、删除；修改留在私有副本，保存后由宿主统一校验持久化 |
| src/ExplorerTabs.App/MainForm.WorkspacePersistence.cs | 有界串行目录捕获、完整会话保存、串行显式恢复、自动保存、退出预保存 |
| src/ExplorerTabs.App/MainForm.Commands.cs | 精确关闭 / 完成检测 / 重开栈、关闭后左邻聚焦、标签搜索、命令面板与路由 |
| src/ExplorerTabs.App/QuickPickerDialog.cs | 双行搜索列表，AND 分词、键盘导航、无匹配与 Esc |
| src/ExplorerTabs.App/TabStrip.cs | 本地标签点击 / 拖动 / 悬停关闭按钮 / 单行滚动 / 多行与纵向溢出；缓存目录 Tooltip；尾部挂新建按钮但不参与标签测宽 / 排序；不接管文件拖放 |
| src/ExplorerTabs.App/SettingsDialog.cs、RenameTabDialog.cs | 两页设置、快捷键组合下拉与重复校验，标签别名 |
| src/ExplorerTabs.App/TabKeyboardHook.cs | 专用消息线程；前台 / 菜单判定，复制不可变 scope，回调只排队 |
| src/ExplorerTabs.Core/HostController.cs、Models.cs | 身份与恢复日志先行的标签生命周期；关闭请求保留日志直到消失 |
| src/ExplorerTabs.Core/ExplorerCreation.cs | 独立创建与只读目录 STA 单飞；CreatedExplorer 呈现恢复 lease / 取消交接 |
| src/ExplorerTabs.App/ShellExplorerBackend.cs | COM STA 后端、精确 Shell view / PIDL 匹配、原生 dual ABI；仅核验本次新建 HWND 后 Visible=false |
| src/ExplorerTabs.Core/WorkspaceLibrary.cs | 会话 / 收藏 / 标签组模型、深复制、验证、原子库存储、搜索和行数策略 |
| src/ExplorerTabs.Core/HostShortcuts.cs | 22 操作、默认与候选组合、重复验证、物理 down/up / 注入 / repeat 策略 |
| src/ExplorerTabs.Core/WindowClosing.cs | identity 校验后 PostMessage(WM_CLOSE)，不 detach / Quit / kill |
| src/ExplorerTabs.Core/FolderTarget.cs、AppSettings.cs | 普通目录验证；设置默认值兼容、深复制与原子写盘 |
| src/ExplorerTabs.Core/NativeWindowApi.cs、TaskbarService.cs、RestorePresentation.cs、RecoveryJournal.cs | Win32 / Shell 操作、恢复结构 / 显示顺序、只读呈现采样、事故日志 |
| tests/ExplorerTabs.Tests/DeferredCreationTests.cs、ChromeUiTests.cs | fake 创建取消 / 失败 / lease 并发与隐藏真实窗框 / 紧凑布局 / 图标 tooltip；不运行真实 Explorer |
| tests/ExplorerTabs.Tests/EmbeddedChromeTests.cs | 自注册 Ribbon 类与隐藏自建 HWND 测标题区测量 / 根窗口裁切 / 未知布局回退 / 非 Explorer 恢复；不操作真实 Shell |
| assets/icons/、assets/screenshots/ | 三版原创 SVG、对比预览、暂用 A 的多尺寸 ICO / 嵌入资源；README 引用的离屏单行 / 多行截图（不含真实 Explorer 内容） |
| tests/ExplorerTabs.Tests/WorkspaceTests.cs、WorkspaceUiTests.cs | 库 / 快捷键 / close / STA 策略；隐藏 UI 会话、迟到结果、多行跨行拖动、设置 / 搜索 |
| tests/ExplorerTabs.Tests/Program.cs、FeatureTests.cs、CreationUiTests.cs、ShellInteropTests.cs | 基线断言、fake Shell、自建透明窗口、隐藏 UI 与原生 ABI stub |
| scripts/build.ps1、scripts/package.ps1、build.bat / test.bat / verify.bat / clean.bat / run.bat / ci.bat、package.bat、manager.sh、Directory.Build.props | net48 / x64 / 版本单一来源、受保护开发快照、可选安全 smoke；.bat 仅做参数透传与前置检查；package 产出 dev zip + release unpack/zip，不杀宿主 |
| dist/、artifacts/ | Git 忽略的发布与验证产物：`dist/dev` 版本化 debug zip、`dist/release/unpack` + release zip；历史 `dist/vX-release` 原样保留；artifacts/ 存离屏图像与验证日志 |
| docs/ | 使用 / 历史反馈；不改用户已填清单 |
| AGENTS.md、CLAUDE.md、ARCHITECTURE.md、ROADMAP.md、CHANGELOG.md、HANDOFF.md | 规则、图谱、计划、发布、进行中接力 |
| README.md、check-docs.sh、.claude/skills/ | 使用入口、模板机械体检与显式文档技能 |

## 2. 任务 → 代码入口
| 任务 | 文件 / 函数 | 约束 |
|---|---|---|
| 产品 / 旧版身份 | ProductIdentity.GetStateDirectory / GetMutexName、Program.Main、BrandingUnitTests | 继续旧 LocalAppData 目录与 per-user Local mutex；仅测试合成身份，不争用实际宿主 |
| 灰框 / 空栏 | MainForm.WndProc / InvalidateChrome、NativeWindowApi.MeasureEmbeddedTitleInset / EmbeddedViewportBounds / Layout | WM_NCPAINT 抑制 / NCACTIVATE lParam=-1；仅精确 Explorer 嵌入根窗口裁切，不改 Ribbon / 恢复快照 |
| 新建 / 复制 | MainForm.CreateTabAsync / QueueCreateFromMenu、ExplorerCreationWorkflow.Run / Verify | 15 秒；排除已有与受管 HWND，核验后延迟呈现；结果 using / Dispose，成功 MarkAttached；迟到不接管 |
| 窗框 / 图标工具栏 | MainForm.InitializeChrome / WndProc / ChromeHitTest、InitializeWorkspaceUi / UpdateToolbarHints、TabStrip.PreferredChromeHeight | 无 WS_CAPTION 但保留系统 / 缩放样式；右上角 X / Alt+F4 关闭所有会话窗口后退出；100% 单行栏各 36 px |
| 工具栏 / 标签栏布局 | MainForm.InitializeWorkspaceUi、MainForm.PaintToolbarDivider、TabStrip.SetTrailingControl / UpdateRows | 加入窗口与分离窗口相邻在左组；右组只保留搜索（设置移入应用菜单）；新建按钮紧跟最后一个标签且不参与测宽 / 拖动排序 |
| 弹窗样式 / 标题栏 | DialogStyle.Apply / DrawBorder、DialogTitleBar、QuickPickerDialog / SettingsDialog / RenameTabDialog / WindowPicker / AboutDialog / LibraryManagerDialog | 无边框自定义标题栏（标题 / 关闭 / 拖动）替代原生厚边框；1px 圆角边框 + Win10 窗口 region 小圆角；不改系统文件 / 文件夹选择框 |
| 应用菜单 / 关于 | MainForm.CreateAppMenu / ShowAppMenu / ShowAbout、AboutDialog | 图标 / 标签栏空白右键统一托管菜单；设置、关闭（分离所有窗口）、关闭、关于（单独分组置底）；关闭项展示 Alt+F4 并带“关闭所有会话窗口”Tooltip，分离项带“分离所有会话窗口”Tooltip；不再提供移动 / 大小 / 最大最小化 |
| 加入已有窗口 | MainForm.ShowAddWindowsMenu / CreateAddWindowsMenu / OpenWindowPicker / AttachSelectedWindows、WindowPicker | 先列出未接管的 DiscoverExplorers 候选，文案为“加入【标题】窗口”并带点击说明 / PID / HWND Tooltip；底部“打开“加入”管理界面”再开 WindowPicker；仍走 IsExplorer 与精确身份校验 |
| 标签右键菜单 / 中键 / 悬停关闭 | MainForm.CreateTabMenu / ShowShortcut、TabStrip.MiddleClose / TabButton.HitClose、MainForm.CloseTabAsync | 省略移动菜单；快捷键仅用 ShortcutKeyDisplayString 展示；中键、Ctrl+W、悬停 × 都走精确 WM_CLOSE，不分离 |
| 新建呈现所有权 | BrowserReference.DeferPresentation、CreatedExplorer.BindCancellation / MarkAttached / Dispose、HostController.Attach | 恢复回调不携 COM 跨线程；新建隐藏窗日志记录独立可见恢复；失败 / 取消仅恢复精确身份的未嵌入窗 |
| 源目录 / 当前目录 | ShellExplorerBackend.ReadDirectory、StaExplorerTabService.ReadDirectoryAsync、MainForm.ReadTabDirectoryAsync | 自己的 Shell view HWND 子孙关系，前后 identity 核验；只读 STA，不导航 |
| 精确关闭 | MainForm.CloseTabAsync、HostController.RequestClose、NativeWindowCloser.RequestClose | 每 session 去重；先持久化；只 WM_CLOSE；不早删日志 |
| 关闭完成 / 重开 | MainForm.CompleteClosedTabs / ReopenTabAsync、TabManagementPolicy.CloseSuccessor、HostController.PruneLost | 消失并清日志才入最多 20 个目录重开栈；活动标签消失后优先左邻（无左邻取右邻），关闭后台标签不切换；6 秒拒绝保留标签并可重试 |
| 捕获 / 保存 | MainForm.CaptureWorkspaceAsync / SaveSessionAsync / SaveGroupAsync | 每项 2.5 秒；任何读取失败 / 排序 / 别名 / 活动变化保留旧库；内容相同不重写 |
| 会话 / 组 / 收藏打开 | MainForm.OpenWorkspaceAsync | 显式串行创建、逐项 15 秒、可取消、成功保留、最后激活保存索引；总量不超过 100 |
| 菜单生命周期 | MainForm.ShowManagedMenu / ShowManagedMenuAtScreenPoint / RegisterManagedMenu / QueueManagedMenuCleanup / ReleaseClosedManagedMenus / DisposeManagedMenus / QueueMenuAction | Closed 只排队；取消 / 显示失败也清理；命令先释放已关闭菜单；Dispose 不恢复分组窗口 |
| 收藏 / 组管理 | MainForm.CreateLibraryMenu / ShowLibraryMenu / ShowLibraryManager / AddFavoriteAsync / AddBookmark / RenameLibraryItemAsync / CommitLibrary、LibraryManagerDialog | 持久化成功才换内存；删除记录不关闭窗口；管理弹窗修改只留在副本，保存或打开时才提交；打开收藏 / 组只返回待打开目标 |
| 搜索 / 命令 | MainForm.ShowTabPickerAsync / ShowCommandPicker / ExecuteCommandAsync、QuickPickerDialog.Filter | 列表打开前有界读取当前目录；失败可显示旧缓存标签，不猜路径 |
| 键盘 | TabKeyboardHook.OnKey / Eligible / UpdateScope、HostShortcutPolicy.Handle | 不消费其他程序、菜单 / 独立对话框、注入 / Win 组合；UI 执行时再验范围 |
| 多行 / 拖动 / 悬停关闭 | TabStrip.ConfigureLayout / MeasureTabWidth / AssignWrappedRows / UpdateRows / Render / Reveal / UpdateInsertion / TooltipFor、TabButton.HitClose | FlowBreak 按各标题实际宽度换行，最多 2–8 可见行；为 × 固定预留 20 逻辑 px；保留滚动；跨行不重挂窗口 |
| 设置 | SettingsDialog、MainForm.ShowSettings / ApplySettings、AppSettingsStore | 入口在应用菜单、Ctrl+, 与命令面板；工具栏不再有设置按钮；wrap 默认关闭；22 操作可下拉重绑 / 留空禁用，重复拒绝；保存成功才应用 |
| 正常退出 / 关闭 | MainForm.OnClosingWithSession → OnClosing、CloseSessionsForExit / CompleteExit / RequestExitByClosingSessions / CloseRestoringWindows | 预保存普通目录；用户关闭（X / Alt+F4 / 应用菜单关闭）先对所有 Attached 会话发精确 WM_CLOSE，全部消失才退出，10 秒未关闭则取消退出并保留宿主；只有分离菜单的“恢复窗口并退出宿主”走 detach / restore；都不 kill 进程 |
| 分离 / 事故恢复 | HostController.Detach / DetachAll、Program.RecoverBeforeStartup | 保持隐藏恢复结构，再显示 / 重绘 / 任务栏注册；事故记录优先于目录重开 |
| 打包 / 出包目录 | scripts/package.ps1 的 Invoke-Build / Copy-AppFiles / Write-BuildInfo / New-Zip、package.bat、manager.sh package | 版本读 Directory.Build.props；只清 dist/dev、dist/release、.runtime/staging；dev zip 有顶层目录、release zip 无；不生成 setup.exe；不 taskkill 宿主 |

## 3. 重点链路与易踩坑
- 0.4.1 Navigate2 使用完整原生 dual vtable、PIDL 的 VT_ARRAY|VT_UI1 与 optional VT_EMPTY。原生 stub 与用户真机基线均验证；不改回晚绑定 Type.Missing。
- 新窗口来自自己创建的 ShellBrowserWindow 引用；禁止运行 explorer.exe 后猜最新 HWND，禁止 Quit 或杀共享进程。
- ShellBrowserWindow 通常由 explorer.exe 托管；开机后 Shell 类工厂尚未就绪时，COM 会改用注册的 LocalServer32 宿主 `rundll32.exe shell32.dll,SHCreateLocalServerRunDll`。`NativeWindowApi.IsExplorer` 接受这两种 Windows 自带宿主，但仍要求 CabinetWClass / ExploreWClass 且 HWND/PID/TID/启动时间/映像精确一致；不按标题、最新窗口或枚举顺序猜测。
- 目录按 QueryService / QueryActiveShellView / GetWindow 的自己视图匹配；有视图时否决冲突的自动化 HWND。不用公共宿主根、标题、PID 或枚举顺序回退。PIDL 优先，精确匹配的 LocationURL 仅作视图不可用回退。
- 窗口关闭与分离不同：close 不 Restore、不先删除标签；请求拒绝仍受管。PruneLost 日志失败时可重试但不得恢复已消失目标。未知身份查询错误不当作消失。
- recovery.json 保存活窗口原结构；workspace.json 仅普通目录 / 顺序 / 别名 / active index。后者不含 HWND、选中项、滚动或浏览历史，不能替代事故恢复。
- 会话捕获是有界串行的 best-effort 时间点集合，不冻结 Explorer 自身导航。任何读取失败 / 标签结构变化都不覆盖旧库，不把部分成功伪装成完整会话。
- 目录读取与创建各自单飞。取消任务可立即完成，但卡住 COM 的 gate 保留到工作线程实际退出；不 abort、不无限增线程。结果与 gate 释放间允许短有界等待。
- ContextMenuStrip.Closed 尚在 WinForms SetVisibleCore / ModalMenuFilter 中，禁止同步 Dispose。临时菜单 / 模态范围延后释放，取消 Opening 不依赖 Closed；菜单点击也不得同步重建标签或分离，统一 QueueUi / QueueMenuAction 后执行。
- 异步菜单先结束 native mouse message 再排队。Close / 收藏捕获点击时精确 session；关闭读目录期间 per-session 去重，退出 / dispose 后不发迟到关闭。模态交互时停钩子，分组窗不覆盖菜单。
- 标签按实际字体 / 标题 + 图标 padding 测宽，默认 110–280 逻辑 px；多行累计真实宽度、FlowBreak 换行，仅超过行上限才扣纵滚条。字号 / DPI / 别名变化重新测量；单行仍保持自适应宽与横滚，重绘保留 X / Y viewport。必须先调整 viewport 高度再发布 scroll extent，Render 的 ResumeLayout(false) 避免短名变长名时留下幽灵纵滚条。
- 快捷键有限候选组合，保留 Explorer 的 F2 与文件操作语义。钩子只判断 / 排队；不 COM、写盘、查进程或重挂。按键已消费的 release 即使范围变化仍配对吞掉。
- 退出先预保存普通目录；默认用户关闭（X / Alt+F4 / 菜单关闭）随后关闭所有会话窗口，失败时保留宿主；显式恢复退出才 detach / restore。分离失败仍阻止退出。0.2.1 黑块 / 缩略图恢复顺序已获用户确认，不因本轮功能改动。

### 3.5 浏览器式 UI 与新建延迟呈现
- 独立 ShellBrowserWindow 激活 → 排除已有 / 受管 HWND → 完整身份核验 → Visible=false → PIDL 导航 → 再核验并抑制显示 → 目录到位 → 交付 CreatedExplorer lease。
- 真实后端不再主动 Show 独立新窗口；Shell 激活瞬间 / 内部导航是否仍露脸留真机验证，不靠全局 EnumWindows / hook 追捕窗口。
- using(result) 覆盖单次及会话创建；STA 交付前绑定取消，COM / 后端 Dispose 失败与未交付结果均释放 lease。注册与取消并发幂等，Registration.Dispose 不持有 gate。
- Attach 的新建隐藏窗快照在日志落盘前设独立可见恢复意图；Activate 成功后 MarkAttached。失败恢复只精确 native identity + 顶层无父 / 非 WS_CHILD，不 Quit / kill / 携 COM 跨 STA。
- WM_NCPAINT 不交默认绘制，WM_NCACTIVATE 以 lParam=-1 交 DefWindowProc 保留激活状态而不画厚边；只刷新宿主 chrome / toolbar，不强制重绘整个 Explorer。
- 嵌入 FrameChanged 先于 Layout；只有精确 Explorer 且 client 与 window 一致，直接全宽 UIRibbonCommandBarDock 含 UIRibbonCommandBar、顶部偏移在 DPI 缩放后的 80px 内，才按实测 inset 把根窗口移到 viewport.Y-inset 并增高 inset；底部保持不变。未知布局返回 0；不改子控件、普通 / 分组窗口或恢复快照。嵌入日志记录 inset / viewport，诊断导出追加宿主区域 / DPI / 滚动状态。
- WM_NCCALCSIZE 去独立标题框；窗框保留系统菜单 / 边缘缩放 / 工作区最大化。品牌及三个窗口按钮固定第一行，多行只增长标签区域；空白拖动 / 双击最大化。
- 单行标签栏与工具栏各约 36 px（100%）；仅溢出才增加滚动高度；工具栏在自身底部画 1px 低饱和度线，不增加空行。分组图标按钮有 Tooltip / AccessibleName / hover / focus；tooltip 读取实际快捷键设置。
- 工具栏分左右两组：左侧为“加入窗口… / 分离窗口…”相邻的窗口组，再是收藏与标签组 / 会话与恢复；右侧只保留打开对话框的搜索（设置移入应用菜单）；两组及中缝都画同一条底部分界线。加入图标为箭头进入窗口，分离图标为箭头离开窗口，设置使用实心齿轮（非放射线太阳形）。分离按钮菜单统一为“关闭当前窗口 / 分离当前窗口 / 分离所有窗口 / 关闭应用并分离所有窗口”，最后一项仍走 detach / restore。
- 加入已有窗口先弹托管菜单：直接列出 `DiscoverExplorers` 中未被接管的窗口，文案为 `加入【标题】窗口`（Tooltip 说明点击行为并附 PID / HWND），点击单项即按精确身份加入；无候选显示禁用占位；底部分隔项“打开“加入”管理界面”再打开原 `WindowPicker`。发现异常只记日志并按空列表显示，不猜测窗口。
- 左上角品牌图标与标签栏空白右键统一打开托管应用菜单：设置 / 关闭（分离所有窗口） / 关闭 / 关于（单独分组置底），移除移动 / 大小 / 最大最小化；两个关闭项均带 Tooltip，关闭项显示 Alt+F4，快捷键统一由 `ShortcutKeyDisplayString` 画在右侧快捷键列。右上角 X、Alt+F4、菜单“关闭”都请求精确 WM_CLOSE 所有 Attached 会话，全部消失后退出；10 秒未关闭则取消退出并保留宿主，不 kill、不恢复。菜单“关闭（分离所有窗口）”与分离菜单的“关闭应用并分离所有窗口”仍走 detach / restore。`ShowManagedMenuAtScreenPoint` 与锚点菜单共用同一生命周期 / 模态作用域；Alt+Space 仍由原生系统菜单处理。
- 收藏菜单：收藏当前目录… / 收藏其他目录… / 保存当前标签组，随后是常用目录与标签组列表，底部分隔项“打开“收藏”管理界面”。管理弹窗分“目录管理 / 组管理”两页，可打开、收藏目录、重命名、移除常用目录，也可打开、重命名、删除标签组；所有修改只写入私有副本，点“保存”后由宿主 `CommitLibrary` 校验写盘，打开条目只返回待打开目标，不直接操作 Explorer。
- 标签悬停时在右侧固定预留列显示 ×（16 逻辑 px），只有 Attached 标签可点；按在 × 上不会启动拖动或选中，释放时复用 `CloseTabAsync` 的精确 `WM_CLOSE`。中键、Ctrl+W、悬停 × 共用关闭路径。
- 活动标签消失后由 `TabManagementPolicy.CloseSuccessor` 优先选择左邻（没有左邻取右邻，跳过恢复中 / 已请求关闭 / 同轮消失的标签）；关闭后台标签保持当前活动。Tooltip 只读 `directoryCache` 显示目录，未缓存显示“未缓存”，不再显示拖动排序提示。
- 新建标签按钮由 `TabStrip.SetTrailingControl` 挂在最后一个标签之后，`Render` 只重建 `TabButton`，尾部按钮保持在子控件末位；测宽、换行、拖动插入点只统计 `TabButton`，尾部按钮尺寸仅随 DPI 调整。
- 标签中键与 Ctrl+W 都调用 `CloseTabAsync`（精确 `WM_CLOSE`），不再分离；标签右键菜单删除左右移动，关闭 / 复制 / 重命名通过 `ShortcutKeyDisplayString` 展示当前绑定（设置关闭快捷键时不展示）。
- 命令面板 / 搜索、设置、重命名、加入窗口弹窗、关于页使用 `ShellDialog` + `DialogTitleBar`：无边框、无继承图标、无原生厚灰边，标题栏可拖动、右侧关闭；`DialogStyle` 在句柄创建 / 显示 / 尺寸变化时用 `CreateRoundRectRgn` + `SetWindowRgn` 加 12 逻辑 px 小圆角（Win10 无原生圆角），并保留 `CS_DROPSHADOW`；表单预留 1px Padding，`ShellDialog.OnPaint` 画同半径的 1px 边框；不应用到系统文件夹选择框。
- 工具栏旧 mode / shortcuts 控件只保留状态，不再占布局；模式及快捷键开关入口在设置。临时菜单 Renderer 不改变 Closed 延后释放的安全契约。

### 3.6 产品名称与兼容身份
- 当前 solution / 项目 / namespace / assembly / exe / manifest / 嵌入图标统一 ExplorerTabs；Form 标题版本读取程序集，不维护第二份版本号。
- ProductIdentity.Name 是品牌名；LegacyIdentity 是历史存储与互斥契约。`%LOCALAPPDATA%\ExplorerTabHost` 中 settings / workspace / recovery / logs 保持原位，不迁移 / 覆盖；`Local\ExplorerTabHost.<user>` 同旧版互斥，改名不能开启第二个接管宿主。
- JSON DTO 与字段 / enum 数值不改；Fixtures/v0.6.1 是旧程序集生成的合成数据，读取与重序列化逐字节核对；读取事故日志不会执行恢复。
- 默认测试的 mutex 探针仅启动测试 runner，自建 Guid 用户身份、CreateNoWindow，不读取实际用户的状态或窗口。历史 dist / 验证记录与用户清单原样保留，发布只复制新构建的明确文件名单。

### 3.7 打包与出包目录
- `package.bat` → `scripts/package.ps1`；版本只从 `Directory.Build.props` 的 `<Version>` 读取，不在脚本里写死。
- 根目录 `build.bat` / `test.bat` / `verify.bat` / `clean.bat` / `run.bat` / `ci.bat` 只做参数透传与前置检查，构建 / 测试逻辑仍集中在 `scripts/build.ps1`；`verify.bat` 用 `-TreatWarningsAsErrors` 传入警告即错误并追加 `check-docs.sh`，`clean.bat` 不 taskkill，`run.bat` 仅由用户显式启动。
- 只清理 `dist/dev`、`dist/release`、`.runtime/staging`；历史 `dist/vX-release` 是用户入口与历史包，禁止整目录删除。暂存目录仅在两个 zip 都成功后删除。
- dev zip 根含 `ExplorerTabs_<版本>_x64-unpack-debug/`；release zip 从 `dist/release/unpack` 内部打包，解压后 exe 直接在根下。release 不含 PDB，dev 含 PDB / build-info / logs；release 另复制 README.md 及其引用的图标 / 截图，保证离线可读。
- 不生成 setup.exe：项目不引入第三方 NuGet 或 Inno Setup，release 以免安装解压包分发。
- 打包前只检测运行中的 ExplorerTabs：目标位于 `dist/` 下则报错退出；绝不 taskkill、不关闭用户 Explorer。zip 用 .NET `ZipFile` 生成，不依赖外部 tar。

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
| AppSettings | 版本 1 向后兼容缺字段：WrapTabs=false、MaximumTabRows=3、RememberSession=true、22 默认 KeyBindings |
| HostShortcutPolicy.Handle | 物理 down/up 配对，repeat 不重触发，注入不消费；scope / create / switch / active 权限限制 |
| MainForm.managedMenuCleanups / closedManagedMenus / disposingUi | 临时菜单归宿主所有；Closed 标记待清理，下一 UI turn 释放；宿主销毁清剩余且不执行迟到命令 / 恢复可见性 |
| MainForm.ShowManagedMenuAtScreenPoint / CreateAppMenu / CreateAddWindowsMenu | 与锚点菜单共用模态 / 延后释放；应用菜单为设置 / 关闭（分离所有窗口） / 关闭 / 关于；加入菜单候选仍按精确 Handle 去重并走 AttachSelectedWindows 身份校验，候选项文案带加入前缀与 Tooltip |
| LibraryManagerDialog | 接收 `WorkspaceLibrary` 私有副本；目录 / 组编辑只在副本内，`Dirty` 标记未保存修改；保存返回 `Result`，打开只返回 `FavoriteToOpen` / `GroupToOpen`，不写盘、不操作 Explorer |
| TabManagementPolicy.CloseSuccessor | 活动标签消失后优先左邻、再右邻；跳过恢复中 / 已请求关闭 / 同轮消失；关闭后台标签返回当前活动 |
| TabStrip.SetTrailingControl / MiddleClose / TooltipFor / TabButton.HitClose | 尾部动作控件不参与标签测宽 / 插入点；× 固定预留列、仅 Attached 可命中且不启动拖动；中键 / × 只触发关闭回调，由宿主复用精确 `WM_CLOSE`，不分离 / 不持久化；Tooltip 只读缓存目录 |
| DialogStyle.Apply / DrawBorder / DialogTitleBar | 仅作用于宿主自建 `ShellDialog`；无边框自定义标题栏、关闭按钮与拖动；表单 1px Padding + 同半径 1px 边框；按 DPI 设置窗口圆角 region；不接管系统对话框、不改变窗口身份 |
| WindowIdentity / NativeWindowApi.Matches | HWND + PID / TID / 进程启动 ticks / 映像 / 类名，不仅 HWND |
| NativeWindowApi.IsExplorer | 仅 CabinetWClass / ExploreWClass，宿主映像限 `%WINDIR%\explorer.exe` 或 `%WINDIR%\System32\rundll32.exe`（ShellBrowserWindow 注册的 LocalServer32）；创建、加入、关闭、任务栏、事故恢复共用，不按标题 / PID / 枚举顺序猜测 |
| RecoveryJournal / AppSettingsStore / WorkspaceLibraryStore | 原子写；破坏性窗口操作前日志先行；坏原文件保留并禁用本次持久化 |

## 5. 构建与安全验证
Windows + .NET SDK + .NET Framework 4.8 Developer Pack，无第三方 NuGet。
入口：powershell -ExecutionPolicy Bypass -File scripts/build.ps1 -NativeSmoke -CrossProcessSmoke -TabUiSmoke -CreationUiSmoke -WorkspaceUiSmoke。
测试不启动真实 Shell、不 Show 交互宿主、不注册全局钩子；原生 COM stub 只本机 PIDL 解析。--workspace-ui-smoke 包含工作空间与窗框隐藏 UI 测试；--chrome-ui-smoke 可单独运行。--render-ui / --render-tabs / --render-tabs-hover / --render-workspace / --render-picker / --render-settings / --render-settings-keys / --render-rename / --render-about 生成隐藏实际控件图像。
0.6.2 发布包 dist/v0.6.2-release/ExplorerTabs.exe 原样保留；当前打包入口与 dist 规则见 §3.7；保留所有旧 dist 原文件名 / 内容，清理仅限已核验项目 bin / obj；最终验证数量以 CHANGELOG / HANDOFF 为准。
真实关闭、会话批量创建、新快捷键前台边界及多行实际 DPI 体验留用户在对话验收，不写新回归清单。
