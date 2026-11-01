# XScreen 交接文档

给接手的人（或下一个会话）看的现状说明。每完成一轮更新一次。

**怎么读**：第一到五节是接手就要知道的（现状、环境、怎么跑、代码在哪、当前异常）；第六节是不能擅自放开的边界；第七节是下一步；第八节是坑；第九节是本轮核实记录。项目此前没有交接文档，这份是 2026-11-01 补的第一份，内容全部来自仓库、发布证据和本机实测，不是回忆。

> 位置说明：根目录被 `.gitignore` 的 `/*` 挡住（只白名单放行特定文件），所以本文放在 `docs/` 下，可正常提交。

## 一、当前状态（2026-11-01 核实）

- **这是什么**：LG 32GX870A 的 Windows 显示器控制工具，C# + WinUI 3 + Windows App SDK 2.2.0，.NET 10 自包含 x64 非打包程序。完整窗口为主，托盘为快捷入口。
- **版本**：应用 **0.3.31.0**，安装器文件版本 **0.3.31.2**（第三轮安装器修订）。
- **最新发布**：`v0.3.31-r2`，2026-10-20。标签、HEAD、`origin/main` 三者同一提交 `508b96c`，工作区干净，**没有未提交改动、没有未合并分支**。
- **仓库**：`https://github.com/WyattCode/XScreen`，**私有**。star / fork / issue 均为 0，全部 8 个提交都在 2026-10-20 一天内完成。跟踪文件 123 个。
- **发布资产**：每个版本 6 个（Setup.exe、blockmap、latest.yml、ZIP 便携版、逐文件清单、SHA256SUMS.txt）。本机 `release/` 下 Setup 的 SHA256 已与 `SHA256SUMS.txt` 及 `docs/release-verification-0.3.31-r2.json` 三方对齐。
- **本机安装**：`D:\Apps\XScreen`，`XScreen.dll` 版本 `0.3.31+87179e2`，与 `artifacts/XScreen-0.3.31-win-x64/XScreen.dll` **字节相同**，但目录内容与发布清单差一个文件、也查不到卸载登记，见 5.2。
- **最后一次真实运行**：2026-10-29 20:00（`runtime-data/app.log`），启动只读，7 次读取 / 0 次写入。最后一次真实硬件写入是 2026-10-28 21:03，亮度 100→83→100，回读相符。
- **本轮没做的事**：没有构建、没有运行测试、没有启动程序、没有碰显示器、没有执行安装器。**这台机器现在也构建不了**（见第二节）。

## 二、环境：先看这一节

**本机没有 .NET SDK，只有运行时 8.0.21。**

```
C:\Program Files\dotnet\sdk        不存在
dotnet --list-sdks                 空
dotnet --list-runtimes             Microsoft.NETCore.App 8.0.21
```

项目目标是 `net10.0-windows10.0.26100.0`，所以 `Build-XScreen.ps1`、`dotnet run --project tests/...`、`dotnet publish` **现在全部会失败**。要继续开发必须先装 .NET 10 SDK（以及 Windows App SDK 2.2.0 所需的 workload，`--ignore-failed-sources` 已写在构建脚本里）。

其余可用：

| 工具 | 状态 | 用途 |
| --- | --- | --- |
| Node 22.22.2 | 可用 | `npm run dist`、electron-builder |
| electron-builder 26.15.3 | `node_modules` 已装 | 只做 NSIS 包装，不含 Electron 运行时 |
| Python 3.13.14 | 可用 | 安装器检查脚本 |
| `pefile` | **未安装** | `Test-XScreenInstaller.py` 依赖它，需 `pip install pefile` |
| `gh` | 可用且已登录 WyattCode | 读发布、拉仓库数据 |

## 三、怎么构建、怎么验

完整顺序（README 里有同样一段，这里是带上下文的版本）：

