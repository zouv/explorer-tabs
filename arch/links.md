# 重点链路与易踩坑（分册）

> 由 ARCHITECTURE.md §3 拆出；路由入口见 ARCHITECTURE §0.5。

## 3. 重点链路与易踩坑
- 0.4.1 Navigate2 使用完整原生 dual vtable、PIDL 的 VT_ARRAY|VT_UI1 与 optional VT_EMPTY。原生 stub 与用户真机基线均验证；不改回晚绑定 Type.Missing。
- 新窗口来自自己创建的 ShellBrowserWindow 引用；禁止运行 explorer.exe 后猜最新 HWND，禁止 Quit 或杀共享进程。
- ShellBrowserWindow 通常由 explorer.exe 托管；开机后 Shell 类工厂尚未就绪时，COM 会改用注册的 LocalServer32 宿主 `rundll32.exe shell32.dll,SHCreateLocalServerRunDll`。`NativeWindowApi.IsExplorer` 接受这两种 Windows 自带宿主，但仍要求 CabinetWClass / ExploreWClass 且 HWND/PID/TID/启动时间/映像精确一致；不按标题、最新窗口或枚举顺序猜测。
- 跨进程 `SetParent` 在 DPI awareness 不同时（Win10 1703+）会由 Windows 强制把子窗口 awareness 重置为宿主一侧，不是无害的虚拟化；同进程跨 context 则直接失败。因此嵌入白名单仅放行：Explorer 为 Unaware / UnawareGdiScaled / System、宿主为 PerMonitor / PerMonitorV2、且双方 `GetDpiForWindow` 非 0 且相等（开机 rundll32 宿主 + 100% DPI 的现场）。其余 mismatch 与任一 context 读取失败仍拒绝，并在 host.log 记 `attach.dpi-mismatch ... allowed=False`。`MatchThreadDpiToExplorer` 只采纳 PerMonitorV2（其次 PerMonitor）候选，绝不因首个 rundll32 Unaware 窗口把宿主 UI 降级；高 DPI 下同场景仍提示改用顶层分组模式。
- 目录按 QueryService / QueryActiveShellView / GetWindow 的自己视图匹配；有视图时否决冲突的自动化 HWND。不用公共宿主根、标题、PID 或枚举顺序回退。PIDL 优先，精确匹配的 LocationURL 仅作视图不可用回退。
- 窗口关闭与分离不同：close 不 Restore、不先删除标签；请求拒绝仍受管。PruneLost 日志失败时可重试但不得恢复已消失目标。未知身份查询错误不当作消失。
- recovery.json 保存活窗口原结构；workspace.json 仅普通目录 / 顺序 / 别名 / active index。后者不含 HWND、选中项、滚动或浏览历史，不能替代事故恢复。
- 启动 `TryClearAllMissing` 对死 owner 的全部记录仅查询完整身份：全不匹配才原子写空副本，永不 Restore / Recover；有匹配 / UNKNOWN 则仍人工确认，写失败不继续启动。线程 / 类名查询返回 0 但 HWND 仍在也属于 UNKNOWN。静默清理成功才传 `ConfigureStartupRecovery`，横条分别显示原窗口数量与保存目录数量，用户点击深复制快照后才重开；无快照不猜目录、关闭记住会话仅引导菜单、暂不打开本次不再弹。
- 会话捕获是有界串行的 best-effort 时间点集合，不冻结 Explorer 自身导航。任何读取失败 / 标签结构变化都不覆盖旧库，不把部分成功伪装成完整会话。
- 目录读取与创建各自单飞。取消任务可立即完成，但卡住 COM 的 gate 保留到工作线程实际退出；不 abort、不无限增线程。结果与 gate 释放间允许短有界等待。
- ContextMenuStrip.Closed 尚在 WinForms SetVisibleCore / ModalMenuFilter 中，禁止同步 Dispose。临时菜单 / 模态范围延后释放，取消 Opening 不依赖 Closed；菜单点击也不得同步重建标签或分离，统一 QueueUi / QueueMenuAction 后执行。
- 异步菜单先结束 native mouse message 再排队。Close / 收藏捕获点击时精确 session；关闭读目录期间 per-session 去重，退出 / dispose 后不发迟到关闭。模态交互时停钩子，分组窗不覆盖菜单。
- 标签按实际字体 / 标题 + 图标 padding 测宽，默认 110–280 逻辑 px；多行按真实宽度逐行分配（不再依赖 FlowBreak），仅超过行上限才出现纵向条。字号 / DPI / 别名变化重新测量；单行保持自适应宽与横向滚动，重绘保留 X / Y 滚动位置。标签栏关闭原生 AutoScroll / WS_HSCROLL / WS_VSCROLL：横向条只由单行溢出触发、纵向条只由多行超过上限触发，两轴不会互相反馈而同时出现；滚动条为 4 逻辑 px 自绘圆角浮条（悬停变色、支持滚轮 / 拖动 / 单行滚轮横移），横向条画在标签下方预留的 4px 内、不增加标签栏高度。尾部新建按钮始终跟随最后一个标签：行满时优先把最后标签微缩到最小宽度以上，仍不够则把最后标签一起移到下一行，绝不让 `+` 独自换行。
- 快捷键有限候选组合，保留 Explorer 的 F2 与文件操作语义。钩子只判断 / 排队；不 COM、写盘、查进程或重挂。按键已消费的 release 即使范围变化仍配对吞掉。
- 退出先预保存普通目录；默认用户关闭（X / Alt+F4 / 菜单关闭）随后关闭所有会话窗口，失败时保留宿主；显式恢复退出才 detach / restore。分离失败仍阻止退出。0.2.1 黑块 / 缩略图恢复顺序已获用户确认，不因本轮功能改动。

