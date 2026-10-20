# 托盘提示与小图标修复

用户提供的截图中，XScreen 托盘悬停显示空白提示，图标的有效尺寸也比相邻图标偏小、偏软。

## 根因和修复

- 原来启用 `NOTIFYICON_VERSION_4`，但只设置 `NIF_MESSAGE | NIF_ICON | NIF_TIP`，缺少明确启用标准提示的 `NIF_SHOWTIP`。新注册载荷固定提示为 `XScreen`，每次 Explorer 重建托盘也保留这些标志并重新协商版本。对应规则见 [Microsoft NOTIFYICONDATAW](https://learn.microsoft.com/windows/win32/api/shellapi/ns-shellapi-notifyicondataw)。
- 原来固定 `LoadImage(...,32,32,...)`，在100%、125%、150%缩放下还需要系统再次缩放。现取任务栏窗口 DPI 和 `SM_CXSMICON/SM_CYSMICON`，从独立托盘 ICO 请求对应物理尺寸；DPI/显示设置通知触发必要的图标刷新。依据 [GetSystemMetricsForDpi](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getsystemmetricsfordpi)。
- 原图在32像素档有1像素外侧留白。增加独立的 `XScreen.Tray.ico`，以矢量几何分别栅格化16、20、24、28、32、36、40、48、56、64、96、128、256像素。直边对齐像素，清理外围留白和半透明直边，白色描边按尺寸调整。常用Windows缩放请求有对应尺寸，自定义缩放仍由系统选择最合适的帧。
- 通过独立服务管理注册、DPI更新、Explorer恢复及图标句柄所有权。更新失败保留旧图；临时新句柄失败后释放，共享系统回退图标不销毁，退出后迟到通知不能重新添加。

## 验证边界

`tests/XScreen.TrayChecks` 只编译托盘载荷/生命周期代码，无显示器传输。Shell注册使用假接口；资源检查调用 Windows 的 LoadImage/GetIconInfo/GetDIBits 读取本地 ICO，不添加真实托盘图标。覆盖提示标志、UTF-16实际封送、DPI选帧、Explorer重复恢复、注册/更新失败、退出清理，以及13个尺寸的实际Windows解码、边界透明度和颜色。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Build-XScreenTrayIcon.ps1
dotnet run --project tests/XScreen.TrayChecks -c Release -- analysis/evidence/unique-tray-report.json src/XScreen.App/Assets/XScreen.Tray.ico
```

正式应用仅完成编译和资源核对，未启动、安装或替换正在运行的实例，没有读写显示器。悬停文字、Shell最终缩放和真实显示效果仍需用户手动启动后确认。像素对比图是资源渲染预览，不是真实系统托盘截图。初次修复构建保留在 `artifacts/XScreen-tray-fix-win-x64`；二次审查后的构建位于 `artifacts/XScreen-tray-review2-win-x64`，未改动既有发布资产。

## 二次审查（2026-10-20）

先补充故障用例再修改代码，记录了原实现的5项失败和随后发现的2项 DPI 失败。修复后的139项检查全部通过，含13个尺寸的 Windows 原生图标解码；正式程序未启动，真实 Shell 注册写入和显示器读写均为0。

修复范围：

- 首次注册失败后缺少恢复入口。新增仅用于 Shell 注册的5秒恢复定时器，成功后停止；显示设置通知也可触发恢复。不调用显示器刷新、连接或设置接口。
- 重复添加失败会误判托盘不可用。添加失败后先尝试修改已有记录，再决定是否进入恢复状态；每次重新添加后重新协商版本4。
- 恢复时原图标文件临时不可读会换成通用图标。优先重用已持有的图标，保留其所有权和实际尺寸，不重复销毁。
- 注册接口抛异常时临时图标泄漏。统一在 finally 中释放未采用的候选句柄；注册、DPI 更新、版本协商和日志失败均有隔离。
- 删除接口抛异常会跳过退出清理。先阻止后续恢复，再尽力删除 Shell 记录，最后释放自有图标；不确定状态也尝试清理登记，定时器和窗口子类在退出时移除。原生窗口初始化失败时主窗口仍可用。
- DPI 修改失败后仍沿用“可用”状态，不能确认系统记录是否存在。现在尝试重新添加；修改和添加均失败时进入待恢复状态，保留图标资源但不允许依赖一个未确认的托盘入口隐藏窗口。

额外回归覆盖：恢复过程中退出、DPI 更新过程中退出、退出后迟到的定时通知、重复通知重入、恢复连续失败、共享回退图标替换，以及日志写入失败。真实托盘悬停、定时恢复和跨显示器最终效果仍需用户手动验收。