```powershell
# 1. 构建（产物 artifacts/XScreen-<version>-win-x64）
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Build-XScreen.ps1

# 2. 离线回归（全部使用假显示器，不碰真实设备）
dotnet run --project tests/XScreen.Acceptance -c Release
dotnet run --project tests/XScreen.OfflineSafety -c Release
dotnet run --project tests/XScreen.InteractionChecks -c Release
dotnet run --project tests/XScreen.DiagnosticsChecks -c Release -- artifacts/diagnostics-checks.json
dotnet run --project tests/XScreen.OsdChecks -c Release -- . artifacts/osd-checks.json
dotnet publish tests/XScreen.ReviewChecks -c Release -o artifacts/XScreen-regression-<version> -p:NuGetAudit=false
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Test-XScreenOfflineUi.ps1 -RunName <新名字>

# 3. 打包（ZIP + 逐文件清单 + SHA256，拒绝覆盖已存在的版本目录）
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Package-XScreen.ps1

# 4. 安装器（需要 artifacts/releases/v<version>/ 已存在）
npm ci --ignore-scripts
npm run dist

# 5. 安装器检查
python scripts/Test-XScreenInstaller.py --report artifacts/installer-checks.json
python scripts/Test-XScreenInstallerBehavior.py --report artifacts/installer-behavior-checks.json
```

**两个硬性前置条件**，不满足会直接抛错：

1. `Package-XScreen.ps1` 要求 `analysis/evidence/local-review/summary.json` 里至少 24 个场景、含 `floating-notice`、且全部 `exitCode=0 / failures=0 / nativeReads=0 / nativeWrites=0`。用 `-EvidenceDirectory` 指向别的通过目录即可。注意 `analysis/` 整个被 git 忽略，**换机器就没有这份证据**。
2. `Package-XScreen.ps1` 还会比对 `artifacts/XScreen-<version>-win-x64` 与 `artifacts/XScreen-regression-<version>` 里 `XScreen.dll` / `XScreen.Core.dll` 的哈希——回归宿主必须用同一批程序集。

证据路径和 `-RunName` 每次都要用新值，避免覆盖旧结果。

## 四、代码在哪

跟踪 26 个 `.cs`（3060 行）；本机另有未跟踪的 `src/XScreen.Preview`（4 个文件，748 行）。

- `src/XScreen.Core/` —— 与 UI 无关的核心：
  - `WindowsMonitorTransport.cs`：DDC/CI 原生读写，走 `GetPhysicalMonitors` + `SetVCPFeature`，**不依赖 LG 的 DLL**。
  - `MonitorController.cs`：串行队列、写后回读、故障锁存。**所有调节都走这一条路径**。
  - `MonitorSafetyPolicy.cs`：哪些 VCP 码允许写。对比度 `0x12` 在这里被永久拒绝。
  - `OsdProtocol.cs`、`AppReset.cs`：OSD 协议常量与共享重置的目标值。
- `src/XScreen.App/` —— WinUI 3 外壳：
  - `MainWindow.cs` / `MainWindowNotice.cs`（浮动提示）/ `MainWindowHotkeys.cs`
  - `OsdControlsView.cs`（画面与色彩页）、`OsdComparisonView.cs`、`ResetSettingsView.cs`、`HotkeySettingsView.cs`
  - `NotificationIcon.cs` + `TrayMenuWindow.cs`：原生托盘（不用 Shell 通知区图标 API 的高层封装）
  - `GlobalHotkeys.cs`（`RegisterHotKey` + `MOD_NOREPEAT`）、`Preferences.cs`、`WindowStateStore.cs`、`NativeWindow.cs`
  - `Program.cs`：入口。命令行只认 `--render-only` / `--capture-once` / `--show-tray-menu` 三种离线模式，**没有自动硬件测试模式**，其它参数直接抛异常。
  - `TracingMonitorTransport.cs`：包一层诊断记录，不增加 I/O。
- `tests/` —— 8 个工程，`XScreen.ReviewChecks` 是最大的一块（2298 行），对**已发布的实际程序集**跑断言。
- `packaging/` —— NSIS 片段（`installer.nsh`、`functions.nsh`、`per-user.nsh`、`upgrade.nsh`、`no-start.nsh`）。`generated-*.nsh` 由构建脚本生成，不进 git。

