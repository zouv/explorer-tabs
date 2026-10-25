# 当前工程验证结果 — 0.4.0

日期：2026-10-25（本机 Asia/Shanghai）。Windows 10 22H2 / build 19045 / x64，目标 .NET Framework 4.8。

## 当前用户反馈与实现范围

- 用户已确认 0.2.1 恢复 / 黑块 / 任务栏缩略图 / Ctrl+Tab 作用域，以及 0.3.0 排序、别名和批量分离正常；仅表示本机体验通过，不泛化所有系统。
- 新增“＋ 新建标签”、前台限定 Ctrl+T、右键复制普通目录、本机基础设置保存。
- 每个标签仍对应真实常驻 Explorer；切换不导航。复制只复制位置，不复制别名、历史、选中项或滚动位置。虚拟目录 / 搜索结果暂不支持。
- 原生恢复、重绘和任务栏顺序保持不变。原 Ctrl+Tab 策略不改，但为 Ctrl+T 扩展了 TabKeyboardHook 分发、空宿主作用域及 T / Win 键状态记录；真实前台范围仍需回归，不能声称钩子本体未改。
- 本轮未做自动捕获、Ctrl+W、会话目录重开或看门狗。后续反馈在对话询问，不新增人工清单；原清单反馈和勾选未改。

## 创建、取消和设置边界

专属 ShellBrowserWindow COM 引用创建。创建前记录全部已有顶层 HWND（含隐藏窗口）及受管 HWND；初始句柄若已存在，在导航前拒绝。不用标题、PID 或“最新窗口”猜测，不启动 explorer.exe 后抓窗口回退。

普通目录经 SHParseDisplayName 得到绝对 PIDL，以字节 SAFEARRAY 传入 Navigate2。Busy=false 不单独视为成功，还等待 LocationURL 确认目标。模态 / 拖动期间延迟接管；挂载失败残留恢复条目时立即刷新可见标签，激活失败走原 Detach 回滚。

网络访问与 COM 在独立单飞 STA 工作线程执行。UI 请求 15 秒超时、可取消；取消立即结束 UI 等待，迟到结果不接管。不 Abort 工作线程；外进程调用不能保证及时终止，可能稍后出现独立新窗口，不自动关闭。真实线程退出前保留 busy gate，不累积线程。

日志补源 HWND、已有句柄数、COM 创建开始 / 返回、目录解析、Navigate2 开始 / 返回、显示、线程释放、接管 / 取消等阶段，可能含目录路径。Windows SDK ExDisp.Idl 和本机 shdocvw.dll 类型库已只读核对接口：IShellWindows.Count DISPID 为 1610743808（0x60020000）；类型库读取未激活 Shell 实例。

设置路径：`%LOCALAPPDATA%\ExplorerTabHost\settings.json`，与 recovery.json 独立。保存模式、快捷键、新建目录、正常退出时窗口矩形和最大化状态。默认目录为用户目录。界面只校验语法，不同步检查 UNC 可访问性。临时文件、Flush(true)、原子替换保存，失败不提交实时设置。读取损坏 / 无权限 / 过大 / 未来版本文件时保留原文件、本次默认运行且停用持久化。

## 自动验证与证据

