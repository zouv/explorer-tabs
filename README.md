# ExplorerTabs 0.6.2 — Windows 10 Explorer 多标签原型

独立 C# / WinForms / .NET Framework 4.8 / x64 程序。**没有改 QTTabBar，也没有复制其 GPL 代码。**

每个标签对应一个存活的原生 Explorer 窗口。切换只布局 / 显隐窗口，不调用 `BrowseObject`，不重新导航文件夹，不关闭 Explorer，不注入 DLL。保留完整视图状态的前提是 Explorer 自身及窗口仍然存活。0.5.0 另外支持普通目录会话保存与显式重开；重开不等于恢复原视图历史。

## 运行

构建后的程序：

```text
D:\Git\zgithub\explorer-tabs\dist\v0.6.2-release\ExplorerTabs.exe
```

新版本单独输出到 `dist\v0.6.2-release`，不覆盖旧版。若旧宿主仍在运行，请先在旧版中分离全部窗口并正常退出，再启动新版。

双击运行，无需管理员权限或安装。目标运行环境：Windows 10 1703+ x64，.NET Framework 4.8。当前开发 / 测试机是 Windows 10 22H2（19045）。没有在其他 Windows 10 版本验收。

1. 不要使用有未完成复制、移动、删除等重要操作的 Explorer 窗口进行首轮测试。
2. 启动 ExplorerTabs，默认嵌入模式；需要时通过工具栏设置图标选择挂载模式。
3. 点 **＋ 新建标签** 或在宿主 / 当前受管 Explorer 前台按 **Ctrl+T**；也可点 **加入窗口** 手动选择已有窗口。支持多选；不会自动捕获任何窗口。
4. 点击标签切换；至少两个正常标签时，可在宿主或当前 Explorer 前台使用 **Ctrl+Tab / Ctrl+Shift+Tab** 循环切换。设置页可统一禁用宿主快捷键，也可逐个改绑或禁用。键盘焦点转移为 best-effort，必要时仍可点击文件区域。
5. 拖动标签排序；右键可真正关闭此 Explorer、收藏目录、重命名、恢复目录名称、向左 / 右移动、分离当前 / 其他 / 右侧 / 全部标签。中键仍可分离当前标签。分离不是关闭，窗口恢复为独立 Explorer。
6. 标签右键 **复制此标签的目录…** 创建独立的新 Explorer；通过 **设置…** 修改默认目录、挂载模式和快捷键。已有标签时不能改模式。
7. 正常退出使用 **窗口 → 恢复窗口并退出宿主** 或关闭外壳；先尝试保存会话，再恢复全部窗口，恢复失败则阻止正常退出。

请先关闭 QTTabBar 或其他工具的“自动捕获新窗口”功能，避免多个工具竞争。

### 0.6.2：统一名称与工程

- 产品、solution、项目、命名空间与程序文件统一为 **ExplorerTabs**；仓库目录为 `D:\Git\zgithub\explorer-tabs`。
- 为兼容已有配置、会话和事故恢复，继续使用 `%LOCALAPPDATA%\ExplorerTabHost`；不自动迁移 / 覆盖数据。新旧版沿用相同单实例互斥标识，不能同时接管窗口。
- 新发布为 `dist\v0.6.2-release\ExplorerTabs.exe`；0.6.1 及更早发布包保持原文件名和内容。

### 本版界面调整

- 标签按标题与实际字体自适应：默认最小 110 / 最大 280 逻辑 px，超长标题省略但保留完整 Tooltip；多行按各标签真实宽度换行。
- 工具栏底部采用低饱和度 1px 分界线，不额外占一行。嵌入时根据 Windows 10 Ribbon 的实际位置裁去其残留标题空区；未知布局保留原样，绝不猜固定偏移或修改 Ribbon 子控件。
- 宿主抑制原生边框的激活重绘，保留边缘缩放与系统窗口行为。灰框 / 空栏的实际机器效果仍需体验确认；可用底部诊断图标导出含宿主布局 / DPI 与嵌入偏移的日志。

### 模式差异

