# XScreen

适用于 LG 32GX870A 的 Windows 显示器控制工具，使用 C#、WinUI 3 和 Windows App SDK。完整窗口为主，托盘为快捷入口。

## 下载与运行

从本仓库 [Releases](https://github.com/WyattCode/XScreen/releases) 下载 **`XScreen-0.3.29-Setup-x64.exe`**，按向导安装。也保留 ZIP 便携版，完整解压后运行 `XScreen.exe`。这是私有仓库，仅授权账号能访问下载。

当前版本 **0.3.29**。本地构建可运行根目录的 `运行XScreen.cmd`。启动只读取状态，不自动调节；不要直接运行 ZIP 内的 EXE。

## 功能

- 快速控制：亮度、显示器音量、锐度；支持拖动、数值输入和撤销上次调节。
- 画面与色彩：画面模式、色温、黑色稳定器，逐项手动应用并回读。对比度仅显示读取值。
- 两个页面共用“重置设置”：先查看六项目标值和影响范围，确认后执行。这是应用预设，不是显示器恢复出厂。
- 深色、浅色、跟随 Windows；窗口大小和最大化状态记忆。
- 原生托盘菜单；可自定义全局快捷键，收起到托盘仍可使用。

全局快捷键位于“更多操作 → 全局快捷键”，可修改、清除或关闭。默认均按住 **Ctrl + Alt + Shift**：

| 操作 | 增加10 | 降低10 |
| --- | --- | --- |
| 亮度 | ↑ | ↓ |
| 显示器音量 | → | ← |
| 锐度 | Page Up | Page Down |

按住不连发。快捷键冲突会提示，打开快捷键配置时暂停注册。窗口大小和快捷键偏好保存在 `%LOCALAPPDATA%\XScreen`，跨版本沿用。

## 控制边界

型号支持和物理效果验收仅限已有证据；新增画面控制仍需用户自行验收。启动、刷新、打开设置和切换页面均不会写入显示器。调节串行执行，逐次回读；异常暂停，不自动重试、回滚或恢复。读写相符不等于完整硬件恢复证明。

对比度禁止写入；Adaptive Sync、画面比例、Dual Mode 继续禁用。远程桌面可能无法访问实体显示器，程序会显示对应状态。

## 构建

需要 Windows x64、.NET 10 SDK，可访问 NuGet。应用为自包含的 WinUI 3 非打包程序，使用项目文件锁定的 Windows App SDK 版本。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Build-XScreen.ps1
```

构建产物在 `artifacts/XScreen-0.3.29-win-x64`。发布包不包含日志、个人偏好、调试符号、原版LG程序、反编译文件或下载缓存。

## 离线回归

所有测试使用假显示器，不自动调整真实显示器。ReviewChecks 使用刚发布的实际程序集，执行脚本核对应用和 Core 哈希。

```powershell
dotnet run --project tests/XScreen.Acceptance -c Release
dotnet run --project tests/XScreen.OfflineSafety -c Release
dotnet run --project tests/XScreen.InteractionChecks -c Release
dotnet run --project tests/XScreen.DiagnosticsChecks -c Release -- artifacts/diagnostics-checks.json
dotnet run --project tests/XScreen.OsdChecks -c Release -- . artifacts/osd-checks.json
dotnet publish tests/XScreen.ReviewChecks -c Release -o artifacts/XScreen-regression-0.3.29 -p:NuGetAudit=false
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Test-XScreenOfflineUi.ps1 -RunName local-review
```

UI回归会创建自己的测试窗口；原生热键检查仅使用专用测试组合和假设备窗口，不发送物理快捷键，不与实时程序共用显示器传输。证据路径和运行名称必须使用新值，避免覆盖旧结果。`tests/Fixtures/osd-reference.json` 只保存已整理的参数事实，协议测试无需原版安装器或反编译源码。

## 打包

先完成构建及离线检查，再执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Package-XScreen.ps1
```

脚本生成新 staging 目录、ZIP、SHA256 和成员清单；只复制已发布程序和产品说明，不运行程序。版本输出已存在时拒绝覆盖。

详见 [版本修复说明](docs/RELEASE-0.3.29.md) 和 [更新记录](CHANGELOG.md)。


EXE安装器默认安装到当前用户的 `%LOCALAPPDATA%\Programs\XScreen`，提供开始菜单、可选桌面快捷方式和Windows卸载入口。安装/卸载前需从托盘退出XScreen；不会自动终止进程或启动程序。卸载仅移除安装清单中的文件，保留设置、日志和其他用户文件。详见[安装器构建与验证](docs/INSTALLER.md)。