### 3.5 浏览器式 UI 与新建延迟呈现
- 独立 ShellBrowserWindow 激活 → 排除已有 / 受管 HWND → 完整身份核验 → Visible=false → PIDL 导航 → 再核验并抑制显示 → 目录到位 → 交付 CreatedExplorer lease。
- 真实后端不再主动 Show 独立新窗口；Shell 激活瞬间 / 内部导航是否仍露脸留真机验证，不靠全局 EnumWindows / hook 追捕窗口。
- using(result) 覆盖单次及会话创建；STA 交付前绑定取消，COM / 后端 Dispose 失败与未交付结果均释放 lease。注册与取消并发幂等，Registration.Dispose 不持有 gate。
- Attach 的新建隐藏窗快照在日志落盘前设独立可见恢复意图；Activate 成功后 MarkAttached。失败恢复只精确 native identity + 顶层无父 / 非 WS_CHILD，不 Quit / kill / 携 COM 跨 STA。
- 保留原生 thick frame，但把顶部边框并入客户区：`WM_NCCALCSIZE` 默认计算出客户区后，把 `rgrc[0].Top` 拉回窗口顶边；左右 / 下边框仍是非客户区，DWM 画薄边框、宽 5–8px 的不可见边距负责缩放命中，子控件无法覆盖。顶部视觉带由 `SyncTopResizeBand` 设为 3 逻辑 px 的 `Padding.Top`（`BackColor` 与标签栏同为 `HostTheme.Chrome`），`WM_NCHITTEST` 在该带返回 `HTTOP` / 左右角；`TabStrip` 与其 `TabButton` 再把各自顶部 4px 转发为 `HTTOP`，顶部总命中区仍约 7px。最大化时该带收为 0。`WM_NCACTIVATE` 以 lParam=-1 交 `DefWindowProc` 并返回，避免 Windows 10 失焦时把非激活边框画进顶部带；`WM_GETMINMAXINFO` 按原生边框外扩 `MaxSize` / `MaxPosition`，使最大化客户区正好等于工作区（不遮任务栏、无顶部条）。不要再改回 WM_NCCALCSIZE 返回 0 + 大面积 Padding 模拟边框。
- 标签栏空白双击最大化 / 还原：`ChromeMouseDown` 在 `e.Clicks == 2` 时直接 `ToggleMaximize`，其余左键才发 `HTCAPTION` 进入移动循环；不要依赖 `MouseDoubleClick`（首次按下已进入移动循环，双击事件不一定送达）。
- 无标签的欢迎页 `WelcomePanel` 由 content `Dock=Fill` 填充，`ResizeRedraw` 确保窗口缩放后完整重绘；按钮在 `OnLayout` 按 `ClientSize` 重新居中，图标 / 标题 / 按钮随尺寸移动。
- 嵌入 FrameChanged 先于 Layout；只有精确 Explorer 且 client 与 window 一致，直接全宽 UIRibbonCommandBarDock 含 UIRibbonCommandBar、顶部偏移在 DPI 缩放后的 80px 内，才按实测 inset 把根窗口移到 viewport.Y-inset 并增高 inset；底部保持不变。未知布局返回 0；不改子控件、普通 / 分组窗口或恢复快照。嵌入日志记录 inset / viewport，诊断导出追加宿主区域 / DPI / 滚动状态。
- 无独立标题栏：CreateParams 移除 WS_CAPTION、保留 WS_SYSMENU / WS_MINIMIZEBOX / WS_MAXIMIZEBOX / WS_THICKFRAME；品牌及三个窗口按钮固定第一行，多行只增长标签区域；空白拖动 / 双击最大化。
- 单行标签栏与工具栏各约 36 px（100%）；仅溢出才增加滚动高度；工具栏在自身底部画 1px 低饱和度线，不增加空行。分组图标按钮有 Tooltip / AccessibleName / hover / focus；tooltip 读取实际快捷键设置。
- 工具栏分左右两组：左侧为“加入窗口… / 分离窗口…”相邻的窗口组，再是收藏与标签组 / 会话与恢复；右侧只保留打开对话框的搜索（设置移入应用菜单）；两组及中缝都画同一条底部分界线。加入图标为箭头进入窗口，分离图标为箭头离开窗口，设置使用实心齿轮（非放射线太阳形）。分离按钮菜单统一为“关闭当前窗口 / 分离当前窗口 / 分离所有窗口 / 关闭应用并分离所有窗口”，最后一项仍走 detach / restore。
- 工具栏取消按钮（X，提示“取消操作”）由 `MainForm.UpdateCancelAffordance` 控制：新建/复制、打开工作区、手动保存、搜索目录读取等用户可感知操作持续约 450 ms 后才显示；自动会话保存通过 `workspaceOperationSilent` 静默，不显示取消按钮。`PollWindows` 每 1.5 s 调用 `SetCreationControls` 时不会重启延迟计时；操作提前结束则计时停止，快速操作不闪。
- 加入已有窗口先弹托管菜单：直接列出 `DiscoverExplorers` 中未被接管的窗口，文案为 `加入【标题】窗口`（Tooltip 说明点击行为并附 PID / HWND），点击单项即按精确身份加入；无候选显示禁用占位；底部分隔项“打开“加入”管理界面”再打开原 `WindowPicker`。发现异常只记日志并按空列表显示，不猜测窗口。
- 左上角品牌图标与标签栏空白右键共用应用菜单：设置… / 退出并保留窗口 / 关闭所有标签并退出 / 关于…（单独分组置底）；退出项由 AddExitMenuItems 与托盘共用文案 / Tooltip / Enabled，关闭项右侧显示 Alt+F4。X / Alt+F4 / 关闭所有标签并退出仍请求精确 WM_CLOSE，全部消失后退出；10 秒未关闭则保留宿主，不 kill。退出并保留窗口走 detach / restore，恢复未完成也阻止退出；分离菜单只保留分离当前 / 所有标签和退出并保留窗口。菜单共用既有模态 / 延后释放契约；Alt+Space 仍由原生系统菜单处理。
- 收藏与标签组菜单：收藏当前目录… / 收藏其他目录… / 保存当前标签组…，随后是带数量的收藏 / 标签组分区，条目直接点击打开为新标签 / 追加组，不再嵌套 CRUD 子菜单；底部“管理收藏与标签组…”。管理弹窗分“收藏目录 / 标签组”两页，私有副本保存或打开时经 CommitLibrary 校验提交，取消丢弃；返回打开目标不直接操作 Explorer。库不可写时禁修改入口，但允许打开已有条目并明确显示说明。
- 标签悬停时在右侧固定预留列显示 ×（16 逻辑 px），只有 Attached 标签可点；按在 × 上不会启动拖动或选中，释放时复用 `CloseTabAsync` 的精确 `WM_CLOSE`。中键、Ctrl+W、悬停 × 共用关闭路径。
- 活动标签消失后由 `TabManagementPolicy.CloseSuccessor` 优先选择左邻（没有左邻取右邻，跳过恢复中 / 已请求关闭 / 同轮消失的标签）；关闭后台标签保持当前活动。Tooltip 只读 `directoryCache` 显示目录，未缓存显示“未缓存”，不再显示拖动排序提示。
- 加入窗口菜单只显示候选标题，底部“选择窗口…”；MenuText 去换行 / 转义 &、最多 32 个文本元素（含省略号），Tooltip 留完整标题及动作说明、不带 PID / HWND。会话菜单分保存当前快照、恢复上次会话（追加）、重新打开关闭的标签、清除上次会话记录；说明快照覆盖 / 标签组长期保存 / 重开最多 20 项 / 自动保存可能重建快照。动态动作捕获深复制，当前库变化不改变已选目标。
- 新建标签按钮由 `TabStrip.SetTrailingControl` 挂在最后一个标签之后；`Render` 按 WindowSession 引用复用按钮与标签菜单，仅删除不存在的会话按钮；活动 / 标题刷新不打断 capture，结构变化才取消指针。尾部按钮保持末位且不参与标签排序；换行时优先微缩最后标签，达到最小宽后与 `+` 一起下移。
- 标签中键与 Ctrl+W 都调用 `CloseTabAsync`（精确 `WM_CLOSE`），不再分离；标签右键菜单删除左右移动，关闭 / 复制 / 重命名通过 `ShortcutKeyDisplayString` 展示当前绑定（设置关闭快捷键时不展示）。
- 命令面板 / 搜索、设置、重命名、加入窗口弹窗、关于页使用 `ShellDialog` + `DialogTitleBar`：无边框、无继承图标、无原生厚灰边，标题栏可拖动、右侧关闭；`DialogStyle` 把 `Form.BackColor` 设为 `DialogBorder` 形成 1px 边框环，窗口 region 与标题栏 / 内容根的裁剪共用同一 `GraphicsPath`（内圆角比外圆角小 1px）；不再绘制描边、不再使用 `CS_DROPSHADOW`。直边为完整 1px，圆角是像素级台阶——这是为了避开 GDI+ 描边被二进制 window region 切掉外侧而发虚的问题；不应用到系统文件夹选择框。
- 设置弹窗使用左导航（常规 / 标签栏 / 快捷键）+ 右侧页面 + 底部保存 / 取消：`SidebarButton` 负责选中条，`ThemedCheckBox` / `FieldHost` / `PresetButton` 统一控件风格；新建标签目录预设包含用户目录、“此电脑”、文档、下载与系统目录选择器；快捷键页用 AutoScroll + AutoSize 页面，保证新增托盘选项与底部恢复默认项在小屏 / 高 DPI 下可达。
- 全局快捷键通过 `RegisterHotKey` 绑定 HWND（`MOD_NOREPEAT`）：Shown 后注册，ApplySettings 失败回滚；WM_HOTKEY 排队前与执行时均拦截模态 / 退出。`GlobalHotkeyPolicy.Decide` + `TabKeyboardHook.IsTargetForeground` 区分呼出与切后台：其他程序前台 / 隐藏 / 最小化时呼出；宿主或精确活动标签前台时默认最小化，勾选 `HideToTrayOnGlobalHotkey` 才 Hide + 托盘。呼出用 RestoreHostWindowState 保留最大化；不切换 ShowInTaskbar / FormBorderStyle，避免重建 HWND。
- 窗口布局：构造先按旧 Bounds 预定位；`OnLoad` 在首次自动缩放后再次按工作区夹取恢复物理 Bounds（仅一次）。`WindowPlacementTracker` 捕获最后普通矩形与非最小化状态，排除图标 / 过小矩形；当前设置与两条退出链路统一保存该快照，不用最小化后的 RestoreBounds / Maximized 判定。
- “此电脑”是唯一支持的虚拟目标（token `shell:MyComputerFolder`）：`FolderTarget.Normalize` 接受并规范化，创建时跳过 `DirectoryExists`，由 `IExplorerCreationBackend.IsAt` 用 `SHParseDisplayName` + `IPersistFolder2.GetCurFolder` + `ILIsEqual` 做 PIDL 精确匹配；`ShellViewReference.ReadDirectory` 对当前 PIDL 为此电脑时返回 token，使会话 / 标签组 / 收藏可保存与恢复。
- 菜单共享 MenuIdle / CanUseTab / CanReadDirectory / CanDetachTabs / CanCaptureWorkspace / CanOpenWorkspace：空态、忙碌、关闭 / 恢复态、目录单飞、只读和容量限制提前禁用冲突操作；MenuIdle 刻意不含 modalDepth（菜单自身占用 scope），动作仍 QueueUi / QueueMenuAction 延后并再次检查。命令面板沿用同一能力、术语与实际绑定，增加管理入口。
- 工具栏旧 mode / shortcuts 控件只保留状态，不再占布局；模式及快捷键开关入口在设置。临时菜单 Renderer 不改变 Closed 延后释放的安全契约。