| | 嵌入窗口（实验性，默认） | 顶层窗口分组（兼容回退） |
|---|---|---|
| 实现 | 调整样式，跨进程 `SetParent` 到内容 Panel | 不改变父窗口，协调屏幕位置和显隐 |
| Explorer UI | 去掉顶层标题框，保留其地址栏、目录树、文件视图等 | 保留整个 Explorer 窗口及其标题框 |
| 切换 | 显示新子窗口，再隐藏旧子窗口 | 显示新顶层窗口，再隐藏旧顶层窗口 |
| DPI | 接管前要求宿主和该窗口的 awareness context 相等 | 不执行跨进程 reparent；仍需实测跨屏效果 |
| 任务栏 | 移除 `WS_EX_APPWINDOW`，请求 Shell 移除任务栏项；分离后请求重新注册。0.2.1 调整恢复顺序并补诊断，用户已反馈功能验证通过；本轮保留该恢复实现，不据此保证所有机器 | **保留 Explorer 自身任务栏项**；不是完整的单窗口桌面分组 |
| 已知限制 | Shell 未必能正确适应子窗口、焦点和拖放语义 | Explorer 仍可独立移动、置前；不会自动跟踪其手动移动来反推外壳位置 |
| 崩溃后 | 子窗口可能被销毁，恢复日志无法救回已销毁的窗口 | 仍存活的隐藏窗口可在下次启动时尝试恢复；不是绝对保证 |

启动时在创建 WinForms HWND 前匹配第一个已有 Explorer 的 DPI awareness，每次嵌入还会独立检查。上下文不一致就拒绝嵌入，不冒险调用 `SetParent`。不同 DPI awareness 的窗口不能强行混用；可分离全部标签后切换顶层分组。

**原型不是生产级窗口管理器。** 尚未实现自动接管、任务栏统一、跨宿主拖标签、每标签内置关闭按钮、完整视图状态跨运行恢复、托盘 / 看门狗。没有伪造前台权限，也没有 `AttachThreadInput` 强行抢焦点。

### 0.6.1：浏览器式界面与新建呈现优化

- **窗口栏**：移除独立标题栏，标签与最小化 / 最大化 / 关闭在同一行；标签空白可拖动、双击最大化，品牌图标可打开系统菜单。关闭宿主仍保存会话并恢复窗口，不等同于右键关闭标签。
- **工具栏**：新建 / 加入窗口｜标签搜索 / 常用目录与标签组 / 会话｜窗口管理 / 设置，改用分组图标按钮与 Tooltip；提示展示实际配置的快捷键。诊断入口位于底部右侧。
- **内容优先**：100% 缩放单行标签栏与工具栏各约 36 px；无溢出不占额外滚动高度，多行仍按设置限制可见行数。活动标签、hover / focus、菜单及文字按钮使用统一配色，空状态可直接新建或加入窗口。
- **Ctrl+T / 复制 / 批量打开**：精确核验本次新建 Explorer 后先延迟显示，准备好再附加标签，去掉主动 Show 独立窗的步骤；失败 / 取消会恢复本次新建独立窗口的可见性。不捕获其他窗口，不关闭失败创建的窗口。
- **边界**：Shell COM 激活瞬间及内部导航仍可能短暂显示；是否完全无闪现需用户真机确认，当前不保证零闪现。硬退出 / 进程被杀仍不是完全可恢复。
- **应用图标**：`assets/icons` 有 A（蓝色叠窗文件夹）、B（深色活动标签）、C（绿色文件夹组）三版原生 SVG 与 `icon-choices.png` 对比图。暂用 A 作为多尺寸 exe / 窗口 ICO，最终款式等待选择。

### 0.5.1：菜单生命周期修复

- 修复工具栏“常用 / 标签组”“会话”“窗口”菜单关闭时的 ContextMenuStrip 已释放异常；菜单与模态范围在框架关闭流程返回后释放，不在 Closed 内同步 Dispose。
- 标签右键重命名 / 排序 / 恢复名称 / 分离，以及库删除操作统一延后执行，避免在菜单点击链路里重建控件或打开嵌套对话框。
- 菜单取消、显示失败与宿主退出都有显式清理；增加菜单打开 / 关闭原因 / 释放日志。运行中外层异常不再误报为“启动失败”。
- 不修改已保存设置、会话或窗口恢复日志；0.4.1 / 0.5.0 发布包保留，人工菜单操作需再次体验验证。

### 0.5.0：工作空间与标签操作