## 五、必须知道的现状与异常

### 5.1 安装器从未被执行过

这是整个项目最大的未验证面。`docs/release-verification-0.3.31-r2.json` 里写得很清楚：

```json
"installerExecuted": false, "uninstallerExecuted": false,
"formalApplicationStarted": false, "realRegistryWrites": 0
```

554 项静态检查 + 64 项行为回归都是**离线**的：静态检查验 NSIS CRC / PE / 载荷哈希 / blockmap；行为回归用一个有限的 NSIS 源码解释器跑宏和分支，Win32、Shell、注册表、文件系统全部是模拟。真实向导、UAC、Shell 快捷方式、锁竞争、注册表写入**一次都没跑过**。安装器也未代码签名。

### 5.2 本机安装有三处对不上（2026-11-01 实测）

**(a) 少一个文件。** 逐文件比对 `D:\Apps\XScreen` 与 `XScreen-0.3.31-files.json`：**522 / 523 哈希一致，缺 `Microsoft.Windows.Workloads.Resources_ec.dll`（174944 字节）**。该文件在 `artifacts/releases/v0.3.31/stage/XScreen/` 里是存在的，ZIP 里也有。原因不明——可能是安装时被拦截，也可能是拷贝进来的。不排除影响 WinAppSDK 运行时资源加载。

**(b) 查不到卸载登记。** 三处 Uninstall 注册表位置（HKCU、HKLM、WOW6432Node，共 111 条 DisplayName 条目）**没有任何一条含 "XScreen"**，`HKCU\Software\XScreen` 也不存在。但安装目录里有 `Uninstall XScreen.exe`（哈希与 `release/static-build.json` 的 `uninstallerSha256` 完全一致）、`uninstallerIcon.ico`，开始菜单 `%APPDATA%\...\Programs\XScreen.lnk` 指向 `D:\Apps\XScreen\XScreen.exe`。

**(c) 时间线也对不上。** 安装目录里绝大部分文件（含 `Uninstall XScreen.exe`）的写入时间是 **2026-10-20 21:04**——那是 `release/` 下 r2 安装包的构建时刻；但目录本身的创建时间是 **10-20 01:27**（0.3.29 时代）。而 r2 的验证记录写着 `installerExecuted: false`。也就是说这个目录**不是通过跑 r2 安装器得到的**，最可能是一次手动拷贝/覆盖叠加在旧目录上。

结论要说得保守：**能确认的是这台机器上 `D:\Apps\XScreen` 有完整的 522 个程序文件、开始菜单快捷方式指向它、但三处注册表都没有卸载登记**。至于"为什么会这样"，需要真跑一次安装器才能回答（见第七节第 2 项）。在查清之前不要把这个状态当成"安装器坏了"的证据，也不要当成"安装器没问题"的证据。

### 5.3 仓库卫生：三处不一致

- `XScreen.slnx` 只含 6 个工程（App、Core、Acceptance、OfflineSafety、OsdChecks、TrayChecks），**缺 `InteractionChecks`、`DiagnosticsChecks`、`ReviewChecks`、`PreviewChecks`**。而 README 明确要求跑前三个。`dotnet build XScreen.slnx` 不会构建它们。
- 反过来，README 没提 `TrayChecks`（它也没在 README 的回归清单里）。
- `src/XScreen.Preview` 和 `tests/XScreen.PreviewChecks` **未被 git 跟踪**（`.gitignore` 的 `/src/*` 只白名单了 App 和 Core）。也就是说 clone 下来没有 0.2.0 那个历史 mock，`预览新界面.cmd` 会指向一个不存在的目录。

### 5.4 版本号散落在 9 个地方

改版本时要一起改，漏了基本都会在构建或检查阶段报错（有断言兜着，不会静默出错）：

