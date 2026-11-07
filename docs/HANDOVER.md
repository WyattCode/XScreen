# XScreen 交接文档

给接手的人（或下一个会话）看的现状说明。每完成一轮更新一次。

**怎么读**：第一到五节是接手就要知道的（现状、环境、怎么跑、代码在哪、当前异常）；第六节是不能擅自放开的边界；第七节是下一步；第八节是坑；第九节是 2026-11-01 那轮的核实记录，第十一节是 2026-11-08 这轮的（含对本文档自身的更正）。项目此前没有交接文档，这份是 2026-11-01 补的第一份，内容全部来自仓库、发布证据和本机实测，不是回忆。

**这份文档会过时，而且已经在细节上过时过一次**（见 11.4）。接手时先用第七节末尾那几条命令把第一到五节核一遍，再决定信哪些数字。

> 位置说明：根目录被 `.gitignore` 的 `/*` 挡住（只白名单放行特定文件），所以本文放在 `docs/` 下，可正常提交。

## 一、当前状态（2026-11-08 核实）

- **这是什么**：LG 32GX870A 的 Windows 显示器控制工具，C# + WinUI 3 + Windows App SDK 2.2.0，.NET 10 自包含 x64 非打包程序。完整窗口为主，托盘为快捷入口。
- **仓库**：`https://github.com/WyattCode/XScreen`，**私有**。0.3.31 及之前的 8 个提交都在 2026-10-20 一天内完成，0.3.32 是第 9 个（2026-11-02）。star / fork / issue 均为 0 是 11-01 那轮核实的数字，本轮未复核（`gh` 可用，登录态未再确认）。
- **发布资产**：每个版本 6 个（Setup.exe、blockmap、latest.yml、ZIP 便携版、逐文件清单、SHA256SUMS.txt）。0.3.31-r2 那一份三方对齐（本机文件 / `release/SHA256SUMS.txt` / `docs/release-verification-0.3.31-r2.json`）；0.3.32 目前只有 `release/` 下的本地文件，没有进 git 的验证记录。
- **版本**：应用 **0.3.32**，安装器文件版本 `0.3.32.0`（`electron-builder.yml` 的 `buildVersion`）。0.3.32 全是界面尺寸与布局修复：滑轨轨道加粗到 10px、滑块 26px、修偏左 9 逻辑像素、窗口最小高度 680→900、紧凑模式阈值提到 1010 使其可达、浅色主题滑块改深色、次要按钮补 hover/pressed、托盘分组判据改为遍历全部可写控件。用户可见的说明在 `docs/RELEASE-0.3.32.md`，改动经过在 `docs/REVIEW-2-20261101.md` 和 `docs/REVIEW-3-20261102.md`（后者记了 5 个真问题和 624 条断言）。**产品边界没有变**，见第六节。
- **最新发布：0.3.32 只提交了一半就停住**。HEAD = `47a59f5`「Release XScreen 0.3.32 with a rebuilt slider rail and layout fixes」，工作区干净、没有未合并分支，但 `main` **领先 `origin/main` 一个提交**，**没有 `v0.3.32` 标签**（`git describe --tags` 给出 `v0.3.31-r2-1-g47a59f5`）。上一轮说的"标签、HEAD、`origin/main` 三者同为 `508b96c`"只对 0.3.31-r2 成立，那一版仍是**最后一个真正闭合的发布**。跟踪文件 127 个。
- **发布证据这一轮少了一层**：`release/verification.json` 记 0.3.32 已打包——`XScreen Setup 0.3.32.exe` 67,181,858 字节、SHA256 `270af07c…dcc35`、payload 523 文件、`payloadUnchanged: true`。但 `release/` 整个被 `.gitignore` 的 `/*` 挡住，**`docs/release-verification-0.3.32.json` 不存在**，而 0.3.31 那一轮是有的。0.3.32 的发布证据目前只在这台机器上。
- **本机安装**：`D:\Apps\XScreen` 现在是**实机装上去的 0.3.32**（2026-11-02）。HKCU 有卸载登记 `XScreen 0.3.32`，键名 `89f32412-8419-574a-a05f-81788692eca6`，`UninstallString` 为 `"D:\Apps\XScreen\Uninstall XScreen.exe" /currentuser`；`InstallLocation` 与 `InstallDate` 是空的，HKLM 两处仍无条目。目录里递归共 526 个文件，`Microsoft.Windows.Workloads.Resources_ec.dll` **依然缺失**（见 5.2）。
- **最后一次真实运行**：2026-11-02 16:47–16:48，日志 `D:\Apps\XScreen\runtime-data/app.log`（27 条）。那次**有真实硬件写入**：显示器音量 `0x62` 60→50，写后回读相符（`parameter-readback-match … fullDisplayRecovery=unconfirmed`），累计 `nativeReadAttempts=11 / nativeWriteAttempts=2`。同一次启动日志写的是 "Starting XScreen **0.3.31** LIVE"，那是硬编码字符串在骗人，见 5.4。
- **11-08 分两轮**：上午那一轮（第十一节）全程只读；随后的第二轮真的编译了、跑了全套离线检查、真卸载并真重装了程序（第十二节）。两轮都没有向显示器写入。