- **常用目录 / 标签组**：工具栏提供当前目录收藏、文件夹选择添加、打开 / 重命名 / 移除；标签组保存普通目录顺序、别名与活动位置，打开时追加标签。删除收藏 / 组不关闭窗口，同名组替换会确认。
- **会话**：默认自动保存，启动时只提示“恢复上次会话”，不自动创建窗口。可显式保存 / 恢复 / 清除，也可在设置关闭自动保存。清除后不立即自动重建，直到新的标签结构变化或手动保存。
- **保存保护**：普通目录只读捕获，每项有超时；任意读取失败（如虚拟目录）或捕获期间排序 / 别名 / 活动改变，都保留旧会话或组，不静默保存半份。成功的相同内容不重复写盘。
- **恢复保护**：逐项新建、可取消，失败跳过，成功标签保留；迟到结果不接管，不无限增加卡住的 STA 线程。单次会话 / 组最多 100 标签；组追加后的受管总数不超过 100。
- **关闭与分离**：右键“关闭此标签的 Explorer”或 Ctrl+W 请求该精确 HWND 关闭；不杀 explorer.exe、不先分离、不主动恢复为普通窗口。保留恢复日志和标签直到窗口确认消失；超过约 6 秒仍存活则保留并允许重试，不强杀。分离 / 中键 / 普通退出仍恢复独立窗口，不关闭。
- **重开**：Ctrl+Shift+T 按最近顺序重开本次运行中已关闭的普通目录和别名（最多 20 项）；不恢复浏览历史、选中项、滚动位置或已关闭 Explorer 实例。
- **标签列表 / 溢出搜索**：工具栏“标签列表…”或 Ctrl+P 按名称 / 目录过滤，空格分词 AND；↑↓ / Enter / Esc。列表打开前最多约 2.5 秒串行刷新目录，未能刷新时可显示旧缓存 / 标题，不猜虚拟目录路径。
- **多行标签**：设置 → 常规 / 标签栏 → 多行标签栏（Wrap Tabs），默认关闭；可见上限 2–8 行，默认 3。超出纵向滚动，支持跨行排序和揭示选中标签；单行模式保留横向滚动。
- **命令面板**：Ctrl+Shift+P 搜索常用命令、收藏目录与标签组。
- **界面**：紧凑工具栏、明确的窗口操作菜单、活动标签高亮、双行搜索结果、常规 / 快捷键两页设置。自动保存 / 批量创建期间部分冲突操作暂时禁用；可用“取消操作”停止当前工作。

#### 默认快捷键

| 快捷键 | 操作 |
|---|---|
| Ctrl+T | 新建标签 |
| Ctrl+W | 关闭当前标签的 Explorer（非分离） |
| Ctrl+P / Ctrl+Shift+P | 标签搜索 / 命令面板 |
| Ctrl+Shift+T | 重开刚关闭的普通目录 |
| Ctrl+Shift+D | 复制当前标签目录 |
| Ctrl+Tab / Ctrl+Shift+Tab | 下一 / 上一标签 |
| Ctrl+PageDown / Ctrl+PageUp | 下一 / 上一标签备用组合 |
| Ctrl+1…8 / Ctrl+9 | 第 N 个 / 最后一个标签 |
| Ctrl+Shift+R | 重命名当前标签；F2 留给 Explorer 文件重命名 |
| Ctrl+Shift+S | 保存当前会话 |
| Ctrl+, | 设置 |

设置 → 快捷键中有 22 个操作，可从受支持组合中重绑、留空禁用或恢复默认；重复组合不能保存。不是任意键录制器，也没有全局文件操作快捷键。
只在宿主 / 当前受管 Explorer 前台、非菜单 / 独立对话框 / 宿主弹窗时生效；其他程序不受接管。实际前台、焦点和各 DPI 环境仍待本轮用户验收。

当前 0.6.1 Release 构建零警告 / 零错误，666 项安全自动检查通过（核心 396、原生 44、跨进程 7、标签 UI 29、创建 UI 34、工作空间 / 窗框 UI 156），并检查空状态、720 / 1100 宽单行 / 多行、设置两页及重命名实际控件的离屏图像。**未自动运行真实 Explorer / 交互宿主 / 全局钩子；新建闪现、原生窗框及高 DPI 体验需用户反馈。**

### 0.4.1：反馈修复与迁移

