# 0.3.31.1 安装器二次审查

本次审查只检查源码、NSIS 模板与既有产物，并构建离线候选 **0.3.31.2**。审查阶段未提交或发布；后续发布说明见 [安装器修订2](RELEASE-0.3.31-r2.md)。未安装、卸载或启动正式程序，未修改实际安装登记或显示器设置。应用载荷仍为 0.3.31，523 个文件的哈希不变。附带 JSON 保留审查结束时的候选状态。

## 发现与修复

1. **旧卸载器可能误指向新目录。** Stock `initMultiUser` 会把 `$INSTDIR` 改写为当前注册目录。此前只比较改写后的目录与登记，因此旧位置或拷贝出的卸载器可能通过检查并删除当前安装。现在在 `un.onInit` 第一条操作捕获原始目标，初始化后及删除前都要求它与当前登记目录一致；不一致则在删除前停止。
2. **静默卸载缺少最终进程复查，且检查后仍可启动程序。** Stock 模板仅在非静默路径执行 section 内的进程检查。现在 `customRemoveFiles` 自行重验身份与占用，并在文件变更前创建应用使用的同名互斥体。覆盖升级也持有此句柄，直到 Setup/Uninstall 进程退出，防止同一会话的正常应用入口中途启动。取得锁时发现应用已启动或出现权限错误，均在删除前停止；自持句柄不被误认为外部占用。
3. **旧命令行参数可绕过数据保留策略。** 即使配置 `deleteAppDataOnUninstall: false`，原生模板仍接受 `--delete-app-data` 并递归删除 Roaming 下的应用目录。现在生成的卸载模板中完全移除该分支，普通与静默调用、包括旧参数，均保留用户数据。产品目录内仍只删除既定文件清单。

这些问题已在修改前用源码 VM 复现，报告为 `analysis/evidence/installer-r1-second-review-baseline-r2.json`：57 个用例中上述 3 项失败，其余通过。复现和修复验证均不执行原生安装器，也不触碰真实注册表或用户数据。

## 离线回归

- 静态验证 **554 项通过**：PE/NSIS CRC、安装器版本、完整载荷哈希、blockmap、发布元数据、实际参与编译的源码哈希、原始目录捕获顺序、删除范围与应用互斥体名称。
- 源码行为回归 **64 项通过**：原有目录、三个版本升级、快捷方式和数据保留，以及初始化重定向、延迟启动、创建锁时的竞争与拒绝访问、自持句柄、向导阶段不提前阻止程序、路径别名、大小写不同的数据删除参数。
- 构建仍静态提取卸载器，不执行生成器、Setup、Uninstall 或 XScreen。源码 VM 的 Win32、Shell、注册表和文件操作边界全部为模拟。

报告为 `installer-verification-0.3.31-r2.json`、`installer-behavior-verification-0.3.31-r2.json` 与 `installer-second-review-verification-0.3.31-r2.json`。候选安装器位于 `release/XScreen Setup 0.3.31.exe`，文件版本 **0.3.31.2**；对应 SHA256 见 `release/SHA256SUMS.txt`。上一版产物保留于 `artifacts/release-archives/release-0.3.31-r1-preserved-before-r2-review`。

真实安装、卸载、锁竞争和系统 Shell 行为未执行验证；源码和静态结果不代表实机验收。已经复制到其他位置的旧版卸载器不会被这次源码修改自动修复。

## API 依据

- [NSIS：$INSTDIR 与卸载器原始目录](https://nsis.sourceforge.io/Docs/Chapter4.html#varinstdir)
- [Windows：CreateMutexW 的已存在对象与句柄语义](https://learn.microsoft.com/en-us/windows/win32/api/synchapi/nf-synchapi-createmutexw)