运行命令：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File "F:\Developer\AI\vibe\agent_codex\ExplorerTabHost\scripts\build.ps1" -NativeSmoke -CrossProcessSmoke -TabUiSmoke -CreationUiSmoke
```

Release 构建 **0 warnings / 0 errors**，实际结果：

| 测试 | 通过项数 |
| --- | ---: |
| 纯策略 / 模拟 / 日志 / 设置 / 假 Shell 创建 | 243 |
| 本进程测试自身透明窗口原生检查 | 44 |
| 测试自身跨进程透明 helper 检查 | 7 |
| 隐藏标签 / 排序 / 重命名 UI | 29 |
| 隐藏新建 / 设置 UI | 30 |
| 合计 | **353** |

新增覆盖路径 Unicode / UNC / URL 解码 / 非法命令及控制字符、Ctrl+T 配对 / 注入 / 启动持键 / 连按、设置默认 / 损坏保留 / 锁文件原子失败 / 临时清理、屏幕边界、已有 HWND 导航前拒绝、目录读取、导航延迟、句柄改变、身份丢失、各阶段取消、STA 单飞 / 立即取消 / 迟到释放 / 输入快照 / 释放失败，以及实际隐藏 MainForm 的新建 / 复制 / 取消 / 模态延迟 / 关闭后迟到 / 设置先存再提交 / 挂载及回滚失败。

日志：`F:\Developer\AI\vibe\agent_codex\ExplorerTabHost\artifacts\v0.4.0\build-and-tests.log`。

实际 WinForms 离屏渲染已看图检查，包含普通宽度、720 最小宽度工具栏换行 / 完整标签行和设置对话框：

- `F:\Developer\AI\vibe\agent_codex\ExplorerTabHost\artifacts\v0.4.0\ui-tabs.png`
- `F:\Developer\AI\vibe\agent_codex\ExplorerTabHost\artifacts\v0.4.0\ui-tabs-minimum.png`
- `F:\Developer\AI\vibe\agent_codex\ExplorerTabHost\artifacts\v0.4.0\settings.png`

自动检查不 Show / Shown 交互宿主，不安装全局快捷键钩子，不注入键鼠，不创建 / 接管 / 隐藏 / 移动 / 关闭真实 Explorer。新建使用 fake backend；真实 Shell 创建、复制及前台键盘组合待用户反馈，不等同于完整集成验收。

## 发布与文件保护

新发布：`F:\Developer\AI\vibe\agent_codex\ExplorerTabHost\dist\v0.4.0\ExplorerTabHost.exe`，FileVersion 0.4.0.0。EXE / Core DLL 与 Release 输出一致，README 与当前说明一致。

- EXE SHA-256：E9B37066CEFB72765BD232F4C9207D8E7D08373D0C3F27427BF75485E1011CEA
- Core DLL SHA-256：456AEB964E9CC25AEB9153EAB7D2791803D5E36785C826C30CAD7EEC2C7434A4
- 原两份人工清单、最初 dist 及 v0.2.0 / v0.2.1 EXE 与下面历史哈希一致；v0.3.0 EXE 仍为 49CEE12DAACF160DB1FCCE1C727F47A71410E83C1B1FAA050CD8B95A424E6EBC。
- 没有启动或终止用户宿主、没有覆盖旧发布，只输出新 v0.4.0。没有写用户真实 settings.json、没有清理用户 git 状态、没有提交。

保留实验性 SetParent 风险及旧恢复证据边界。最大化只有布局 / 保存策略检查，未在桌面激活 / 最大化真实窗口；设置后重启需实际体验。不承诺强杀、掉电或 Explorer 崩溃后救回已销毁的窗口。

---

# 0.3.0 历史交付记录（当时的验收状态）

日期：2026-10-25（本机 Asia/Shanghai）。环境：Windows 10 22H2 / build 19045 / x64，.NET SDK 10.0.303，目标 .NET Framework 4.8。

## 用户反馈与基线

- 最新对话反馈：用户表示“现在功能验证已经 Ok 了，可以推动下一步了”。将 0.2.1 作为本机功能已验证的稳定基线，包括此前排查的分离 / 退出后任务栏缩略图和局部黑块问题。不据此宣称所有系统、DPI 或生命周期边界都已通过。
- 此前已明确反馈 Ctrl+Tab 两方向及焦点正常，菜单、独立对话框和其他程序不受影响。
- 0.3.0 新增拖动排序、标签别名和批量分离尚未获得人工反馈。真实鼠标消息链、边缘滚动、弹出菜单和对话框与 Explorer 的组合仍需用户体验；工程自动检查不替代这些验收。
- 后续人工反馈直接在对话询问，不再新增测试清单。原用户填写的两个测试文档未修改，留空项不自动补勾。

## 0.3.0 实现范围

- 一个标签继续对应一个真实常驻 Explorer；不导航、不注入、不自动捕获。NativeWindowApi、RestorePresentation、TaskbarService 和 TabKeyboardHook 的已验证实现本轮不改。
- 排序和自定义别名仅改变当前宿主会话元数据，不写恢复日志，不改目录名或 Explorer 原标题。Ctrl+Tab 根据新的标签顺序切换。
- 本地拖动超过阈值后显示插入线、边缘滚动；区外释放、Esc 和捕获丢失取消。不使用全局鼠标钩子、OLE 文件拖放或跨宿主拖动。
- 右键支持向左 / 右移动、重命名 / 恢复目录名、分离当前 / 其他 / 右侧 / 全部。批量目标以右键标签为基准；分离走原恢复流程，不关闭 Explorer，失败保留记录并继续其余目标。
- 分离当前标签优先选择右侧正常邻居，其次最近左邻；仅移除后台标签不改变当前活动标签。
- 交互中暂停定时重建标签和 Ctrl+Tab 作用域。标签点击、排序完成及中键分离通过 UI 队列延迟执行，避免在原鼠标消息内销毁控件。
- 窄窗口预留完整横向滚动条高度；显式保留内容宽度和外侧 padding，防止垂直滚动 / 标签行遮挡 / 最右标签滚动不到位。元数据刷新保留横向滚动位置，切换标签可滚入视野。

## 自动验证

运行命令：

```powershell
powershell -ExecutionPolicy Bypass -File "F:\Developer\AI\vibe\agent_codex\ExplorerTabHost\scripts\build.ps1" -NativeSmoke -CrossProcessSmoke -TabUiSmoke
```

Release 构建：**0 warnings / 0 errors**。

- **138 项纯策略 / 模拟 / 日志 / 诊断包检查**：保留原 95 项检查，新增 43 项排序、别名长度 / Unicode / 控制字符、恢复目录名、外来标签拒绝、批量作用域、按排序确定右侧、相邻接续、失效条目跳过、批量恢复失败保留与继续处理、拖动阈值 / 索引等检查。
- **44 项本进程原生检查**：仅操作测试程序创建的透明窗口，覆盖两种模式的挂载 / 显隐 / 布局 / 样式恢复、保持隐藏完成结构恢复、重绘先于注册、非致命 taskbar / repaint 失败、最小化状态和前台不变、只读窗口树 / DWM / 更新区域。任务栏使用假适配器，不操作真实 Shell 注册，不代表真实缩略图验收。
- **7 项跨进程检查**：仅自己的透明 helper 窗口，验证跨进程挂载 / 调整 / 分离和容器销毁不连带销毁已分离 helper。
- **29 项隐藏标签 UI / 重命名对话框检查**：构造实际 MainForm、TabStrip 和 RenameTabDialog，用模拟窗口后端；验证标签文本 / 无障碍名称 / 省略显示 / &、活动背景、菜单边界、鼠标点击 / 拖动阈值 / 提前 Click 抑制、取消 / 越界释放、排序不改原生窗口或日志、旧控件和菜单释放、菜单实际执行批量分离及恢复目录名、最小宽度完整标签行 / 横向滚动 / 最右标签可见 / 滚动位置保留、延迟选择，以及合法 / 空白别名和取消。

共 **218 项通过**。没有自动接管、隐藏、移动或关闭用户已有 Explorer，没有自动打开交互宿主、安装快捷键钩子、接管真实鼠标或注入按键。隐藏 UI 测试使用托管方法调用、消息队列和离屏布局，不是对真实 Explorer 鼠标交互的完整集成验收。最大化恢复仍仅有纯策略检查，未在用户桌面最大化 / 激活测试窗口。

构建和测试日志：`F:\Developer\AI\vibe\agent_codex\ExplorerTabHost\artifacts\v0.3.0\build-and-tests.log`。

## 界面与发布证据

离屏 DrawToBitmap 渲染实际 WinForms 宿主（4 个模拟标签）和重命名对话框；不调用 Show / ShowDialog，不触发 Shown，不安装键盘钩子。三张截图均已逐张查看：默认和 720 最小宽度顶部 / 标签 / 页脚无遮挡，最小宽度仅有横向滚动条，重命名文本及按钮完整。

- `F:\Developer\AI\vibe\agent_codex\ExplorerTabHost\artifacts\v0.3.0\host-tabs-default.png`
- `F:\Developer\AI\vibe\agent_codex\ExplorerTabHost\artifacts\v0.3.0\host-tabs-minimum.png`
- `F:\Developer\AI\vibe\agent_codex\ExplorerTabHost\artifacts\v0.3.0\rename-dialog.png`

新发布：`F:\Developer\AI\vibe\agent_codex\ExplorerTabHost\dist\v0.3.0\ExplorerTabHost.exe`，FileVersion 为 0.3.0.0。EXE、Core DLL 和 README 与构建输出 / 当前说明一致。

以下保护文件 SHA-256 与本轮开始前记录相同：

| 文件（绝对路径） | SHA-256 |
| --- | --- |
| F:\Developer\AI\vibe\agent_codex\ExplorerTabHost\docs\manual-test.md | 6BE5F8E145D3C899B45388D4E762988EFA0BDD41C433E90B7CC699E1535C4FAE |
| F:\Developer\AI\vibe\agent_codex\ExplorerTabHost\docs\v0.2-test.md | 0F3D289F8BB7CB84DC70C62260CBF1BFF2DD8129FAA21F8A2C6287E15896EADA |
| F:\Developer\AI\vibe\agent_codex\ExplorerTabHost\dist\ExplorerTabHost.exe | 63A1A7C1A7AE65FC50B1422B32DB533EE79AB3A960D4A271A033E4B9FBC3155F |
| F:\Developer\AI\vibe\agent_codex\ExplorerTabHost\dist\v0.2.0\ExplorerTabHost.exe | 56DC4AF65707F5F24888C5F1B7664DF269440CB1F6EAC8FF90BF4FD35B17E788 |
| F:\Developer\AI\vibe\agent_codex\ExplorerTabHost\dist\v0.2.1\ExplorerTabHost.exe | 22A98587ADAD0B1130768AACD881D490602E2D68FD5C874F48DFB2FEFA0ABDC3 |

仅新建 0.3.0 发布目录，未启动 / 终止任何用户宿主，未覆盖旧发布、未复制人工回归清单、未清理用户 git 状态、未提交代码。

## 保留的证据边界

0.2.1 的隐藏结构恢复、非同步全树重绘请求、任务栏注册和有限只读延迟采样继续保留。AddTab 成功不等于真实任务栏 UI 必已恢复，RedrawWindow 接受请求不等于 paint 完成，host-calls-ms 不是用户可见切换时间。普通 / 最小化恢复使用非激活命令；最大化保留原 placement 语义，不保证绝不激活。

这仍是实验性窗口外壳；不承诺强杀、掉电或 Explorer 崩溃后救回已销毁的窗口。0.3.0 没有增加持久化会话、看门狗、自动接管、Ctrl+W 或跨宿主标签移动。