- 项目已按 vibe-coding-starter 文档模板初始化，首次迁至 D:\Git\zgithub\explorer_tab_host（0.6.2 已统一改名为 explorer-tabs）。沿用 C# / net48 / x64 工程，无 Python / uv 残留；旧工程及原发布保留为备份。
- 新建 / Ctrl+T：日志确认失败点在 Navigate2。改用 Windows SDK 完整展平的 IWebBrowser2 原生 dual ABI；PIDL 传 VT_ARRAY|VT_UI1，可选参数传 VT_EMPTY，不再使用 Type.Missing 的晚绑定参数路径。
- 复制目录：按精确 Shell view 与源窗口归属匹配；目录优先从当前绝对 PIDL 读取，处理 LocationURL 为空的情况。拒绝共享宿主根窗口、其他标签和虚拟目录，不按标题猜测。
- 加入窗口后不再二次弹出“实验性接管确认”，新建也移除相同确认；窗口选择器和静态风险提示保留。
- 新增导航 ABI / 参数类型 / PIDL 长度 / 失败 HRESULT，以及每个 Shell 候选的自动化、浏览器、视图句柄与匹配结果日志。
- 安全自动测试通过；用户本轮已确认新建 / Ctrl+T、嵌入后的不同目录标签复制及加入窗口功能正常。请从最终发布目录运行；dist/v0.4.1 和 dist/v0.4.1-fix 是本轮中间构建，不是最终入口。

### 0.4.0：新建标签、复制目录与基础设置

- **新建**：工具栏“＋ 新建标签”与前台限定 **Ctrl+T**；空宿主也可新建。创建成功后追加并激活标签。
- **复制目录**：标签右键“复制此标签的目录…”，读取该 HWND 对应的 Shell LocationURL，再新建独立 Explorer。只支持普通本地文件夹 / UNC；“此电脑”、搜索结果等虚拟位置暂不支持。不复制别名、历史、选中项或滚动位置，不改变原标签的目录和状态。
- **明确身份**：激活专属 ShellBrowserWindow COM 引用，创建前记录已有顶层 HWND（包括隐藏窗口）和受管 HWND。初始句柄若已存在，在 Navigate2 前拒绝操作。使用目录的绝对 PIDL 导航，通过专属引用 HWND 与进程身份核验；不使用标题、PID 或“最新窗口”猜测，不以启动 explorer.exe 后抓窗口作为回退。
- **等待与取消**：目录访问与外进程 COM 均在单飞 STA 工作线程中执行；UI 请求 15 秒超时，等待时可点“取消新建”。取消后不会迟到接管。外进程调用不能保证立即终止，因此仍可能稍后出现独立新窗口；不自动关闭它。该工作线程实际退出前不允许另一次新建。
- **设置**：记住挂载模式、快捷键开关、新建标签默认目录，以及正常退出时宿主的位置 / 尺寸 / 最大化状态。默认目录为当前用户目录；设置对话框只验证路径语法，实际可访问性在创建工作线程检查，避免 UNC 检查卡住界面。
- **存储**：%LOCALAPPDATA%\ExplorerTabHost\settings.json，与恢复日志独立。保存使用临时文件、Flush(true) 和原子替换；失败不部分更新实时设置。读取失败保留原文件、本次使用默认值并停用持久化，不悄悄覆盖损坏文件。
- **保留范围**：一个标签仍对应真实常驻窗口，切换不导航；0.2.1 原生恢复 / 重绘 / 任务栏顺序与 0.3.0 标签管理逻辑不改。本轮为 Ctrl+T 扩展了键盘钩子分发，原 Ctrl+Tab 配对策略未改，仍需真实前台 / 菜单 / 对话框组合回归。

本轮未增加全局自动接管、Ctrl+W、会话目录重开或看门狗。自动验证使用假 Shell 后端和测试自身窗口，没有自动创建真实 Explorer；真实 Shell 创建及复制目录需要用户体验验证。

### 0.3.0 日常标签管理