## 二、环境：先看这一节

**这台机器上从来没有系统级的 .NET SDK、Node 或 Python。** 能在终端里跑到的解释器全部由宿主程序自带，换宿主或宿主升级，版本就会变，所以下面这些数字是**今天实测的快照**，不是长期事实。

```
C:\Program Files\dotnet\sdk            不存在（系统 dotnet 只有运行时 8.0.21）
C:\Users\Wyatt\.dotnet\sdk             10.0.403   ← 编译必须用这个
PATH 里的 dotnet                       C:\Program Files\dotnet\，不含 SDK
```

要用 `C:\Users\Wyatt\.dotnet\dotnet.exe` 全路径，或把该目录临时加到 PATH 前面（10.5 记了它是怎么装上的：用户级、不需要管理员、没改系统 PATH）。

| 工具 | 位置 | 2026-11-08 实测 | 用途 |
| --- | --- | --- | --- |
| node + npm | `%LOCALAPPDATA%\Programs\MCode\resources\runtimes\win32-x64\node\node.exe`，同目录有 `npm.cmd` / `npx.cmd` | node v24.18.1 | `npm ci`、`npm run dist` |
| node（另一处） | `C:\Program Files\WindowsApps\OpenAI.CodexPrimaryRuntime…\dependencies\node\bin\node.exe` | v24.22.1，**不带 npm** | 只能跑单文件脚本 |
| python + pip | `%LOCALAPPDATA%\Programs\MCode\resources\runtimes\win32-x64\python\python.exe` | Python 3.12.13，pip 26.1.2 | 安装器检查脚本 |
| python（另一处） | Codex 运行时 `dependencies\python\python.exe` | 3.12.14，**无 pip** | 装了也装不了第三方包 |
| `pefile` | 两个 python 里**都没有** | `…\python.exe -m pip install pefile` | `Test-XScreenInstaller.py` 依赖它 |
| electron-builder | 项目 `node_modules` | 26.15.3 | 只做 NSIS 包装，不含 Electron 运行时 |
| NSIS 缓存 | `%LOCALAPPDATA%\electron-builder\Cache` | `nsis-3.0.4.1`、`nsis-resources-3.4.1`、`7zip`，2026-10-29 | 离线打包不必重新下载 |
| `gh` | `C:\Program Files\GitHub CLI`（在系统 PATH） | 2.103.3；登录态本轮未复核 | 读发布、拉仓库数据 |
| 裸命令 `node` / `npm` / `python` / `py` | **都不在 PATH** | `python` 只剩商店别名，会提示去装 Store 版 | 一律写全路径 |

**这一节本身就是上一版交接文档的第一处失真**：它当时写 "Node 22.22.2 可用 / Python 3.13.14 可用 / `pefile` 未安装"，今天这三行的版本号一个都对不上。原因不是被卸载了——`winget list` 与三处 Uninstall 注册表里**都没有 Node.js 或 Python 的登记**，从来没有系统安装，那轮用的是当时那个宿主自带的运行时。`scripts/Build-XScreenInstaller.ps1` 这类要调 node/python 的步骤，换会话后**第一步就该把解释器全路径确认下来**，别照抄上一份文档的版本号。

**编译能力结论也反过来了**：11-01 那版说"这台机器现在构建不了"，现在已经装了 .NET 10.0.403（10.5 记录），`XScreen.Core` / `XScreen.App` / `XScreen.ReviewChecks` 都能编。缺的是 Node/Python 的**可用性稳定性**，不是 .NET。

## 三、怎么构建、怎么验

项目目标是 `net10.0-windows10.0.26100.0`，所以需要第二节的 .NET 10 SDK 全路径（Windows App SDK 2.2.0 的 workload，`--ignore-failed-sources` 已写在构建脚本里）。Node 和 Python 也要用全路径调用，理由见第二节。

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

跟踪 51 个 `.cs`：`src/` 26 个、3316 行，`tests/` 24 个、3532 行。11-01 那轮写的"26 个 / 3060 行"只数了 `src/`，0.3.32 之后净增 256 行。本机另有未跟踪的 `src/XScreen.Preview`（不参与编译，本轮没复核它现在有几个文件）。

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

### 5.1 构建时的安装器验证仍然是零，但本机已经真装过一次

这条要分成两半读。**打包那一步的验证记录里，安装器仍然一次都没执行**：`release/verification.json`（0.3.32）写的是 `generatedExecutablesExecuted: 0`、`finalInstallerExecuted: false`、`applicationExecuted: false`；0.3.31-r2 的 `docs/release-verification-0.3.31-r2.json` 写的是 `installerExecuted / uninstallerExecuted / formalApplicationStarted` 全 false、`realRegistryWrites: 0`。

```json
"installerExecuted": false, "uninstallerExecuted": false,
"formalApplicationStarted": false, "realRegistryWrites": 0
```

