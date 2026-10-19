# electron-builder EXE 发布

按X-SSH项目的发布方式，使用 **electron-builder 26.15.3 + NSIS**，`oneClick: false`、`allowToChangeInstallationDirectory: true`，提供向导和目录选择。XScreen仍是C# / WinUI 3程序，构建使用 `--prepackaged` 包装已经验证的521个原生文件，不加入Electron运行时，不编辑原应用EXE。

## 构建

按README完成.NET发布、离线回归和ZIP打包后运行：

```powershell
npm ci --ignore-scripts
npm run dist
```

输出与X-SSH相同的目录结构：

- `release/win-unpacked/`：原生产品文件。
- `release/XScreen Setup 0.3.29.exe`：安装器。
- `release/XScreen Setup 0.3.29.exe.blockmap`：分块信息。
- `release/latest.yml`：发布文件名、SHA512和版本元数据。
- `release/builder-debug.yml`：构建诊断。

GitHub资产使用元数据中对应的安全文件名 `XScreen-Setup-0.3.29.exe` 和 `.exe.blockmap`，字节与本地带空格的文件名完全相同。软件本身未增加自动更新功能。

## 安装行为

当前用户安装，可选择目录，开始菜单入口，桌面快捷方式可选且默认不选。完成页不自动运行。安装/卸载检查XScreen互斥体，不调用默认taskkill流程，需由用户先从托盘退出。自定义卸载清单只移除521个已安装产品文件及卸载器，不递归清空目录，保留设置、日志和其他用户文件。

兼容此前手写安装器：仅在其记录的目录与当前目标一致时，清理旧的产品卸载登记、旧卸载器和旧卸载快捷方式；不调用旧卸载器，不影响其他目录。

## 验证边界

```powershell
python scripts/Test-XScreenInstaller.py --report artifacts/installer-checks.json
```

静态检查PE、图标、版本、权限、NSIS内部CRC、521个文件的大小/哈希、latest.yml SHA512、blockmap每个分块和卸载范围。测试依赖Python的pefile。

electron-builder构建阶段会生成卸载器辅助产物；没有执行最终安装程序或XScreen，没有安装、卸载或操作显示器。真实安装向导和安装/卸载验收仍由用户执行。应用与Core仍沿用原0.3.29字节及已有23场景428项UI回归。安装器未代码签名。

此前直接手写NSIS的安装器是早期产物，当前推荐本页的electron-builder发布包。