- **拖动排序**：本地鼠标跟踪；超过系统拖动阈值后显示插入线，靠近两端自动横向滚动。移出标签栏释放、Esc 或失去捕获会取消排序，不分离窗口。右键“向左 / 右移动”可作替代。排序不切换当前 Explorer，也不修改恢复日志。
- **重命名标签**：自定义别名最长 120 个 UTF-16 字符，拒绝空白名称和控制字符；不修改目录名或 Explorer 原标题。导航后保留别名，“恢复目录名称”重新使用 Explorer 标题。长名称省略显示，悬停可看完整标题。
- **右键批量分离**：“其他”和“右侧”以右键点击的标签为基准，而非当前活动标签；“右侧”按当前排序计算。所有分离操作都走原来的安全恢复流程，不发送关闭命令。个别恢复失败保留条目和日志，继续处理其余目标。
- **相邻标签接续**：分离当前标签后，优先选择右侧正常标签，没有则选择最近的左侧正常标签；只分离后台标签时保留当前活动标签。Ctrl+Tab 跟随新排序。
- **界面交互保护**：排序时暂停定时标签刷新；拖动、菜单和重命名对话框期间暂停 Ctrl+Tab 作用域。标签点击、拖动完成和中键分离延迟到当前鼠标消息结束后执行，避免处理中销毁按钮。
- **会话内生效**：排序和别名仅在本次宿主运行中保存，不是磁盘持久化，也不重开目录。不新增全局鼠标钩子、OLE 文件拖放接管或自动窗口捕获。

用户已反馈 0.3.0 标签管理功能正常。当前 0.4.1 继续保留该功能；自动测试不替代新功能的真实 Shell 验收。

### 0.2.1 稳定基线与已知边界（本轮保留）

- **保持隐藏完成结构恢复**：0.2.0 在恢复样式时直接写回含 `WS_VISIBLE` 的原值，早于正式显示。0.2.1 改为隐藏 → 恢复 parent / 去掉可见位的原样式 → frame / placement → 显示 → 校验 → 重绘请求 → 任务栏注册。这个具体顺序缺陷是可疑点，不代表已经证实缩略图缺失或黑块的全部根因。
- **全树重绘请求**：嵌入窗口分离后请求 `RDW_INVALIDATE | RDW_ERASE | RDW_ALLCHILDREN | RDW_FRAME`，不强制同步执行外进程 paint。请求被接受不等于真实像素已绘制；用户对 0.2.1 已反馈功能验证通过，不据此泛化所有环境。
- **诊断日志**：记录运行版本 / EXE 路径 / PID / 系统 / DPI，恢复阶段耗时和 parent / owner / style / placement / 显隐，真实 Shell 调用 HRESULT，以及子窗口、DWM cloaked、更新区域和 `SysSetRedraw`。分离后约 200 毫秒 / 1 秒 / 3 秒追加有限只读采样；不重试 AddTab、不重绘、不抢焦点。退出只记即时状态，不等待延迟采样，也不延长生命周期。
- **保存诊断…**：右下角按钮在弹出文件对话框前采集状态，将 `host.log`、上一份轮转日志、`recovery.json` 和窗口状态打包为本地 ZIP；不覆盖已有文件、不递归扫描、不上传。显式导出也只读查询现有顶层 Explorer，因此退出后重新打开空宿主也能导出现场，无需重新接管。包可能含目录标题与路径，请检查后再分享。
- **Ctrl+Tab**：保留 0.2.0 的前台限定专用钩子线程和按键配对策略。此前用户反馈两方向、焦点、菜单、独立对话框及其他程序均正常；0.3.0 保留钩子本体，扩展宿主界面交互时的禁用范围。不据此宣称所有机器 / DPI / 生命周期边界已验收。
- **窗口显示语义**：普通和最小化恢复使用非激活命令；原最大化状态继续保留最大化 placement 语义，不承诺最大化恢复也绝不激活。没有强制前台权限、伪造 Shell 通知或同步重绘循环。

用户反馈直接在对话中收集，不再新增测试清单。原有 `docs\manual-test.md` 和 `docs\v0.2-test.md` 保留，不改用户勾选 / 反馈，也不再复制到新发布目录。

## 状态保护

接管流程：确认窗口身份 → 响应 / 顶层 / DPI 检查 → 保存恢复日志 → 挂载 → 成功提交；中途失败尝试恢复原状态。

恢复快照含 HWND、PID、线程 ID、进程启动 UTC ticks、进程映像、窗口类、原样式 / 扩展样式、parent / owner、`WINDOWPLACEMENT`、显隐和宿主句柄。HWND 不用于跨运行标识；恢复前重新核对其他身份字段。这样可降低 HWND 重用风险，但不消除所有同进程、同线程的句柄重用竞态。

日志位置：

