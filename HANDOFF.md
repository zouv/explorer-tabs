# 会话接力 · ExplorerTabs

状态日期：2026-11-11；0.12.0 发布进行中（版本已提升，待提交 / 打包 / 上传）。四项体验修复已实现并通过 1134 项断言，待用户实机复验；真实 Shell、实际系统重启与交互体验不自动操作。
源码根 D:/Git/zgithub/explorer-tabs；分支 main；origin 关联 https://github.com/zouv/explorer-tabs；当前正式版本 0.11.1，Release https://github.com/zouv/explorer-tabs/releases/tag/v0.11.1；历史 Release 保留。
最后验证：2026-11-11 完整 `build.ps1 -TreatWarningsAsErrors -NativeSmoke -CrossProcessSmoke -TabUiSmoke -CreationUiSmoke -WorkspaceUiSmoke` 通过 1134 项安全断言（核心 536、原生 63、跨进程 17、标签 UI 38、创建 UI 43、工作区 412、隔离打包 25），零警告 / 零错误（`.runtime/verification-fixes.txt`）。未启动真实交互宿主、未操作用户 Explorer；本轮四项修复与真实 Shell 键鼠交互待用户复验，历史 0.11.1 发布验证见 CHANGELOG。

正式入口 `dist/release/unpack/ExplorerTabs.exe`；Release / Debug zip 与 `dist/release/SHA256SUMS.txt` 均已发布。标签 `v0.11.1` 固定指向代码提交 `cc455de`，发布结果文档另提交，不移动标签或因文档收尾重打包。完整发布记录见 CHANGELOG 0.11.1 / ROADMAP P44–P46。

## 待用户反馈（直接在对话询问，不写人工回归文档）
- 2026-11-11 四项修复待实机复验（先退出旧宿主，再运行新的 `dev.bat` 或重新打包）：① “退出并保留窗口”后重开不应再出现“上次会话已保存 N 个目录”横条；② “加入窗口”菜单应看到“加入全部窗口”，点击后加入当前全部未接管 Explorer；③ 点 `+` / Ctrl+T 应立即切到带转圈等待页并高亮占位标签，成功切到新窗口，失败 / 取消恢复原标签；④ 托管 Explorer 内 F2 重命名按 Ctrl+A 应全选文件名（含扩展名）。真实 Shell 键鼠行为未自动操作。
- 2026-11-07 本轮验证入口为正式 0.11.1 `dist/release/unpack/ExplorerTabs.exe`。先正常退出旧宿主，再手动启动：右键复制中间 / 非活动标签应在该标签右侧占位并完成；系统重启后旧窗口全失效应进入主界面提示手动重开保存目录，无快照不承诺重开。真实 Shell / 实际系统重启尚未操作，完整说明见 CHANGELOG 0.11.1 / ROADMAP P44–P45。
- 2026-11-01 正式 0.11.0 包包含前三轮改动。正常退出旧宿主后手动运行正式入口；确认应用 / 托盘退出两项的窗口保留结果、收藏 / 组直接打开与管理入口、会话追加恢复、只读 / 忙碌禁用，以及长名称在高 DPI 下的显示；离屏检查不等于真实弹出与键鼠验收。

