# 代码链路图谱 · ExplorerTabHost

> 图谱是定位索引，不是源码真相；先按任务范围阅读，再按函数名跳转。结构变更同步此文件，超过 400 行拆分。

## 0. 一句话架构
C# WinForms 宿主为 Windows 10 Explorer 提供标签：每个标签一个真实常驻窗口，跨进程 SetParent 嵌入或分组显隐；不注入 Shell、不猜目录、不导航既有窗口。

Program → MainForm → HostController → NativeWindowApi / TaskbarService
新建 / 复制 → StaExplorerTabService（单飞 STA）→ ExplorerCreationWorkflow → ShellExplorerBackend
恢复 → RecoveryStore → HostController → RestorePresentation

## 0.5 任务作用域路由
| 任务 | 该读 | 可忽略 |
|---|---|---|
| 新建 / 复制失败 | App/ShellExplorerBackend.cs、ShellBrowserInterop.cs、ShellViewReference.cs、Core/ExplorerCreation.cs、FolderTarget.cs、SourceWindowMatch.cs、App/MainForm.cs、tests/ShellInteropTests.cs、CreationUiTests.cs | 恢复窗口、排序 UI |
| 接管确认与菜单 | MainForm.AddWindows / CreateTabAsync、CreationUiTests.cs | Shell 导航内部、恢复实现 |
| 恢复黑块 / 缩略图 | Core/HostController.cs、NativeWindowApi.cs、RestorePresentation.cs、TaskbarService.cs | 新建、设置 |
| 标签交互 / 快捷键 | App/TabStrip.cs、MainForm.cs、TabKeyboardHook.cs、Core/TabShortcutPolicy.cs、NewTabShortcutPolicy.cs | Shell COM |
| 构建 / 发布 / 模板 | scripts/build.ps1、Directory.Build.props、manager.sh、根文档 | 窗口操作代码 |

## 1. 文件职责
以下源码路径相对项目根。
| 文件 / 目录 | 职责 |
|---|---|
| src/ExplorerTabHost.App/Program.cs | 单实例、恢复处理、设置加载和依赖注入 |
| src/ExplorerTabHost.App/MainForm.cs | 工具栏、创建请求、接管、分离、退出、设置 |
| src/ExplorerTabHost.App/ShellExplorerBackend.cs | STA Shell COM 创建、目录读取、PIDL 导航及诊断 |
| src/ExplorerTabHost.App/ShellBrowserInterop.cs | SDK 完整展平的 IWebBrowser2 原生 dual ABI，继承方法不能删改顺序 |
| src/ExplorerTabHost.App/ShellViewReference.cs | IServiceProvider → IShellBrowser → IShellView → IFolderView / IPersistFolder2，只读精确视图和目录 |
| src/ExplorerTabHost.Core/SourceWindowMatch.cs | 视图优先的精确窗口归属，拒绝公共宿主 / 过期自动化句柄 |
| tests/ExplorerTabHost.Tests/ShellInteropTests.cs | 原生 vtable stub 实测 CLR COM 封送，非真实 Shell 创建 |
| src/ExplorerTabHost.App/TabStrip.cs、TabKeyboardHook.cs | 标签显示 / 交互与前台限定键盘钩子 |
| src/ExplorerTabHost.App/SettingsDialog.cs | 挂载模式 / 快捷键 / 默认目录 |
| src/ExplorerTabHost.Core/HostController.cs | WindowSession 接管 / 激活 / 恢复状态机 |
| src/ExplorerTabHost.Core/NativeWindowApi.cs、TaskbarService.cs、RestorePresentation.cs | Win32 / Shell 操作、恢复顺序与只读呈现采样 |
| src/ExplorerTabHost.Core/ExplorerCreation.cs | 创建工作流、取消与 STA 单飞门禁 |
| src/ExplorerTabHost.Core/FolderTarget.cs、AppSettings.cs | 普通目录验证与原子设置存储 |
| tests/ExplorerTabHost.Tests/ | 控制台断言、fake Shell、透明自建窗口、隐藏 UI 测试 |
| scripts/build.ps1、manager.sh | Release 构建、受保护发布和可选安全 smoke 模式 |
| ExplorerTabHost.sln、Directory.Build.props | 工程入口、net48 / x64 / 版本 / 警告即错误 |
| dist/、artifacts/ | 忽略的版本化发布与验证图像 |
| docs/ | 使用文档和用户已填历史反馈（不可改原清单） |
| AGENTS.md、CLAUDE.md | 工作规则；CLAUDE 单行导入 |
| ARCHITECTURE.md、ROADMAP.md、CHANGELOG.md、HANDOFF.md | 图谱、计划、版本记录、进行中接力 |
| check-docs.sh、.claude/skills/ | 模板机械体检与显式文档技能 |
| README.md、.gitignore | 使用入口与构建产物排除 |