```text
%LOCALAPPDATA%\ExplorerTabHost\host.log
%LOCALAPPDATA%\ExplorerTabHost\recovery.json
%LOCALAPPDATA%\ExplorerTabHost\settings.json
%LOCALAPPDATA%\ExplorerTabHost\workspace.json
```

恢复日志用临时文件、`Flush(true)` 和替换 / 重命名更新。必须先成功写入，才允许接管。分离后日志清理失败时，当前进程只重试日志清理，不再次移动已恢复的窗口。身份查询权限失败被视为“未知”，不会当成窗口已消失而丢弃记录。

下次启动发现未完成记录，会询问是否尝试恢复仍存活的原窗口；拒绝则保留记录并退出。未处理旧记录前不接管新窗口。恢复失败的条目保留，原宿主仍存活时拒绝争夺其窗口。**不要手动删除有未恢复窗口的 recovery.json。**

这些措施保护的是可控失败 / 正常退出，不是强杀、掉电或 Explorer 崩溃的完整保证。`SetParent` / `SetWindowPos` 仍涉及非合作应用的同步窗口消息；响应探测不能保证随后调用绝不阻塞。建议用虚拟机完成强杀实验。

日志中的 `host-calls-ms` 仅是宿主布局 / 显隐调用耗时，**不代表 Explorer 重绘完成、切换可见延迟，也不能据此宣称“瞬切无闪烁”**。

## 构建与自动验证

需要已安装 .NET SDK 和 .NET Framework 4.8 Developer Pack。不依赖第三方 NuGet 包。

```powershell
powershell -ExecutionPolicy Bypass -File "D:\Git\zgithub\explorer-tabs\scripts\build.ps1"

# 可选：仅操作测试程序自己创建的透明窗口，不操作任何现有 Explorer。
powershell -ExecutionPolicy Bypass -File "D:\Git\zgithub\explorer-tabs\scripts\build.ps1" -NativeSmoke -CrossProcessSmoke -TabUiSmoke -CreationUiSmoke -WorkspaceUiSmoke
```

测试是独立 console runner，不是 `dotnet test` / xUnit。默认运行策略 / 模拟 / 日志，以及无窗口的原生 COM vtable stub / PIDL 封送测试；原生、隐藏标签 UI、隐藏创建 / 设置 UI 及工作空间 UI smoke 显式开启。默认发布目录为 `dist\v0.6.2-release`；可用 `-OutputDirectory "dist\另一个版本"` 单独输出，不应覆盖正在运行的版本。

只读 Explorer 枚举 / DPI 诊断，不移动、隐藏或接管窗口：

```powershell
$exe = 'D:\Git\zgithub\explorer-tabs\dist\v0.6.2-release\ExplorerTabs.exe'
Start-Process -FilePath $exe -ArgumentList '--diagnose' -WindowStyle Hidden -Wait
Get-Content "$env:LOCALAPPDATA\ExplorerTabHost\diagnostics.txt"
```

## 源码结构

- `D:\Git\zgithub\explorer-tabs\ExplorerTabs.sln`：统一 solution 入口；App / Core / Tests 项目均使用 ExplorerTabs 前缀。
- `D:\Git\zgithub\explorer-tabs\src\ExplorerTabs.Core\ProductIdentity.cs`：品牌名与必须保留的旧版数据 / 互斥身份。
- `D:\Git\zgithub\explorer-tabs\tests\ExplorerTabs.Tests\BrandingTests.cs`：改名元数据、嵌入资源、旧 JSON 格式及合成用户跨进程互斥检查。

- `D:\Git\zgithub\explorer-tabs\src\ExplorerTabs.Core\ExplorerCreation.cs`：专属创建流程、可取消单飞 STA 服务。
- `D:\Git\zgithub\explorer-tabs\src\ExplorerTabs.App\ShellExplorerBackend.cs`：Shell COM、绝对 PIDL 导航及精确 HWND 目录读取。
- `D:\Git\zgithub\explorer-tabs\src\ExplorerTabs.Core\FolderTarget.cs`：普通目录和 UNC 校验。
- `D:\Git\zgithub\explorer-tabs\src\ExplorerTabs.Core\AppSettings.cs`：基础设置、原子保存及屏幕布局策略。
- `D:\Git\zgithub\explorer-tabs\src\ExplorerTabs.App\SettingsDialog.cs`：设置对话框。
- `D:\Git\zgithub\explorer-tabs\src\ExplorerTabs.Core\NewTabShortcutPolicy.cs`：独立 Ctrl+T 按键配对策略。
- `D:\Git\zgithub\explorer-tabs\tests\ExplorerTabs.Tests\FeatureTests.cs`、`D:\Git\zgithub\explorer-tabs\tests\ExplorerTabs.Tests\CreationUiTests.cs`：假 Shell 故障注入与隐藏创建 / 设置 UI 验证。