那 554 项静态检查 + 64 项行为回归（0.3.31-r2）都是**离线**的：静态检查验 NSIS CRC / PE / 载荷哈希 / blockmap；行为回归用一个有限的 NSIS 源码解释器跑宏和分支，Win32、Shell、注册表、文件系统全部是模拟。真实向导、UAC、Shell 快捷方式、锁竞争、注册表写入在**打包流程**里一次都没跑过，安装器也未代码签名。

**但在打包之后，0.3.32 的安装器在这台机器上被手动跑过一次**（2026-11-02）：HKCU 出现卸载登记、目录里有卸载器、程序随后真实运行并写过显示器（见一、和 5.2）。这是这个产品**唯一一次有据可查的真实安装**，证据只有注册表条目、目录内容和 `app.log`，没有任何一份验证报告记着它。**卸载器至今没有执行过**，所以"卸载保留设置"这条承诺（`RELEASE-0.3.32.md` 里写着）仍未验证。

### 5.2 本机安装的异常：一处已闭合，一处还在（2026-11-08 实测）

11-01 那轮记了三处对不上。现在状态变了：

**(a) 少一个文件——仍未解决。** `D:\Apps\XScreen` 里递归 526 个文件，`Microsoft.Windows.Workloads.Resources_ec.dll`（174944 字节）**依然不在**。它在 `artifacts/releases/v0.3.31/stage/XScreen/` 和 ZIP 里都有。第十二节做了全新安装（同一个安装包、同一个路径、卸载后目录已清空），**结果依旧缺这个文件**——所以它不是拷贝产物，而是这套安装包释放文件这一步的稳定行为，原因待查（NSIS 清单或防病毒拦截）。不排除影响 WinAppSDK 运行时资源加载——0.3.32 装完后程序能正常启动并完成 DDC 读写，所以不是致命的，但别把它当噪音。

**(b) 查不到卸载登记——已闭合，但留下一个新的隐患。** HKCU 现在有 `XScreen 0.3.32`：键名 `89f32412-8419-574a-a05f-81788692eca6`，`UninstallString` 为 `"D:\Apps\XScreen\Uninstall XScreen.exe" /currentuser`，`DisplayVersion 0.3.32`，`Publisher XScreen`。也就是说 11-01 那条"三处注册表共 111 条里没有 XScreen"是**真的因为当时没装过**，不是安装器把登记写坏了。

但这条登记的 **`InstallLocation` 是空值，`InstallDate` 也是空值**（HKLM 两处仍无条目，per-user 安装本该如此）。空 `InstallLocation` 不是 cosmetic：`packaging/functions.nsh:8`、`functions.nsh:99` 和 `installer.nsh:110` 三处都靠读它来定位旧安装目录，而 `functions.nsh:9-13` 的分支是**读到空就当"没有旧版本"，直接 `Return` 返回成功**。所以在这台机器上，下一次升级**不会**运行旧卸载器、**不会**删除旧文件集，只会往同一目录覆盖——正是 r2 处理"旧卸载器目录重定向"想避免的那条路径被空值绕过去了。这一条要在真实升级测试里盯住（第七节的卸载 / 升级验收）。

**(c) 时间线对不上——已闭合。** 11-01 发现 `D:\Apps\XScreen` 的目录创建时间（10-20 01:27，0.3.29 时代）远早于文件 mtime（10-20 21:04，正是 r2 安装包构建时刻），而 r2 记着 `installerExecuted: false`，据此推断那次不是跑安装器得到的。现在这台机器上跑过 0.3.32 安装器，登记、卸载器、快捷方式和 `app.log` 相互印证，0.3.31 那个目录的来历问题**不再是当前状态的一部分**，只作为历史留着。

**能确认的**：本机装的是 0.3.32，卸载入口可用（登记路径与实际文件一致），程序装后能跑并真实调节成功，但少一个 `Resources_ec.dll`，且它的 `InstallLocation` 是空的。**不能确认的**：卸载是否真的保留用户文件和设置；缺那个 dll 是否影响 WinAppSDK 的其它资源加载；空 `InstallLocation` 在实际升级时会不会导致旧文件残留（三处读它的代码都按"没有旧版本"处理）。这三条要靠一次真实卸载 + 重装 + 一次跨版本升级才能回答，见第七节的卸载 / 升级验收。

### 5.3 仓库卫生：三处不一致

- `XScreen.slnx` 只含 6 个工程（App、Core、Acceptance、OfflineSafety、OsdChecks、TrayChecks），**缺 `InteractionChecks`、`DiagnosticsChecks`、`ReviewChecks`、`PreviewChecks`**。而 README 明确要求跑前三个。`dotnet build XScreen.slnx` 不会构建它们。
- 反过来，README 没提 `TrayChecks`（它也没在 README 的回归清单里）。
- `src/XScreen.Preview` 和 `tests/XScreen.PreviewChecks` **未被 git 跟踪**（`.gitignore` 的 `/src/*` 只白名单了 App 和 Core）。也就是说 clone 下来没有 0.2.0 那个历史 mock，`预览新界面.cmd` 会指向一个不存在的目录。

### 5.4 版本号散落在 9 个地方