`package.json`、`src/XScreen.App/XScreen.App.csproj`（`<Version>`）、`electron-builder.yml`（`buildVersion`，四段）、`scripts/Build-XScreen.ps1`（默认输出目录）、`scripts/Package-XScreen.ps1`（`-Version`）、`scripts/Build-XScreenInstaller.ps1`（`-Version`）、`scripts/Test-XScreenOfflineUi.ps1`（`-Version`）、`tests/XScreen.ReviewChecks/XScreen.ReviewChecks.csproj`（`XScreenVersion`）、`运行XScreen.cmd`（路径）。

另有一处**没有断言**：`src/XScreen.App/Program.cs:28` 的启动日志里硬编码了 `"Starting XScreen 0.3.31 LIVE"`，改版本时容易漏。

## 六、产品边界（不要擅自放开）

来自 `PRODUCT.md`，是用户逐条明确授权过的：

- **可写七项**：亮度 `0x10`、显示器音量 `0x62`、锐度 `0x87`、画面模式 `0x14`、色温 `0x15`、黑色稳定器 `0xF9`、对比度 `0x12`（**仅读**）。
- **对比度禁止写入**：0.3.23 因为再次写失败，用户明确要求移除调节路径，UI、控制器、原生传输三处都拦。读取和同步仍允许。
- **继续禁用**：Adaptive Sync、画面比例、Dual Mode。
- **重置**是全应用预设（亮度 100、音量 50、锐度 50、Gamer1/raw45、Warm/raw3、black50/encoded10），**不是显示器恢复出厂**。确认前先显示六项目标值。
- **不自动做任何硬件操作**：不轮询、不自动重试、不回滚、不在启动时写入、不把"读写相符"说成"完整硬件恢复"。
- 故障后锁存暂停，只有 `0xC026258B` 这类**写入前**的校验错误允许用户手动"重新读取"恢复。

改这些之前先读 `PRODUCT.md` 的日期条目，每一条都记了是哪天、因为什么用户授权的。

## 七、下一步

按优先级排：

1. **跑完整离线回归**（不是只跑我验证过的那几个场景）。SDK 已装（见 10.6），第十节那批改动**已编译、已跑过 5 个场景并做了真实启动验证**，但没跑全套。
2. **真实安装/卸载验收**。跑一次 `release/XScreen Setup 0.3.31.exe`，重点确认 5.2 的三个异常，尤其是卸载登记到底写没写。这是 0.3.31 三轮安装器修订**唯一没验过的部分**，也是 r2 三个修复（旧卸载器目录重定向、删除前取互斥体、`--delete-app-data` 分支移除）唯一的实机证明机会。
3. **代码签名**。安装器至今未签，SmartScreen 会拦。X-SSH 那边有 `docs/signing.md` 记过选型（Azure Artifact Signing / 商业 OV），可以复用那套结论。
4. **清理仓库卫生**（5.3）：补 `XScreen.slnx` 缺的工程，决定 `XScreen.Preview` 是纳入跟踪还是删掉 `预览新界面.cmd`。
5. **`MainWindow` 的其余状态条件尚未收敛**。第十节只把三处逐字相同的守卫收成一个属性；`CanAdjust`（11 个条件）、`CanOpenReset`、`UpdateRefresh`、`EnableControls` 仍是手写组合，尚未统一。
6. **功能层面没有已授权的待做项**。想加新控制（比如色域、HDR）必须先问用户要授权，并按现有方式走 `MonitorSafetyPolicy` + 串行回读 + 离线回归。

## 八、坑

- **构建产物和回归宿主必须是同一批程序集**。`Package-XScreen.ps1` 会比对哈希，不一致直接拒绝打包。改完代码要重新 `publish` 回归宿主，不能只重编主程序。
- **`analysis/` 整个被 git 忽略**。所有 UI 证据、`summary.json`、旧安装器基线都在里面。换机器后 `Package-XScreen.ps1` 会因为没有证据而失败——不是脚本坏了。
- **不要直接跑 ZIP 里的 EXE**。README 明确写了。跑就用 `运行XScreen.cmd` 或已安装副本。
- **不要为了测试自动改显示器**。历史上出过黑屏事故（`analysis/INCIDENT-BLACK-SCREEN-20261017.md`）。所有回归都用假传输。
- **`--prepackaged` 是这套安装器的关键**。它让 electron-builder 原样包装已发布的 WinUI 文件清单，不下载 Electron、不改 EXE 字节。改动 payload 就得重新走 `Package-XScreen.ps1`。
- **卸载器是静态提取的**，不是执行生成器得来的（`UninstallerReader`）。`release/uninstaller-evidence.bin` 只给 CRC 检查用，不是给用户的卸载入口。
- **已经复制到别处的旧卸载器不会因为新版修复而自动变好**。用当前安装目录里的卸载入口。
- **注册表在这次会话里读得到、但 `reg.exe` 被安全策略拦了**。用 PowerShell 的 `Get-ItemProperty` 而不是 `reg query`。