- 2026-11-01 热键 / 尺寸：默认切后台应最小化并保留任务栏，勾选托盘选项后才隐藏；其他程序前台时按键应呼出，最大化切后台 / 呼出应仍最大化，设置捕获期间不切换窗口。用 X / Alt+F4 / 退出并保留窗口分别在普通 / 最大化 / 最小化 / 托盘状态关闭后重开，确认尺寸不变；真实注册 / 前台 / 托盘尚未自动操作。
- 2026-11-01 本轮请先正常退出旧宿主，再手动运行正式 0.11.0 包（见上方入口）。新建 / 复制应立即出占位、成功后原位变真实标签；失败 / 取消应清占位且旧标签可用；Explorer 自身准备仍可能耗时。
- 2026-11-01 关闭请比较点击反馈与移除延迟，确认活动标签请求后切左邻 / 右邻；若 Explorer 有阻挡对话框，6 秒后仍保留标签、可再次关闭或分离。不应强制杀进程；慢目录读取超时不提供可能错误的重开路径。150ms / 75ms 不代表实机总耗时保证。
- 2026-11-01 隐藏 / 最小化与菜单打开期间完成新建、外部关闭活动标签，不应复活 Explorer；呼出后活动内容应正常。多标签慢目录保存失败 / 退出 30 秒超时应保留旧库；菜单反复打开、改名 / 排序后动作应仍对应正确标签。
- 2026-10-29 dev.bat 运行中宿主确认待手动复验：先启动一次 ExplorerTabs，再运行 `dev.bat`。应列出 PID / 路径并询问；输入 N 或直接回车应取消、不关闭进程，输入 Y 应先尝试正常关闭（12 秒超时强制结束）后继续构建并启动。重定向 / 非交互输入会直接拒绝。
- 2026-10-29 弹窗 chrome 待真机复验：退出旧实例后运行 `dev.bat` 或 `.runtime/build/ExplorerTabs-verify/ExplorerTabs.exe`。所有自建弹窗的四条直边应为完全一致的 1px 灰蓝边框，无描边发虚、无右下投影；圆角是 1px 像素台阶（已确认的取舍）。请重点看左上 / 右下圆角是否连续、四边是否同粗细。
- 2026-10-29 设置面板 / 目录预设待复验：左导航三页（常规 / 标签栏 / 快捷键）对齐与控件风格；点击“我的电脑 / 用户目录 / 文档 / 下载 / 自定义…”应更新路径并在保存后用于新建标签。请分别在 100% / 125% / 150% DPI 下检查对齐和截断。
- 2026-10-29 此电脑待真机验证：在设置中选择“我的电脑”并新建标签，应打开“此电脑”；随后保存会话、重启恢复、收藏此目录并打开，均应保持虚拟位置。若失败请提供 host.log（关注 `Creation source directory provider=IPersistFolder2/ThisPC` 及创建匹配失败信息）。
- 全局快捷键旧版 0.10.0 的无条件隐藏行为已被当前未发布的默认最小化 / 可选托盘方案替代（见本轮验收项）；仍请复验组合键冲突的内联提示与分组模式。
- 2026-10-29 Wrap Tabs / 滚动条 / `+` 待真机复验：先退出旧宿主再运行 `dev.bat`。开启 Wrap 后标签栏不应再同时出现横 / 纵原生滚动条；单行溢出时只在标签栏底部显示 4px 细条，多行超过行数上限时只在右侧显示，可悬停、拖动、滚轮滚动；标签行将满时 `+` 应紧贴最后一个标签（最后一个标签可能略微收窄或与其一起换行），不再独自占一行。请确认观感与滚动手感。
- 2026-10-28 窗口 / 标签栏体验待复验：退出旧宿主后运行 `dev.bat`（构建并启动 `.runtime/build/ExplorerTabs-dev/ExplorerTabs.exe`）。顶部视觉空白已缩至 3px，标签栏 / 标签顶部 4px 仍可缩放（顶部总命中约 7px），侧边 / 底部 / 四角不变；标签栏空白双击切换最大化 / 还原；无标签时调整窗口大小，欢迎页图标 / 标题 / 按钮应重新居中。请分别确认观感、顶部缩放手感与双击行为。检查时启动的探针实例已正常关闭，探针改动的 settings 窗口状态已恢复为 Bounds (0,0,800,607)、Maximized=false。
- 窗口尺寸记忆本轮已覆盖首次 DPI 缩放与最小化退出边界，新增真实退出准备 / 重开和最大化隐藏回归；强制结束进程不保证保存（仅正常退出落盘），显示器 / DPI 变化时仍受工作区与最小尺寸限制。
- 2026-10-28 打包确认流程待复验：`package.bat` 在检测到 ExplorerTabs 运行时列出 PID / 路径并询问；输入 Y 才先尝试正常关闭（12 秒超时强制结束）并继续打包，其它输入取消；非交互输入直接拒绝。请在实际运行宿主时试一次 Y / 非 Y 两条路径。dev.bat 的 LF 行尾问题已改为 CRLF，验证时启动的开发实例已正常关闭。
- 2026-10-28 修复待复验：重启系统后打开新标签，预期不再弹「DPI awareness 不一致」；host.log 应出现 `attach.dpi-mismatch ... hostContext=PerMonitorV2(96) targetContext=Unaware(96) allowed=True`。空闲 ≥20 秒确认工具栏不再周期性闪 X；快速新建不闪，长操作约 0.5 秒后才出现取消按钮。125% / 150% 下 rundll32 Unaware 仍会拒绝并提示顶层分组模式（有意保留的安全边界），请确认窗口保持独立、可分离。
- 0.8.0 / 0.7.0 的应用 / 加入 / 分离 / 收藏旧菜单命名已被 0.11.0 菜单优化替代，不按历史命名验收；仍请验证加入长标题、快捷键对齐及管理弹窗的打开 / 编辑 / 保存 / 取消。
- 0.7.0 弹窗 1px 边框、活动标签关闭后聚焦左邻、标签悬停 × 与目录 Tooltip 仍待真机反馈；X / Alt+F4 关闭标签窗口、未关完保留宿主的安全语义不变。
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
- 打包先进入唯一 `.runtime/staging/package-<Guid>` 并持有独占文件锁，版本 / zip 验证后事务替换 dev / release；失败回滚，回滚不完整时保留 stage。仅成功后清本次 stage，历史包与其它 stage 不动。真实 package / dev 关闭宿主仍必须用户明确输入 Y，非交互拒绝；自动验证使用 fake 进程的独立 fixture。
- `assets/screenshots/` 是 README 引用的离屏截图（单行 / 多行），不含真实 Explorer 内容；详细离屏日志仍在 artifacts/ui/v0.6.2/。
- 所有旧 dist 原文件名 / 内容和 docs/manual-test.md / docs/v0.2-test.md 原文保留；历史 CHANGELOG / verification.md 中旧名是事实，不全局替换。
- 原 D:/Git/zgithub/explorer_tab_host 已整体重命名；旧 F:/Developer/AI/vibe/agent_codex/ExplorerTabHost 备份不改；GitHub 仓库为 zouv/explorer-tabs。
