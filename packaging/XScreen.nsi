Unicode true
!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "x64.nsh"
!include "WinVer.nsh"

Name "XScreen ${APP_VERSION}"
OutFile "${SETUP_FILE}"
InstallDir "$LOCALAPPDATA\Programs\XScreen"
InstallDirRegKey HKCU "Software\XScreen\Install" "InstallDir"
RequestExecutionLevel user
SetCompressor /SOLID lzma
SetCompressorDictSize 32
CRCCheck on
ManifestDPIAware true
VIProductVersion "${APP_VERSION}.0"
VIAddVersionKey /LANG=2052 "ProductName" "XScreen"
VIAddVersionKey /LANG=2052 "FileDescription" "XScreen 安装程序"
VIAddVersionKey /LANG=2052 "FileVersion" "${APP_VERSION}.0"
VIAddVersionKey /LANG=2052 "ProductVersion" "${APP_VERSION}"
VIAddVersionKey /LANG=2052 "LegalCopyright" "XScreen"

!define MUI_ICON "${APP_ICON}"
!define MUI_UNICON "${APP_ICON}"
!define MUI_ABORTWARNING
!define MUI_WELCOMEPAGE_TITLE "安装 XScreen ${APP_VERSION}"
!define MUI_WELCOMEPAGE_TEXT "将为当前 Windows 用户安装 XScreen。$\r$\n$\r$\n安装前请在托盘菜单退出正在运行的 XScreen。$\r$\n$\r$\n安装和卸载不会调节显示器；完成后由你从开始菜单启动程序。"
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_INSTFILES
!define MUI_FINISHPAGE_TITLE "XScreen 已安装"
!define MUI_FINISHPAGE_TEXT "可从开始菜单打开 XScreen。$\r$\n$\r$\n安装程序不会自动启动应用。"
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "SimpChinese"

!macro CheckNotRunning
  System::Call 'kernel32::OpenMutexW(i 0x100000, i 0, w "Local\XScreen.UltraGear") p.r0'
  ${If} $0 P<> 0
    System::Call 'kernel32::CloseHandle(p r0)'
    MessageBox MB_OK|MB_ICONEXCLAMATION "请先在 XScreen 托盘菜单选择“退出 XScreen”，然后重新运行安装或卸载程序。" /SD IDOK
    SetErrorLevel 2
    Abort
  ${EndIf}
!macroend

Function .onInit
  SetShellVarContext current
  SetRegView 64
  ${IfNot} ${RunningX64}
    MessageBox MB_OK|MB_ICONSTOP "此安装包需要 64 位 Windows。" /SD IDOK
    Abort
  ${EndIf}
  ${IfNot} ${AtLeastWin10}
    MessageBox MB_OK|MB_ICONSTOP "XScreen 需要 Windows 10 或更高版本。" /SD IDOK
    Abort
  ${EndIf}
  !insertmacro CheckNotRunning
FunctionEnd

Function un.onInit
  SetShellVarContext current
  SetRegView 64
  !insertmacro CheckNotRunning
FunctionEnd

Section "XScreen（必需）" MainSection
  SectionIn RO
  SetOverwrite on
  !include "${INSTALL_FILES}"
  SetOutPath "$INSTDIR"
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  CreateDirectory "$SMPROGRAMS\XScreen"
  CreateShortcut "$SMPROGRAMS\XScreen\XScreen.lnk" "$INSTDIR\XScreen.exe" "" "$INSTDIR\Assets\XScreen.ico"
  CreateShortcut "$SMPROGRAMS\XScreen\卸载 XScreen.lnk" "$INSTDIR\Uninstall.exe"
  WriteRegStr HKCU "Software\XScreen\Install" "InstallDir" "$INSTDIR"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\XScreen" "DisplayName" "XScreen"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\XScreen" "DisplayVersion" "${APP_VERSION}"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\XScreen" "Publisher" "XScreen"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\XScreen" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\XScreen" "DisplayIcon" "$INSTDIR\XScreen.exe"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\XScreen" "UninstallString" '$\"$INSTDIR\Uninstall.exe$\"'
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\XScreen" "NoModify" 1
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\XScreen" "NoRepair" 1
SectionEnd

Section /o "创建桌面快捷方式" DesktopSection
  CreateShortcut "$DESKTOP\XScreen.lnk" "$INSTDIR\XScreen.exe" "" "$INSTDIR\Assets\XScreen.ico"
SectionEnd

Section "Uninstall"
  !include "${UNINSTALL_FILES}"
  Delete "$INSTDIR\Uninstall.exe"
  Delete "$SMPROGRAMS\XScreen\XScreen.lnk"
  Delete "$SMPROGRAMS\XScreen\卸载 XScreen.lnk"
  RMDir "$SMPROGRAMS\XScreen"
  Delete "$DESKTOP\XScreen.lnk"
  DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\XScreen"
  DeleteRegKey HKCU "Software\XScreen\Install"
  ; Non-recursive: retain runtime logs, user preferences and any unowned files.
  RMDir "$INSTDIR"
SectionEnd