### 3.6 产品名称与兼容身份
- 当前 solution / 项目 / namespace / assembly / exe / manifest / 嵌入图标统一 ExplorerTabs；Form 标题版本读取程序集，不维护第二份版本号。
- ProductIdentity.Name 是品牌名；LegacyIdentity 是历史存储与互斥契约。`%LOCALAPPDATA%\ExplorerTabHost` 中 settings / workspace / recovery / logs 保持原位，不迁移 / 覆盖；`Local\ExplorerTabHost.<user>` 同旧版互斥，改名不能开启第二个接管宿主。
- JSON DTO 与字段 / enum 数值不改；Fixtures/v0.6.1 是旧程序集生成的合成数据，读取与重序列化逐字节核对；读取事故日志不会执行恢复。
- 默认测试的 mutex 探针仅启动测试 runner，自建 Guid 用户身份、CreateNoWindow，不读取实际用户的状态或窗口。历史 dist / 验证记录与用户清单原样保留，发布只复制新构建的明确文件名单。

### 3.7 打包与出包目录
- 当前正式版本 0.13.0（2026-11-13），`v0.13.0` 标签固定指向代码提交 `fe74c7b`；正式双 zip + SHA256SUMS 已发布并经远端 digest / 下载核验，详见 CHANGELOG 0.13.0。构建阶段 1138 项安全检查通过；正式包原始二进制另在独立 runner 复跑 1113 项可执行检查通过（`.runtime/verification-packaged-v0.13.0.txt`）。发布结果文档另提交，不移动标签或因文档收尾重打已发布包。
- `package.bat` → `scripts/package.ps1`；版本只从 `Directory.Build.props` 的 `<Version>` 读取，不在脚本里写死。
- 根目录 `build.bat` / `test.bat` / `verify.bat` / `clean.bat` / `run.bat` / `dev.bat` / `ci.bat` 只做参数透传与前置检查，构建 / 测试逻辑仍集中在 `scripts/build.ps1`；`verify.bat` 用 `-TreatWarningsAsErrors` 传入警告即错误并追加 `check-docs.sh`，`clean.bat` 不 taskkill，`run.bat` 仅由用户显式启动；`dev.bat` 先构建、再运行 `.runtime/build/ExplorerTabs-dev/ExplorerTabs.exe`，并把 `-CloseRunningHost` 传给 build.ps1：检测到已有 ExplorerTabs 时列出 PID / 路径并询问，输入 Y 才先正常关闭、12 秒超时后强制结束，然后继续构建 / 启动，其它输入以退出码 2 取消；非交互输入直接拒绝。
- 每次先构建到唯一 `.runtime/staging/package-<Guid>`；公共 `package.lock` 用独占文件句柄保护构建 / 发布且不 unlink。EXE / Core 程序集版本一致，两个 zip 的条目数 / 长度 / SHA256 均验证后，才将旧 dev / release 移入本次备份并逐个发布；任一步失败逆序回滚，回滚失败保留 stage 明示路径。成功仅清本次已校验 stage；历史 `dist/vX-release` 与其它暂存目录不动。
- dev zip 根含 `ExplorerTabs_<版本>_x64-unpack-debug/`；release zip 从暂存 release/unpack 内部打包，发布后落在 `dist/release/unpack`。release 不含 PDB，dev 含 PDB / build-info / logs；release 复制 README.md 及引用图标 / 截图，保证离线可读。
- 不生成 setup.exe：项目不引入第三方 NuGet 或 Inno Setup，release 以免安装解压包分发。
- 打包前检测运行中的 ExplorerTabs：列出 PID / 路径并询问，默认取消；只有明确输入 `Y` 才先 `CloseMainWindow`（12 秒）再对剩余宿主强制结束。非交互输入直接拒绝并保留 dist；不直接操作用户 Explorer。zip 用 .NET `ZipFile`，无需外部压缩器。

