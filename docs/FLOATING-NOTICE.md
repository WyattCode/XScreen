# 浮动提示修复（2026-10-20）

主窗口原来把全局 InfoBar 放在独立的 Auto 行中；打开提示后，该行高度和行距会减少下面的控制区空间。现在移除该行，将提示卡片作为 root 的最后一个子元素覆盖显示，靠右下排列在底部工具栏上方。

卡片可关闭，背景不透明并随深浅主题更新；宽度上限 760 DIP，高度上限 240 DIP，长文本在卡片内部滚动。替换当前提示时回到文字开头。提示不会抢走键盘焦点，关闭后整个浮层折叠。关闭错误卡片不修改故障状态，不启用控制，不读取或重发设置。全局提示适用；编辑器内的字段校验和独立对话框反馈保持原位。

## 离线验证

`tests/XScreen.ReviewChecks/FloatingNotice.cs` 已接入 `scripts/Test-XScreenOfflineUi.ps1`。同一 fake-only 宿主对未修改的 0.3.30 UI 复现了布局挤压与不可关闭的问题，基线报告有 72 个失败断言。首轮候选完成 6 个场景、362 个断言，失败 0：

- floating-notice：259，覆盖两个页面、深浅主题、1220×900 / 840×680 窗口、信息/成功/错误提示、长文本滚动与替换、关闭后故障保留、关闭后无拦截层。
- ui-alignment：51。
- hotkey-audit：14。
- osd-fault-name：3。
- reset-settings：14。
- ui-details：21。

本地证据：`analysis/evidence/floating-notice-verified/summary.json`；该目录包含模拟窗口截图和逐项断言。正式候选与回归宿主分别位于 `artifacts/XScreen-floating-notice-win-x64`、`artifacts/XScreen-regression-floating-notice`。

审查阶段只运行了注入模拟传输、renderOnly=true 的回归宿主，真实显示器读取和写入计数均为 0。未启动正式程序或执行安装，已发布的 0.3.30 文件未覆盖。上述结果证明本地候选的离线 UI 行为，不代表正式程序的实机验证；后续发布说明见 [0.3.31](RELEASE-0.3.31.md)。

## 二次审查与修复

再次审查发现并离线复现三处问题，基线报告为 `analysis/evidence/floating-notice-audit-baseline.json`：

1. **长消息关闭按钮不可见**：滚到底部时，原生关闭按钮与正文一起滚到卡片外，基线位置为卡片顶端上方 744 DIP。将实际关闭按钮移到滚动区外；原生按钮仅保留宽度，不接受命中、Tab 或辅助功能导航，防止文字与固定按钮重叠。
2. **键盘关闭后焦点跳到标题栏**：当焦点在浮层内时，关闭后移到可用的刷新按钮；若刷新不可用，则移到外观选择器。焦点原本在浮层外时不干预。支持在卡片内按 Escape 关闭，关闭不解除暂停或发送设置。
3. **次要告警覆盖硬件故障**：故障暂停且错误提示仍打开时，快捷键或托盘等较低优先级的提示不能替换故障原因和恢复说明。错误仍可关闭；成功读取后的原有清除逻辑保持有效。

二次审查的候选构建：`artifacts/XScreen-notice-audit-win-x64`；模拟回归宿主：`artifacts/XScreen-regression-notice-audit`。两者的应用和核心程序集哈希一致。完整 24 场景共 **720 个断言全部通过**，真实显示器读取/写入均为 0。浮动提示场景现含 292 个断言，包括按钮真实命中、关闭后焦点、Escape 路由、文字与按钮间距、提示打开期间缩放/换主题、故障优先级。

最终审查证据位于 `analysis/evidence/floating-notice-second-review/summary.json`，包含各场景报告和模拟截图。审查期间未启动正式程序或执行安装，也未覆盖已发布安装包；0.3.31 正式构建另有独立的完整回归与安装包验证。