- `D:\Git\zgithub\explorer-tabs\src\ExplorerTabs.Core\HostController.cs`：可测试的接管、标签切换、排序 / 别名 / 批量分离和恢复状态机。
- `D:\Git\zgithub\explorer-tabs\src\ExplorerTabs.Core\TabManagementPolicy.cs`：纯标签命名、批量目标、相邻接续和拖动索引策略。
- `D:\Git\zgithub\explorer-tabs\src\ExplorerTabs.App\TabStrip.cs`：本地标签拖动、边缘滚动和菜单生命周期。
- `D:\Git\zgithub\explorer-tabs\src\ExplorerTabs.App\RenameTabDialog.cs`：会话内别名编辑与校验。
- `D:\Git\zgithub\explorer-tabs\src\ExplorerTabs.Core\NativeWindowApi.cs`：Explorer 识别、身份检查、两种模式的 Win32 后端。
- `D:\Git\zgithub\explorer-tabs\src\ExplorerTabs.Core\RecoveryJournal.cs`：恢复日志和启动恢复报告。
- `D:\Git\zgithub\explorer-tabs\src\ExplorerTabs.Core\TaskbarService.cs`：Shell 任务栏注册 / 移除和 HRESULT 日志。
- `D:\Git\zgithub\explorer-tabs\src\ExplorerTabs.Core\RestorePresentation.cs`：隐藏样式、显示命令与异步重绘请求。
- `D:\Git\zgithub\explorer-tabs\src\ExplorerTabs.Core\WindowDiagnostics.cs`：只读窗口树 / DWM / 更新区域诊断。
- `D:\Git\zgithub\explorer-tabs\src\ExplorerTabs.Core\DiagnosticArchive.cs`：白名单本地诊断包导出。
- `D:\Git\zgithub\explorer-tabs\src\ExplorerTabs.App\PresentationMonitor.cs`：有限延时采样与导出快照。
- `D:\Git\zgithub\explorer-tabs\src\ExplorerTabs.Core\TabShortcutPolicy.cs`：可测试的按键配对策略。
- `D:\Git\zgithub\explorer-tabs\src\ExplorerTabs.App\TabKeyboardHook.cs`：前台限定快捷键及专用钩子线程。
- `D:\Git\zgithub\explorer-tabs\src\ExplorerTabs.App\MainForm.cs`：标签外壳、布局、手动接管和退出保护。
- `D:\Git\zgithub\explorer-tabs\tests\ExplorerTabs.Tests\Program.cs`：故障注入、日志测试、本进程 / 跨进程透明窗口 smoke。

## 验收范围

本机本轮已通过 **268 项策略 / 模拟 / 日志 / 原生 COM 封送检查、44 项本进程透明窗口检查、7 项跨进程 helper 检查、29 项隐藏标签 UI 检查和 34 项隐藏新建 / 设置 / 加入窗口 UI 检查（共 382 项）**。Release 为 0 警告 / 0 错误。额外完成 720 宽空宿主 / 标签栏及设置面板的离屏渲染核查。

原生 COM stub 实测 Navigate2 的 vtable 调用、VT_ARRAY|VT_UI1 完整 PIDL、VT_EMPTY 可选参数、VARIANT_BOOL 和失败 HRESULT。只读诊断已成功从本机已有 Explorer 取得对应 Shell view 与 IPersistFolder2 当前目录；未移动、隐藏、关闭或导航该窗口。

自动检查未启动真实 Shell 新建流程、交互宿主或全局快捷键钩子，不单独代表真机验收。人工验收另由用户完成：0.2.1 恢复 / 焦点范围、0.3.0 标签管理及 0.4.1 新建 / Ctrl+T、不同目录标签复制和加入窗口均已反馈通过。

本轮自动验证日志位于 artifacts/full-v0.4.1.log（忽略入库）。docs/verification.md 保留为 0.4.0 历史证据，不作为本轮结果。后续人工反馈直接在对话收集，不另建清单或修改原反馈文件。
