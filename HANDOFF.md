# 会话接力 · ExplorerTabHost

状态日期：2026-10-26；无进行中开发任务；待用户复验与图标选择。
源码根 D:/Git/zgithub/explorer_tab_host；分支 master；用户已明确授权将 0.5.x / 0.6.x 功能和 0.6.1 修复提交，当前提交号用 git log -1 查看；未推送远端。

## 待用户反馈（直接在对话询问，不写人工回归文档）
- 新版入口 dist/v0.6.1-release/ExplorerTabHost.exe：先在旧宿主分离全部并正常退出，再手动运行。
- 灰框：反复切后台 / 前台、鼠标移入窗边是否仍出现；拖动 / 最大化 / 边缘缩放 / 最小化是否正常。
- 空标题区：Ribbon 上方空行是否去除；切标签、展开 / 收起 Ribbon 是否不裁正常功能区；分离后独立 Explorer 是否完整恢复。
- 自适应标签：短名 / 长名、重命名、多行 / 单行切换与跨行排序是否正常；125% / 150% DPI 是否正确。
- 前版延迟呈现、新建失败 / 取消、菜单等体验仍按已收反馈记录，不将 fake 测试等同真机验收。
- assets/icons/icon-choices.png 的 A / B / C 款式选择；当前 exe / Form 暂用 A。

## 接续入口 / 安全
- 本轮发布详情与 725 项检查见 CHANGELOG 0.6.1；图谱 ARCHITECTURE §2 / §3.4–3.5；总体状态 ROADMAP P15。不要重新开发已交付功能。
- 灰框：MainForm.Chrome.cs 的 WndProc / InvalidateChrome；空区：NativeWindowApi.cs 的 MeasureEmbeddedTitleInset / EmbeddedViewportBounds / Layout。未知 Ribbon 布局零偏移，不改子控件或恢复快照。
- 实测依据为用户原日志：Explorer client 与 window 等大，直接全宽 Ribbon dock 位于 root.Top+31，且包含 command bar。新日志 embedded.chrome 记录 titleInset；诊断包追加 Host UI 区域 / DPI / 滚动状态。若真机仍异常，先看紧凑日志而非输出巨量 child dump。
- 构建默认 dist/v0.6.1-release；同名发布正在运行不得覆盖，后续修复用新版本目录。不自动运行真实 Explorer / 交互宿主 / 全局钩子，不操作用户窗口。
- 测试入口 scripts/build.ps1 -NativeSmoke -CrossProcessSmoke -TabUiSmoke -CreationUiSmoke -WorkspaceUiSmoke；--workspace-ui-smoke 包含工作空间 / 窗框 UI，--native-smoke 包含自注册隐藏 Ribbon HWND。
- artifacts/ui/v0.6.1/ 保留构建日志与单行 / 多行离屏预览；截图不含真实 Explorer 内容。
- 保留所有旧发布、用户 docs/manual-test.md / docs/v0.2-test.md、settings / workspace / recovery；不新增人工回归文档。