### 3.8 响应性与隐藏宿主
- CreateTabAsync 开始即 ShowPendingCreation；Guid + CTS 绑定一次请求，不伪造 HWND、不进入 Sessions / recovery / workspace。复制占位以 PendingSource 引用紧跟来源、刷新跟随移动、来源移除退尾；普通新建尾插。保留当前内容；成功 Attach / Activate / MarkAttached 后由 AttachCreatedCore 按来源当前索引 MoveTab，再原位绑定按钮；取消 / 失败删占位，迟到窗口仍独立。日志分段统计 Shell ready、durable attach、activation / UI ready，不代表 Explorer 实际像素完成。
- CloseTabAsync 立即显示关闭中，目录读取最多等 150ms；超时取消并观察 / 丢弃迟到结果，不把过期缓存加入重开栈。RequestClose 仍先持久化日志再精确 WM_CLOSE；请求后切邻居但保留 closing 标签。75ms closeMonitor 只 PruneTargets；原 1500ms PollWindows 兜底；真实消失与日志提交成功前不得移除。模态 / 交互期间延后，6 秒未关保留并可重试。
- CanPresentExplorer 同时约束 Visible / minimized / modalDepth / closing / disposingUi；创建完成、EnsureActive、Resize、Activated、模态退出与热键呼出遵守策略。Activate(visible:false) 更新活动模型但不 Layout / Show，回滚也不复活隐藏窗口。
- WorkspaceCapturePolicy：预算 5000 + min(count,100) × 2600ms，自动最少 7 秒、手动最少 30 秒、最高 265 秒；退出另限最多 30 秒。完整性 / 顺序 / 活动校验失败或超时保留旧库，不承诺退出能读完 100 个慢目录。
- 150ms / 75ms 是等待预算 / 检查间隔，不是实机总耗时保证；原生调用、Shell 创建准备与日志 Flush(true) 保留，不用预创建窗口池换取表面速度。

