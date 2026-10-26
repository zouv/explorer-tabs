# 会话接力 · ExplorerTabs

状态日期：2026-10-26；无进行中开发任务；待用户真机反馈与图标选择。
源码根 D:/Git/zgithub/explorer-tabs；分支 master；用户已明确授权提交 0.6.2 改名工作；当前提交号用 git log -1 查看；未推送远端。

## 待用户反馈（直接在对话询问，不写人工回归文档）
- 新入口 dist/v0.6.2-release/ExplorerTabs.exe：先在旧宿主分离全部并正常退出，再手动运行；未自动关闭任何宿主或 Explorer。
- 0.6.1 灰框 / Ribbon 空行、自适应标签与多行 / 单行切换、跨行排序及 125% / 150% DPI 仍待真机复验；离屏检查不等于人工验收。
- 图标仍暂用 A；assets/icons/icon-choices.png 已更新 ExplorerTabs 名称，A / B / C 选择尚未确定。

## 接续入口与安全
- 0.6.2 发布与 761 项安全检查见 CHANGELOG；图谱 ARCHITECTURE §3.6，计划 ROADMAP P16。不要重做已完成的改名 / 界面功能。
- ProductIdentity 的 LegacyIdentity 是兼容契约，不是漏改：继续旧 LocalAppData 目录与 per-user Local mutex；新旧宿主互斥，不自动迁移 / 覆盖数据。BrandingTests 只读旧程序集生成的合成 JSON，并使用独立 Guid 测试用户。
- 默认 scripts/build.ps1 输出 dist/v0.6.2-release；正在运行的同名包不能覆盖，后续使用新版本目录。测试可追加 -NativeSmoke -CrossProcessSmoke -TabUiSmoke -CreationUiSmoke -WorkspaceUiSmoke。
- artifacts/ui/v0.6.2/ 保存构建日志、单行 / 多行 720 / 1100 宽与设置页离屏预览；图像不含真实 Explorer 内容。
- 所有旧 dist 原文件名 / 内容和 docs/manual-test.md / docs/v0.2-test.md 原文保留；历史 CHANGELOG / verification.md 中旧名是事实，不全局替换。
- 原 D:/Git/zgithub/explorer_tab_host 已整体重命名；旧 F:/Developer/AI/vibe/agent_codex/ExplorerTabHost 备份不改，不自动改 Git 远端 / GitHub 仓库名。
