# EXE 安装包

`XScreen-0.3.29-Setup-x64.exe` 是带开始菜单、可选桌面快捷方式和Windows卸载入口的用户级安装器。默认目录为 `%LOCALAPPDATA%\Programs\XScreen`。无需安装.NET或Windows App SDK运行库，产品载荷为自包含发布。

安装和卸载前请在托盘退出XScreen，安装器会检查应用单实例互斥体，拒绝覆盖运行中的程序。安装、完成页和卸载均不启动应用，不发送显示器命令。桌面快捷方式默认不选。卸载按521个已安装产品文件的清单删除，不递归清空目录，保留日志和用户偏好。

## 构建

先按README构建并打包已验证的ZIP载荷，再运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Build-XScreenInstaller.ps1 -Compiler C:/path/to/NSIS/Bin/makensis.exe
```

使用NSIS Unicode编译器。本次采用Electron Builder分发的可移植工具，仅解包到工作区，未安装编译器。下载来源：

https://github.com/electron-userland/electron-builder-binaries/releases/tag/nsis-3.0.4.1

归档SHA256：`9877df902530f96357d13a7a31ae2b9df67f48b11ffc9a1700a7c961574ec5fa`。

脚本先逐文件核对已发布ZIP清单，生成独立的安装/卸载清单，再编译EXE并记录SHA256。不覆盖已存在的安装器。失败或再次构建使用新的RunName，保留先前证据。

静态验证使用Python及pefile：

```powershell
python scripts/Test-XScreenInstaller.py --build artifacts/<本次构建目录> --report artifacts/<新报告名>.json
```

检查Windows PE、图标、版本、asInvoker权限、内部CRC、521个输入文件的大小/哈希、明确卸载清单和无自动启动动作。所有检查只读取文件；没有执行安装器、卸载器或应用，因此不代表真实安装/卸载验收。

应用程序和Core与既有0.3.29发布字节相同，23场景428项UI及协议/安全回归结果继续适用。新EXE另行提供SHA256；原ZIP不变。安装器未作代码签名。