## 九、本轮核实记录（2026-11-01）

只读。没有构建、没有测试、没有启动程序。

| 核实项 | 方法 | 结果 |
| --- | --- | --- |
| 交接文档是否存在 | 全项目搜 `HANDOVER/HANDOFF/交接/AGENTS/CLAUDE/STATUS/TODO/SESSION`，加 `git log --diff-filter=DR` | 从未有过，历史里也没删过 |
| 版本与发布对齐 | `git rev-parse HEAD` / `origin/main` / `git describe --tags` | 三者同为 `508b96c`，在工作区干净 |
| 发布包完整性 | `sha256sum` 对比 `release/SHA256SUMS.txt` 与 `docs/release-verification-0.3.31-r2.json` | Setup 与 latest.yml 哈希三方一致 |
| 已安装副本 | 逐文件哈希比对 `D:\Apps\XScreen` 与 `XScreen-0.3.31-files.json` | 522/523 一致，缺 `Resources_ec.dll` |
| 安装来源 | 对比安装目录创建时间（10-20 01:27）与文件 mtime（10-20 21:04） | 不是跑 r2 安装器得到的 |
| 卸载登记 | 三处 Uninstall 注册表共 111 条 | 无 XScreen 条目 |
| 卸载器来源 | `Uninstall XScreen.exe` 哈希 vs `release/static-build.json` | 一致（`f6069c79…`） |
| 构建能力 | `dotnet --list-sdks` / `--list-runtimes` | 无 SDK，仅运行时 8.0.21 |
| 测试依赖 | `python -c "import pefile"` | 未安装 |
| 发布证据数字 | 读 `docs/release-verification-0.3.31.json` 与 r2 | 见下 |

0.3.31 应用侧：24 个 UI 场景 / 720 项检查、托盘 139、协议 187、交互 25、诊断 23、安全 19、验收通过，失败 0，真实显示器读写 0/0。
0.3.31 r2 安装器侧：静态 554 项、行为 64 项，失败 0，安装器/卸载器/正式程序均未执行。

## 十、本轮代码改动（2026-11-01 晚，已编译验证）

通读源码后的四项收拾。改完当时机器上只有 .NET 8 运行时、没有 SDK，所以先写了代码；随后装上 .NET 10 SDK（见 10.6）做了完整验证，结论见 10.7。

### 10.1 删掉 `Preferences.cs`（死代码）

`Preferences` 与 `SavedCombination` 在整个项目里**零引用**（`analysis/` 下的历史副本不算，那些不参与编译）。它实现的是"保存 6 组亮度/音量组合"，0.1.0 时有 UI 入口也真实测过，后来界面重做时入口被删、文件留下了。

