# 会话接力 · ExplorerTabs

状态日期：2026-10-26；无进行中开发任务；待用户真机反馈与图标选择。
源码根 D:/Git/zgithub/explorer-tabs；分支 main；origin 关联 https://github.com/zouv/explorer-tabs；0.6.3 已发布到 GitHub Releases。
最后验证：2026-10-26 `package.bat` 实跑产出 0.6.3 dev zip + release unpack/zip；完整 build.ps1 安全测试通过；Release 资产已上传。

## 待用户反馈（直接在对话询问，不写人工回归文档）
- 新入口 `dist/release/unpack/ExplorerTabs.exe`（由 package.bat 产出）：先在旧宿主分离全部并正常退出，再手动运行；未自动关闭任何宿主或 Explorer。
- 0.6.1 灰框 / Ribbon 空行、自适应标签与多行 / 单行切换、跨行排序及 125% / 150% DPI 仍待真机复验；离屏检查不等于人工验收。
- 图标仍暂用 A；assets/icons/icon-choices.png 已更新 ExplorerTabs 名称，A / B / C 选择尚未确定。

## 接续入口与安全
- 0.6.3 发布、README 规范化与打包规则见 CHANGELOG；图谱 ARCHITECTURE §3.6 / §3.7，计划 ROADMAP P16 / P17 / P18。不要重做已完成的改名 / 界面 / 打包 / 发布工作。
- ProductIdentity 的 LegacyIdentity 是兼容契约，不是漏改：继续旧 LocalAppData 目录与 per-user Local mutex；新旧宿主互斥，不自动迁移 / 覆盖数据。BrandingTests 只读旧程序集生成的合成 JSON，并使用独立 Guid 测试用户。
- `scripts/build.ps1` 默认输出 `.runtime/build/ExplorerTabs-dev`（临时快照）；正式包用 `package.bat` / `sh manager.sh package`，版本取 Directory.Build.props。测试可追加 -NativeSmoke -CrossProcessSmoke -TabUiSmoke -CreationUiSmoke -WorkspaceUiSmoke。
- 打包只清 `dist/dev`、`dist/release`、`.runtime/staging`，历史 `dist/vX-release` 原样保留；不生成 setup.exe；目标包运行中会报错，绝不 taskkill 宿主。
- `assets/screenshots/` 是 README 引用的离屏截图（单行 / 多行），不含真实 Explorer 内容；详细离屏日志仍在 artifacts/ui/v0.6.2/。
- 所有旧 dist 原文件名 / 内容和 docs/manual-test.md / docs/v0.2-test.md 原文保留；历史 CHANGELOG / verification.md 中旧名是事实，不全局替换。
- 原 D:/Git/zgithub/explorer_tab_host 已整体重命名；旧 F:/Developer/AI/vibe/agent_codex/ExplorerTabHost 备份不改；GitHub 仓库为 zouv/explorer-tabs。