改版本时要一起改，漏了基本都会在构建或检查阶段报错（有断言兜着，不会静默出错）：

`package.json`、`src/XScreen.App/XScreen.App.csproj`（`<Version>`）、`electron-builder.yml`（`buildVersion`，四段）、`scripts/Build-XScreen.ps1`（默认输出目录）、`scripts/Package-XScreen.ps1`（`-Version`）、`scripts/Build-XScreenInstaller.ps1`（`-Version`）、`scripts/Test-XScreenOfflineUi.ps1`（`-Version`）、`tests/XScreen.ReviewChecks/XScreen.ReviewChecks.csproj`（`XScreenVersion`）、`运行XScreen.cmd`（路径）。

另有一处**没有断言**：`src/XScreen.App/Program.cs:28` 的启动日志里硬编码了 `"Starting XScreen 0.3.31 LIVE"`。**到 0.3.32 这一轮它已经真的漏改了**：csproj 是 `<Version>0.3.32</Version>`、`electron-builder.yml` 是 `buildVersion: 0.3.32.0`，而 2026-11-02 装好后第一次启动的日志写的是 "Starting XScreen 0.3.31 LIVE"（`D:\Apps\XScreen\runtime-data/app.log` 第 1 条）。也就是**装的是 0.3.32，日志说是 0.3.31**——这行日志是排查时的第一手证据，它会误导人。修法不是再改一次字符串，而是让它从程序集版本读（`Assembly.GetEntryAssembly()?.GetName().Version`），否则每次发版都要记着改。这条排在第七节的启动日志硬编码一项，改它要走第 3 节完整流程，因为会打破源码与 `release/` 产物的一致性（见 10.7）。

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

