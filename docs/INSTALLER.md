# electron-builder EXE 发布

按X-SSH项目的发布方式，使用 **electron-builder 26.15.3 + NSIS**，`oneClick: false`、`allowToChangeInstallationDirectory: true`，提供向导和目录选择。XScreen仍是C# / WinUI 3程序，构建使用 `--prepackaged` 包装已经验证的原生文件清单，不加入Electron运行时，不编辑原应用EXE。

## 构建

按README完成.NET发布、离线回归和ZIP打包后运行：

```powershell
npm ci --ignore-scripts
npm run dist
```

输出与X-SSH相同的目录结构：

- `release/win-unpacked/`：原生产品文件。
- `release/XScreen Setup 0.3.31.exe`：安装器。
- `release/XScreen Setup 0.3.31.exe.blockmap`：分块信息。
- `release/latest.yml`：发布文件名、SHA512和版本元数据。
- `release/builder-debug.yml`：构建诊断。

GitHub资产使用元数据中对应的安全文件名 `XScreen-Setup-0.3.31.exe` 和 `.exe.blockmap`，字节与本地带空格的文件名完全相同。软件本身未增加自动更新功能。

## 安装行为

当前用户安装，可选择本地磁盘中以 `XScreen` 结尾的专用目录，例如 `D:\Apps\XScreen`。拒绝 Windows 系统目录、UNC、未登记的非空目录，以及安装路径和产品成员中的重解析点。更换目录升级时同时检查旧安装目录。命令行静默安装也经过相同的最终检查；拒绝 `/allusers`，管理员运行时也固定为当前用户。

开始菜单入口始终创建；首次安装默认不创建桌面快捷方式，覆盖升级保留已有选择，也尊重用户自行删除桌面入口。新旧入口按实际目标识别，不按文件名盲删；创建失败明确报错。旧版卸载器曾无条件删除桌面同名入口，因此升级前发现无关同名入口会停止，需用户先自行处理冲突。

完成页不自动运行，`--force-run /S` 也不会启动应用。安装/卸载检查 XScreen 互斥体，查询权限异常也停止；不终止应用进程，需用户先从托盘退出。安装程序检查 Windows 内部版本不低于 19041，与应用的最低目标一致。

自定义卸载清单只移除逐文件清单中的产品文件、安装器图标和卸载器，不递归清空目录，保留设置、日志和其他用户文件。普通产品文件删除失败立即停止，保留安装登记供重试；这不代表事务回滚，已经删除的文件不会自动恢复。卸载目录必须与当前用户的安装登记一致。

兼容此前手写安装器：仅在其记录的目录与当前目标一致时，清理旧的产品卸载登记、旧卸载器和旧卸载快捷方式；不调用旧卸载器，不影响其他目录。

## 验证边界

```powershell
python scripts/Test-XScreenInstaller.py --report artifacts/installer-checks.json
python scripts/Test-XScreenInstallerBehavior.py --report artifacts/installer-behavior-checks.json
```

静态检查安装器和静态提取卸载器的 NSIS CRC、PE、图标、版本、权限、完整载荷的大小/哈希、latest.yml SHA512、blockmap每个分块和卸载范围。构建保存参与编译的配置/脚本哈希，防止检查与安装包来源不一致。测试依赖 Python 的 pefile。

行为回归通过有限的 NSIS 源码解释器执行实际宏、分支、循环和生成的卸载清单，仅替换 Win32/COM、注册表、文件系统边界。覆盖目录、旧目录升级、快捷方式选择/冲突/失败、占用删除失败、数据保留和卸载登记校验。解释器不模拟完整 Windows Shell、ACL、UAC 或真实的 NSIS 向导。

当前安装器和应用文件版本为 **0.3.31.0**。安装器载荷与本版已验证的ZIP清单逐项一致；此前0.3.29安装器修订2保留在对应历史发布页。构建使用 electron-builder 内置 `UninstallerReader` 静态提取卸载器，禁止执行生成器或回退到 Wine/VM 执行。`release/static-build.json` 保存提取证据和源码哈希；`uninstaller-evidence.bin` 仅供 CRC 检查，不是提供给用户运行的卸载入口。

本轮只编译与离线回归，没有执行生成的安装器、卸载器或 XScreen，没有安装、卸载或操作显示器。真实安装向导、系统 Shell 快捷方式、权限和安装/卸载验收仍需用户执行；离线结果不宣称完成真实安装验证。本版对实际发布程序集运行假设备UI回归宿主，正式应用入口没有启动。安装器未代码签名。

此前直接手写NSIS的安装器是早期产物，当前推荐本页的electron-builder发布包。