## 2. 任务 → 代码入口
| 任务 | 文件 / 函数 | 约束 |
|---|---|---|
| 新建 / Ctrl+T / 复制 | MainForm.CreateTabAsync / QueueNewTab / QueueCreateFromMenu | 15 秒超时；不得迟到接管 |
| 创建身份验证 | ExplorerCreationWorkflow.Run / Verify | 创建前已有 HWND + 受管 HWND 全排除；禁止猜窗口 |
| PIDL 导航参数 | ShellExplorerBackend.BrowserReference.Navigate | 不导航现有浏览器；只操作此次新建 COM 对象 |
| 源目录读取 | ShellExplorerBackend.ReadDirectory / ShellViewReference.TryOpen / ReadDirectory | 用自己的 Shell view HWND 子孙关系匹配，前后核验身份；拒绝虚拟目录 |
| 加入窗口 | MainForm.AddWindows / AttachSelectedWindows | 用户显式选择后直接加入，无二次实验确认；不自动捕获 |
| 保存设置 | MainForm / AppSettingsStore | 保存成功才更新会话设置；坏文件保留 |
| 恢复既有标签 | HostController.Detach / DetachAll | 保持隐藏完成结构恢复，再显示重绘和任务栏注册 |

## 3. 重点链路与易踩坑
- 0.4.0 真机 Navigate2 报 E_INVALIDARG（日志已定位）。0.4.1 使用完整原生 dual vtable、PIDL 的 VT_ARRAY|VT_UI1 和 optional VT_EMPTY；原生 stub 验证传输，用户本轮另行确认真实 Shell 新建及复制功能正常。
- 新建必须由独立 ShellBrowserWindow COM 引用关联 HWND；不运行 explorer.exe 后找最新窗口，不调用 Quit。
- 复制只复制当前普通目录，不承诺选中项 / 浏览历史 / 视图模式；URL 为空 / 虚拟目录明确拒绝，不从标题猜。
- 复制枚举每项 QueryService(SID_STopLevelBrowser) / QueryActiveShellView / GetWindow。源 HWND 必须包含该视图（IsChild）或与可用精确句柄一致；不按公共根、标题、进程或枚举顺序回退。有视图时它优先，否决冲突的自动化 HWND。
- 目录从 IFolderView.GetFolder(IPersistFolder2) → GetCurFolder → SHGetPathFromIDListEx 解析，视图接口不可用才从已精确匹配浏览器的 LocationURL 读取；虚拟目录仍拒绝。每项 HRESULT、句柄与最终匹配有日志，单项过期 COM 对象不阻塞其他项。
- 跨进程恢复显隐顺序已用户验证，不因新建修复改动恢复实现。

## 4. 接口契约
| 接口 | 契约 |
|---|---|
| IExplorerTabService.CreateAsync | source 可空；携带受管 HWND 快照；返回精确 WindowIdentity 和目录 |
| IExplorerCreationBackend | STA 内创建 / 读取；先 CaptureExistingHandles，后创建 |
| ICreatedExplorerReference | Handle / Busy / LocationUrl / Navigate / Show / Dispose；Dispose 只释放 COM |
| WindowIdentity / NativeWindowApi.Matches | HWND + PID / TID / 进程启动时间 / 映像 / 类名，不仅 HWND |
| RecoveryStore / AppSettingsStore | 先持久化，再有破坏性结构修改；原子替换，不覆盖坏原文件 |

## 5. 构建与安全验证
Windows + .NET SDK + .NET Framework 4.8 Developer Pack，无第三方 NuGet。
入口：powershell -ExecutionPolicy Bypass -File scripts/build.ps1 -NativeSmoke -CrossProcessSmoke -TabUiSmoke -CreationUiSmoke。
测试不启动真实 Shell、不展示交互宿主、不注册全局钩子；原生 COM stub 只调用本机目录解析，不产生 Explorer。已有 Explorer 的只读诊断与真实创建验收分开，后者留用户手动验证。