1. **把 0.3.32 真正发出去**。现在仓库里有一个「Release XScreen 0.3.32」的提交，GitHub 上却没有任何对应的东西：给它打 `v0.3.32` 标签、推 `main`（领先一个提交）、按 0.3.31 的格式把 `release/verification.json` 落成 `docs/release-verification-0.3.32.json` 并提交，再核对六个发布资产是否都上传。推送和打标签是别人看得见的动作，动手前跟用户确认。
2. **跑完整离线回归**。0.3.32 那轮跑过 ReviewChecks 的 13 个场景、624 条断言（`docs/REVIEW-3-20261102.md` 第五节），但 `XScreen.Acceptance`、`XScreen.OfflineSafety`、`XScreen.InteractionChecks`、`XScreen.DiagnosticsChecks`、`XScreen.OsdChecks` 这五个独立工程在 11-01 之后没有重跑记录，而 `XScreen.slnx` 里后三个根本没登记（5.3）。第 3 节那串命令按现在的 SDK 是能全跑的。
3. **真实卸载 → 重装 → 跨版本升级验收**。卸载器至今没执行过，`RELEASE-0.3.32.md` 里"卸载保留设置、日志和其他用户文件"这句话目前是空头承诺。重点三处：卸载后 HKCU 登记是否清干净、`%LOCALAPPDATA%\XScreen\` 的三个偏好文件是否留下、以及 5.2(b) 的空 `InstallLocation` 在升级时是否导致旧文件残留。这台机器已经装好 0.3.32，是做这件事的现成条件。
4. **修掉 5.4 的启动日志硬编码**。让 `Program.cs:28` 从程序集版本读，而不是再手改一次字符串。改它要重走第 3 节完整流程，因为会打破源码与 `release/` 产物的一致性（10.7）。
5. **代码签名**。安装器至今未签，SmartScreen 会拦。X-SSH 那边有 `docs/signing.md` 记过选型（Azure Artifact Signing / 商业 OV），可以复用那套结论。
6. **清理仓库卫生**（5.3）：补 `XScreen.slnx` 缺的四个工程，决定 `XScreen.Preview` 是纳入跟踪还是删掉 `预览新界面.cmd`。
7. **`MainWindow` 的其余状态条件尚未收敛**。第十节只把三处逐字相同的守卫收成一个属性；`CanAdjust`（11 个条件）、`CanOpenReset`、`UpdateRefresh`、`EnableControls` 仍是手写组合，尚未统一。0.3.32 那轮修的三个 bug 里有两个（最小高度漂移、紧凑阈值不可达）正是这类"同一个事实写在两处"的后果，`REVIEW-3` 已把两处改为引用 `WindowStateStore.MinimumWidth/MinimumHeight`，可以照这个思路继续。
8. **功能层面没有已授权的待做项**。想加新控制（比如色域、HDR）必须先问用户要授权，并按现有方式走 `MonitorSafetyPolicy` + 串行回读 + 离线回归。

接手时先跑这几条只读命令，核对本文第一到五节还成不成立（本文已经被证伪过一次，见 11.4）：

```powershell
git status -sb; git log --oneline -3; git describe --tags   # 版本是否闭合
git ls-files | Measure-Object -Count                        # 跟踪文件数
(Get-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\89f32412-8419-574a-a05f-81788692eca6").DisplayVersion
Test-Path "D:\Apps\XScreen\Microsoft.Windows.Workloads.Resources_ec.dll"
Get-Content "D:\Apps\XScreen\runtime-data\app.log" | Select-Object -Last 3
& "C:\Users\Wyatt\.dotnet\dotnet.exe" --list-sdks           # SDK 还在不在
```

## 八、坑

- **构建产物和回归宿主必须是同一批程序集**。`Package-XScreen.ps1` 会比对哈希，不一致直接拒绝打包。改完代码要重新 `publish` 回归宿主，不能只重编主程序。
- **`analysis/` 整个被 git 忽略**。所有 UI 证据、`summary.json`、旧安装器基线都在里面。换机器后 `Package-XScreen.ps1` 会因为没有证据而失败——不是脚本坏了。
- **不要直接跑 ZIP 里的 EXE**。README 明确写了。跑就用 `运行XScreen.cmd` 或已安装副本。
- **不要为了测试自动改显示器**。历史上出过黑屏事故（`analysis/INCIDENT-BLACK-SCREEN-20261017.md`）。所有回归都用假传输。
- **`--prepackaged` 是这套安装器的关键**。它让 electron-builder 原样包装已发布的 WinUI 文件清单，不下载 Electron、不改 EXE 字节。改动 payload 就得重新走 `Package-XScreen.ps1`。
- **卸载器是静态提取的**，不是执行生成器得来的（`UninstallerReader`）。`release/uninstaller-evidence.bin` 只给 CRC 检查用，不是给用户的卸载入口。
- **已经复制到别处的旧卸载器不会因为新版修复而自动变好**。用当前安装目录里的卸载入口。
- **注册表在这次会话里读得到、但 `reg.exe` 被安全策略拦了**。用 PowerShell 的 `Get-ItemProperty` 而不是 `reg query`。
- **Node 和 Python 是宿主自带的，不是本机装的**。版本会变（11-01 记的是 Node 22.22.2 / Python 3.13.14，11-08 实测两处宿主分别是 node 24.18.1 + py 3.12.13 和 node 24.22.1 + py 3.12.14，都对不上）。每次开工先确认解释器全路径，别照抄上一份文档；两个 python 里都没有 `pefile`，要用带 pip 的那个现装。Codex 那个 node **不带 npm**，`npm run dist` 只能用 MCode 那套。
- **`Package-XScreen.ps1` 的默认证据路径现在不存在**。它要 `analysis/evidence/local-review/summary.json`，而本机现在只有 `analysis/evidence/rel_0_3_32_final/summary.json`（24 个场景，2026-11-02 01:36）。不传 `-EvidenceDirectory` 会直接报错，这不是脚本坏了（第八节上一条也说了 `analysis/` 不进 git）。
- **`release/` 和 `artifacts/` 都不进 git**。0.3.32 的安装包（67 MB，`release/XScreen Setup 0.3.32.exe`）和它的 `verification.json` 只存在于这台机器上；换机器就没有安装包，也没有那一轮的验证记录。0.3.31 之所以能三方对齐，是因为 `docs/release-verification-*.json` 提交了。
- **`runtime-data/` 只属于运行中的那份程序**。已安装副本写 `D:\Apps\XScreen\runtime-data\app.log`，旧的构建产物里也留着一堆同名目录（`artifacts/XScreen-0.3.x-win-x64/runtime-data/`），项目根目录**没有**这个目录（被 `**/runtime-data/` 忽略）。查真实运行记录要认安装副本那一份；10.1 删掉 `Preferences.cs` 就是因为它往安装目录里写偏好文件。
- **`XScreen.Review.exe` 的第一个参数是报告路径，第二个才是场景**。写成 `XScreen.Review.exe --window-state` 不会报错：它把场景名当报告路径、按"没有场景"把界面正常开起来，十几秒后自己退出，一条断言都没跑（`tests/XScreen.ReviewChecks/Program.cs:33-34`）。宿主是 WinExe，**没有控制台输出**，判定只能通过报告 JSON 里的 `failures` / `results` / `nativeReadAttempts` / `nativeWriteAttempts`（`Program.cs:106-109`），未处理异常写到 `<报告路径>.error`（`Program.cs:69`）。报告不存在和"通过"在退出码上可能完全一样，必须确认文件真的生成了。
- **场景会开真实窗口，逐条前台跑会被卡住**。宿主跑完自己会 `ExitAsync`（`Program.cs:224`），但自动化时用 `Start-Process` + `WaitForExit(超时)` 并轮询结果文件更稳；本机的做法见 `artifacts/run-verify-scenarios-20261108.ps1`（不进 git）。

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

**没有扩大范围。** `CanAdjust`（11 个条件，比守卫多 `ready`、`resetting`、`advanced?.Busy`，且要求具体行可用）、`CanOpenReset`、`UpdateRefresh`、`EnableControls` 的条件各不相同、顺序也不同，统一它们需要重新推导每个字段的语义，属于真正的重构，不在这轮做。它们仍是潜在的不一致来源，记在第七节的 `MainWindow` 状态条件一项。

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
  （2026-11-08 更新：0.3.32 那一轮已经重走过完整流程，`release/XScreen Setup 0.3.32.exe` 与当时的 HEAD 源码是一致的。这条不变量现在有效的形式是：**任何源码改动之后，`release/` 就再次落后**，包括第七节的启动日志硬编码那项修改。）
- 测试期间在 `%LOCALAPPDATA%\XScreen\` 临时写过 `appearance.json`，已删除；`window-state.json` 未动。截图和临时报告目录已清理。
- 实测中遇到过一次真实的 DDC/CI 读校验失败（`0xC026258B`，第 4 项读取时），程序按设计暂停会话并显示“未确认”，没有猜值。重试后 7 项全部读回。这是设备的偶发通信问题，与本次改动无关。

## 十一、本轮核实记录（2026-11-08）

只读。没有构建、没有运行测试、没有启动程序、没有碰显示器、没有执行安装器或卸载器。

### 11.1 环境与工具链

| 核实项 | 方法 | 结果 |
| --- | --- | --- |
| 本机是否装过 Node / Python | `winget list` 全表 + 三处 Uninstall 注册表按 `node\|python\|volta\|nvm\|conda` 过滤 | **一处登记都没有**，从来没有系统安装 |
| 裸命令是否在 PATH | `Get-Command node,npm,python` / `where.exe node` / `py --version` | 全都不可用；`python` 只剩商店别名，跑了会提示去装 Store 版 |
| 可用的 node + npm | `%LOCALAPPDATA%\Programs\MCode\resources\runtimes\win32-x64\node\` | v24.18.1，同目录有 `npm.cmd` / `npx.cmd` |
| 另一处 node | `C:\Program Files\WindowsApps\OpenAI.CodexPrimaryRuntime…\dependencies\node\bin\node.exe` | v24.22.1，**不带 npm**（`bin` 里只有 node.exe 和 VC 运行库） |
| 可用的 python + pip | 同上 MCode 目录 `\python\python.exe` | 3.12.13，pip 26.1.2 |
| 另一处 python | Codex 运行时 `dependencies\python\python.exe` | 3.12.14，无 pip |
| `pefile` | 两个 python 都 `import pefile` | **两处都没有**，要用带 pip 的那个现装 |
| .NET SDK | `& "C:\Users\Wyatt\.dotnet\dotnet.exe" --list-sdks` | 10.0.403；`C:\Program Files\dotnet` 只有 `host`/`shared`，无 `sdk` |
| 打包工具链 | `node_modules/electron-builder` 版本 + `%LOCALAPPDATA%\electron-builder\Cache` | 26.15.3；NSIS 3.0.4.1、nsis-resources 3.4.1、7zip 均已缓存（2026-10-29），离线可打包 |
| `gh` | `gh --version` | 2.103.3 在系统 PATH；**登录态本轮未复核**（"已登录 WyattCode" 是 11-01 的结论） |
| 打包前置证据 | 读 `analysis/evidence/rel_0_3_32_final/summary.json` | 数组，24 个场景；默认的 `local-review` 目录不存在 |

**结论**：第 3 节那套流程今天是可跑的——SDK、node+npm、electron-builder、NSIS 缓存都在，只欠一个 `pefile`。真正的风险不是"缺工具"，而是**这些工具全部由宿主程序提供，换宿主或宿主升级就变版本**。上一版记的 Node 22.22.2 / Python 3.13.14 现在两处宿主都对不上。

### 11.2 本机安装状态（这一节变化最大）

| 核实项 | 方法 | 结果 |
| --- | --- | --- |
| 是否存在卸载登记 | 三处 Uninstall 注册表遍历 DisplayName | HKCU 下 `89f32412-8419-574a-a05f-81788692eca6` = `XScreen 0.3.32`；HKLM 两处仍无 |
| 登记内容 | `Get-ItemProperty` | `DisplayVersion 0.3.32`、`Publisher XScreen`、`UninstallString "D:\Apps\XScreen\Uninstall XScreen.exe" /currentuser`、**`InstallLocation` 与 `InstallDate` 为空** |
| 空 `InstallLocation` 的后果 | 读 `packaging/functions.nsh:8`、`:99`、`installer.nsh:110` 和 `functions.nsh:9-13` | 三处升级逻辑读它定位旧目录；读到空即按"没有旧版本"返回成功 → **本机下次升级不会清理旧文件** |
| `HKCU\Software\XScreen` | `Test-Path` | 仍不存在 |
| 缺文件 | `Test-Path` | `Microsoft.Windows.Workloads.Resources_ec.dll` **仍缺**（0.3.32 真装之后依旧）；目录递归 526 个文件 |
| 最后一次真实运行 | `D:\Apps\XScreen\runtime-data\app.log`（27 条） | 2026-11-02 16:47:41 启动，16:48:55 结束 |
| 最后一次真实硬件写入 | 同上，`Sequence 16-20` + `parameter-readback-match` | 音量 `0x62` 60→50，回读相符（`fullDisplayRecovery=unconfirmed`），累计 `nativeReadAttempts=11 / nativeWriteAttempts=2` |
| 启动日志的版本号 | 同一日志第 1 条 | 写的是 "Starting XScreen **0.3.31** LIVE"，而装的是 0.3.32 → 5.4 的硬编码漂移已经在真机上产生误导 |

### 11.3 版本与发布是否闭合

| 核实项 | 方法 | 结果 |
| --- | --- | --- |
| 提交与远端 | `git status -sb`、`git log --oneline -6` | HEAD `47a59f5`（Release 0.3.32），工作区干净，**`main` 领先 `origin/main` 一个提交** |
| 标签 | `git describe --tags`、`git tag --sort=-creatordate` | **没有 `v0.3.32`**，最高标签仍是 `v0.3.31-r2` |
| 跟踪文件 | `git ls-files` | 127 个（上一版记 123） |
| 源码规模 | `git ls-files` 分行数 | `src/` 26 个 / 3316 行，`tests/` 24 个 / 3532 行；`src/XScreen.Preview` 跟踪数 0 |
| 版本号落点 | 读 csproj / yml / Program.cs | `<Version>0.3.32</Version>`、`buildVersion: 0.3.32.0`、启动日志仍 `0.3.31`；`git show --stat` 显示四个脚本的默认版本都跟着改了 |
| 0.3.32 打包记录 | `release/verification.json` | 已打包：Setup 67,181,858 字节、SHA256 `270af07c…dcc35`、payload 523 文件、`payloadUnchanged: true`、`finalInstallerExecuted: false`、`applicationExecuted: false` |
| 0.3.32 的 git 内证据 | `docs/` 目录列举 | **只有 `RELEASE-0.3.32.md`**，没有 `release-verification-0.3.32.json`；`release/` 整个被 gitignore 挡住 |

### 11.4 上一版这份文档失真的地方（以及为什么会失真）

写这份文档的人只隔了一天就过时了，全部集中在**环境数字**和**"最后一次"这类时间性判断**上。列出来是给下一轮省事：

| 上一版（11-01）写的 | 现在（11-08）实测 | 失真类型 |
| --- | --- | --- |
| "本机没有 .NET SDK，构建不了" | SDK 10.0.403 已在 `~\.dotnet`，能编译 | 文档写于装 SDK 之前，同一天内被自己推翻 |
| "Node 22.22.2 可用 / Python 3.13.14 可用" | 两处宿主分别是 24.18.1 / 3.12.13 和 24.22.1 / 3.12.14，且都不在 PATH | 版本号来自易变的宿主，不该写成事实 |
| "标签、HEAD、`origin/main` 三者同为 `508b96c`" | 已提交 0.3.32（`47a59f5`），未推送、无标签 | 时间性判断，隔天就旧 |
| "最后一次真实运行 10-29，最后写入 10-28" | 11-02 16:48 有真实写入（音量 60→50） | 同上 |
| "三处注册表 111 条里没有 XScreen" | HKCU 有 `XScreen 0.3.32` | 当时确实没装过；结论本身没错，但读起来像安装器坏了 |
| 第四节 "26 个 `.cs` / 3060 行"、第九节 "跟踪 123 个" | src 26 / 3316，tests 另 24 / 3532，跟踪 127 | 口径没写清（只数了 src），数字也没跟上 |
| 10.7 "`release/` 没重新打包，源码与产物不一致" | 0.3.32 已重打包，两者曾一致 | 已完成的事没标完成 |

**能带走的教训**：交接文档里最靠不住的是"当下环境快照"和"最后一次某事"这两类。写的时候标上日期和第几节，并在第七节留一段可复跑的核对命令（已经这么做了），下一轮先跑那段再谈信任。

### 11.5 本轮结束时仍未闭合的事

卸载器一次都没执行过，`RELEASE-0.3.32.md` 里"卸载保留设置、日志和其他用户文件"这句话没有实测支撑。空 `InstallLocation` 在跨版本升级时的行为只是读代码推断的，没实测。0.3.32 的标签、推送和 git 内的验证记录三项都缺。这三条分别对应第七节的发版闭合与卸载 / 升级验收两项。

## 十二、编译 + 全套离线检查 + 真实卸载重装（2026-11-08，已实际执行）

这一轮不是只读：编译了、跑了全套离线检查、真卸载了、真重装了。显示器**只被读取，没有被写入**。成品目录 `release/` 与 `artifacts/XScreen-0.3.32-win-x64` 都没被这一轮改动，构建全部落在 `verify-20261108` 系列目录里。

### 12.1 编译与离线检查（全绿）

| 项 | 命令 / 位置 | 结果 |
| --- | --- | --- |
| 主程序 | `scripts/Build-XScreen.ps1 -OutputDirectory artifacts/XScreen-0.3.32-verify-20261108-win-x64` | 成功，Core / App 无警告无错误；没有覆盖发布用的产物目录 |
| 独立工程 | `dotnet run --project tests/...` | Acceptance PASS；OfflineSafety 19；InteractionChecks 25；DiagnosticsChecks 23；OsdChecks 187；另加 TrayChecks 61。全部 exit=0，真实读写 0/0 |
| 界面场景 | `artifacts/XScreen-regression-verify-20261108/XScreen.Review.exe <报告> <场景>`，13 个 | **624 条断言、0 失败、native 0/0**。逐场景 44 / 29 / 30 / 14 / 51 / 24 / 27 / 16 / 18 / 16 / 292 / 49 / 14，与 `docs/REVIEW-3-20261102.md` 记录的完全一致 |
| 汇总 | `artifacts/verify-20261108-scenarios/results.txt` 与同名 `*.json` | 13 行记录，全部 exit=0 |

`src/*.cs` 一个字节都没改，所以这套结果等于给 0.3.32 的现有源码 + 现有安装包重新签了一次健康证明。

### 12.2 真实卸载：项目史上第一次执行卸载器

命令：`& "D:\Apps\XScreen\Uninstall XScreen.exe" /currentuser /S`

做对的部分：安装目录从 526 个文件降到 2 个；`Uninstall XScreen.exe` 自身被删除；HKCU 卸载登记被删除；HKLM 两处无残留；`%LOCALAPPDATA%\XScreen\window-state.json` 保留（59B，时间戳仍是 11-02 16:48）；安装目录里的 `runtime-data\app.log`（10577B）和 `runtime-data\sessions\*.jsonl`（19505B）**保留**——`RELEASE-0.3.32.md` 里"卸载保留设置、日志和其他用户文件"这句在日志和设置上确实兑现了。

**缺陷一：开始菜单快捷方式没删。** `%APPDATA%\...\Programs\XScreen\XScreen.lnk` 留在原处，`TargetPath` 是已经不存在的 `D:\Apps\XScreen\XScreen.exe`，点它会报找不到程序。

**缺陷二（更要紧）：保留日志的策略和安装包的守卫互相咬死。** 卸载**故意**留下 `runtime-data`，于是目录非空；而卸载登记已经删掉，所以安装包那条"已登记的 XScreen 安装目录"的豁免也不成立。结果是**重装到同一个路径被拒绝**，弹窗原话：

> 该文件夹已有其他内容。请选择空的 XScreen 文件夹，或已登记的 XScreen 安装目录。

用户只有两条路，都难受：手动删掉整个目录（连带丢掉被"保留"的日志），或者装到别的地方去（开始菜单和卸载登记都指向新路径，旧日志永久孤立）。**这是卸载保留策略与目录守卫的设计冲突，不是偶发故障。**

### 12.3 真实重装

两条路都试过。向导能把路径改成 `D:\Apps\XScreen`，但前提是那个目录已经空掉（见缺陷二）。静默方式也可以：`"XScreen Setup 0.3.32.exe" /S /D=D:\Apps\XScreen`，4 秒完成、exit=0。

| 装后核实 | 结果 |
| --- | --- |
| 文件数 | 524（= 526 减去卸载时特意保留、后来被我删掉的 2 个日志文件） |
| `XScreen.exe` / 卸载器 / 开始菜单快捷方式 | 齐全 |
| HKCU 卸载登记 | 回来了，**仍是同一个键名** `89f32412-8419-574a-a05f-81788692eca6`；`InstallLocation` 与 `InstallDate` **还是空** |
| `Microsoft.Windows.Workloads.Resources_ec.dll` | **仍然缺失** |
| 启动日志 | `Starting XScreen 0.3.31 LIVE`——5.4 的硬编码在全新装机的 0.3.32 上再次出现 |
| 启动行为 | `{"mode":"live","nativeReadAttempts":4,"nativeWriteAttempts":0,"automaticSettingTests":false}`，只读、没有自动写入，符合第六节边界 |

两个结论要更新到前面的章节：**5.2(a) 缺文件**不是"某次拷贝的意外"，全新安装、同一个安装包、同一个路径，结果一样——是装机这一步的稳定行为，ZIP 和 stage 目录里有、装完没有，原因待查（NSIS 文件清单，或防病毒拦截）。**5.2(b) 空 `InstallLocation`** 也不是偶发，静默安装照样为空，是这套 NSIS 登记的固定行为，所以 12.2 的缺陷二每次卸载重装都会重现。

### 12.4 这一轮对机器动过的手

`D:\Apps\XScreen` 在卸载后剩的两个日志文件被我**备份到 `artifacts/verify-20261108-pre/`**（`app.log` 10577B、sessions jsonl 19505B，另有卸载登记键的原始值 `uninstall-registry-before.txt`），然后为了完成重装**删掉了那个空壳目录**和死链快捷方式目录。现在装机是 11-08 重装出来的，`runtime-data\app.log` 是新日志（22 条，首条 04:40:44），**10-28 到 11-02 那段时间的运行记录只剩备份里有了**。

### 12.5 还没验的

跨版本升级仍未实测，而且条件没变好：登记回到 `InstallLocation` 为空，升级会走 `packaging/functions.nsh:9-13` 的"没有旧版本"分支、跳过旧文件清理。要真验证必须先让登记写上 `InstallLocation`，那是 NSIS 侧的代码改动，不是测试能绕过的。