更麻烦的是它写文件的位置：`Path.Combine(AppLog.Folder, "preferences.json")`，而 `AppLog.Folder` 是 `AppContext.BaseDirectory\runtime-data`，即**安装目录内**。窗口状态和快捷键都老实放在 `%LOCALAPPDATA%\XScreen\`，只有它一处例外；目录不可写时会抛异常。

处理方式是**直接删除，而不是修路径**：恢复这个功能属于新增特性，按 `PRODUCT.md` 的规矩需要有明确授权记录，而项目里没有，所以不偷偷把它接回去。真要恢复，应先取得用户授权，再按 10.2 的模式建新存储（用户级路径 + 可注入 + 独立文件）。

### 10.2 外观选择现在会记住（新增 `AppearanceStore.cs`）

以前 `appearance.SelectedIndex` 只改 `root.RequestedTheme`，不存盘，每次启动都回到"UltraGear 深色"。现在存到 `%LOCALAPPDATA%\XScreen\appearance.json`，与 `window-state.json`、`hotkeys.json` 同目录。

几个刻意的实现细节：

- **没有给 `SavedWindowState` 加字段。** `ReviewChecks/WindowState.cs` 用 `Activator.CreateInstance(savedType, width, height, max)` 三参数构造它，加字段会让那条测试直接崩。主题因此走独立文件和独立 record。
- **`AppearancePath` 在 `renderOnly` 时为 `null`**，与 `WindowStatePath`、`HotkeyPath` 一致。离线渲染和回归绝不会碰真实用户目录。
- **构造期只设 `RequestedTheme`，不调用 `ApplyAppearance`。** `ActualTheme` 要等布局才确定，构造期读它是过期的；直接设 `RequestedTheme` 让第一帧就是对的，随后由既有的 `root.ActualThemeChanged → UpdatePalette` 收尾。启动不会先闪深色再变浅。
- **恢复选区在挂 `SelectionChanged` 之前**，否则"读回自己存的值"会被当成一次用户修改、再存一遍。
- 保存失败**不影响界面**：`AppearanceStore.Save` 只记日志返回 `false`，与窗口状态的处理一致。

新增断言 9 条：往返、覆盖旧值、越界拒绝（-1/3/99）、拒绝后旧值不变、损坏文件、越界存档、I/O 失败不入故障、路径与其它偏好同目录、离线渲染不写盘。

### 10.3 `Thread.Sleep(200)` 提成命名常量（值未改）

三处（`MonitorController` 两处、`AppReset` 一处）原来是裸的 `200`，现在统一为 `MonitorController.VerifyDelayMs = 200`，并在使用处写明依据。

**值一个都没动。** `analysis/DDC-CHECKSUM-REVIEW-20261019.md` 明确记过：微软文档的"读取约 40ms、设置约 50ms"是**调用耗时**，不是这个型号需要的额外最小等待，不能据此缩短。没有设备实测就不要改这个数字。

如实记下它的代价：这个 `Thread.Sleep` 在 `Serialized` 的锁内、`Task.Run` 的线程池线程上，所以它同时延迟整个串行队列。这是既有行为，不是本次引入。

### 10.4 三处重复守卫收成一个属性

`ReadPictureStateAsync`、`ReadOsdComparisonAsync`、`WriteOsdAsync` 开头各有一段**逐字相同**的 9 条件守卫，改成 `MonitorWorkBlocked`。改前用哈希确认过三行完全一致，语义等价。

**没有扩大范围。** `CanAdjust`（11 个条件，比守卫多 `ready`、`resetting`、`advanced?.Busy`，且要求具体行可用）、`CanOpenReset`、`UpdateRefresh`、`EnableControls` 的条件各不相同、顺序也不同，统一它们需要重新推导每个字段的语义，属于真正的重构，不在这轮做。它们仍是潜在的不一致来源，记在第七节第 5 项。

### 10.5 验证过程（已全部通过）

**SDK 安装。** 本机原本没有 SDK。用官方 `dotnet-install.ps1` 装到 `%USERPROFILE%\.dotnet`（用户级，不需要管理员，没改系统 PATH），得到 **10.0.403**。要用它的 `dotnet.exe` 全路径，或把这个目录临时加到 PATH 前面。注意：直接 `bash dotnet-install.sh` 会报 `OS name could not be detected: MINGW64_NT`，必须用 PowerShell 版脚本；另外默认执行策略会拒绝运行 `.ps1`，需要 `Set-ExecutionPolicy -Scope Process Bypass`。

**编译结果。**

| 项目 | 结果 |
| --- | --- |
| `XScreen.Core` | 0 警告 0 错误 |
| `XScreen.App` | 0 警告 0 错误 |
| `XScreen.ReviewChecks` | 编译通过（2 条 CS0436 来自 WindowsAppSDK 包自身，与改动无关） |

**回归结果**（`artifacts/XScreen-regression-0.3.31/XScreen.Review.exe`）：

| 场景 | 断言 | 失败 | 真实读写 |
| --- | --- | --- | --- |
| `--appearance-recovery` | 44 | 0 | 0/0 |
| `--window-state` | 29 | 0 | 0/0 |
| `--osd-controls` | 30 | 0 | 0/0 |
| `--reset-settings` | 14 | 0 | 0/0 |
| `--ui-alignment` | 51 | 0 | 0/0 |

`--appearance-recovery` 的 44 条 = 原有 35 条 + 新增 9 条，逐条核对过名字，9 条新断言全部真实执行且通过。

### 10.6 验证中抓到并修掉的两个真实问题

这两个都是**编译器和真机才知道、读代码看不出来**的，值得记下来。

**(a) 第 4 项重构引入了 3 个 CS8604 可空警告。** 基线是 0 警告，改完变成 3 个：把内联的 `selected?.Supported != true` 抽成 bool 属性后，C# 可空流分析**断开了**“守卫通过”与“`selected` 非空”的关联，于是 `controller.ReadAsync(selected)` 被判定可能传 null。

不能靠加 `!` 压制——那是把警告藏起来而不是修好。改成让守卫**返回已确认的设备**：`MonitorDevice? IdleDevice`，调用方写 `if (IdleDevice is not { } device)`，非空事实就重新被编译器看见。改完回到 0 警告。**这是个教训：抽取条件时如果条件里含空值判断，返回值要带上被判断的对象，不能只返回 bool。**

**(b) 主题恢复了选区但界面没变——测试全绿也没发现。** 第一次真机截图：下拉框正确显示“UltraGear 浅色”，但整个界面还是深色。

根因是顺序：`root.RequestedTheme` 在构造函数**靠前**（约 73 行）就按保存值设好了，而 `root.ActualThemeChanged` 要到**靠后**（约 162 行）才挂上。等回调挂上时，`ActualTheme` 早已确定、变化事件不会再发，于是 `UpdatePalette()`（负责刷 `Palette` 颜色刷、`backdrop` 背景图、logo）**一次都没执行**，界面停在构造初期的深色配色。

修法是在挂完回调后立刻手工调一次 `UpdatePalette()`，让两个方向都覆盖：已解析时手动同步，后续变化交给回调。

**为什么 44 条断言没抓到**：那些断言查的是 `appearance.SelectedIndex`、`root.RequestedTheme`、`RequestedTheme == ElementTheme.Light` 这些**状态值**，而 bug 在**绘制副作用**上（`Palette` 静态刷子和 `backdrop.Visibility` 没更新）。状态对、画面错，正是“测试通过 ≠ 功能可用”的典型。以后测主题必须看实际像素或至少查 `backdrop.Visibility` / `Palette` 的实际值。

真机验证用了程序自带的 `--capture-once`：写 `{"Selection":1}` → 启动截图 → 确认白底深字；写 `{"Selection":0}` → 再截图 → 确认黑底浅字。两次截图期间偏好文件都没被程序改写，说明“恢复”路径不会误存。

### 10.7 收尾注意

- 验证时重新 publish 覆盖了 `artifacts/XScreen-0.3.31-win-x64/`，`XScreen.dll` 哈希从 `b6b67…` 变成 `b7673…`（后来又因 10.6(b) 再变）。**`release/` 下的安装包和 ZIP 没有重新打包**，所以现在源码与 `release/` 里的产物不一致。要发版必须重走第 3 节的完整流程。
- 测试期间在 `%LOCALAPPDATA%\XScreen\` 临时写过 `appearance.json`，已删除；`window-state.json` 未动。截图和临时报告目录已清理。
- 实测中遇到过一次真实的 DDC/CI 读校验失败（`0xC026258B`，第 4 项读取时），程序按设计暂停会话并显示“未确认”，没有猜值。重试后 7 项全部读回。这是设备的偶发通信问题，与本次改动无关。