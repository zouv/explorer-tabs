# 会话接力 · ExplorerTabHost

状态日期：2026-10-25；源码分支 master（提交记录以 git log 为准）。
源码根：D:/Git/zgithub/explorer_tab_host；旧根 F:/Developer/AI/vibe/agent_codex/ExplorerTabHost 为只读备份。

## 当前状态
无进行中任务。0.4.1 已完成安全自动验证及用户真实 Shell 验收；验收记录归 CHANGELOG，完成度归 ROADMAP。
下一阶段尚未开工；建议优先会话保存 / 用户显式恢复，标签组后续考虑。

## 下一次开工第一动作
确认用户的下一阶段需求，再按 ARCHITECTURE 作用域路由定位代码，并在本文件写下实施计划；不要继续已完结的 0.4.1 回归排查。

## 最后验证状态
本轮重新构建 Release：0 警告 / 0 错误；382 项安全自动检查通过（268 unit/原生封送 +44自建透明窗口 +7跨进程helper +29隐藏标签UI +34隐藏创建/加入/设置UI）。
日志：artifacts/commit-verification-d91f64393e2f4f128a9d833d244e7a2f.log（忽略入库）。
未自动创建真实 Explorer、启动交互宿主、安装钩子或操作用户窗口；用户另外确认实际新建 / Ctrl+T、复制与加入窗口正常。

## 运行 / 复现
手动运行：dist/v0.4.1-release/ExplorerTabHost.exe。
完整安全验证：powershell -ExecutionPolicy Bypass -File scripts/build.ps1 -NativeSmoke -CrossProcessSmoke -TabUiSmoke -CreationUiSmoke。
运行中发布路径不可确认时用 -OutputDirectory 指定一个全新 dist 子目录，不绕过覆盖保护或终止进程。
模板机械检查：bash manager.sh check-docs。

## 持续约束
每标签一真实常驻 Explorer；匹配自己的 Shell view，不猜公共宿主 / 标题 / 最新窗口；正常退出先分离。
保留恢复顺序、旧版本及用户已填 docs/manual-test.md、docs/v0.2-test.md；仅在用户明确要求时本地提交，不自动推送。
