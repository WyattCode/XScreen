; Native WinUI payload. Keep installation separate from application execution.
!include "nsDialogs.nsh"
!define XSCREEN_BUILD_RESOURCES "${__FILEDIR__}"
Var XScreenLinkPath
Var XScreenLinkTarget
Var XScreenOldDir
Var XScreenMutationMutex
!ifdef BUILD_UNINSTALLER
  !define XSCREEN_PREFIX "un."
  Var XScreenUninstallOrigin
!else
  !define XSCREEN_PREFIX ""
  Var XScreenTargetDir
  Var DesktopChoice
  Var DesktopCheckbox
!endif

!macro XScreenFail MESSAGE
  MessageBox MB_OK|MB_ICONEXCLAMATION "${MESSAGE}" /SD IDOK
  SetErrorLevel 2
  Abort
!macroend

!macro StopIfXScreenRunning
  ${If} $XScreenMutationMutex == ""
  System::Call 'kernel32::OpenMutexW(i 0x100000, i 0, w "Local\XScreen.UltraGear") p.r0 ?e'
  Pop $1
  ${If} $0 P<> 0
    System::Call 'kernel32::CloseHandle(p r0)'
    !insertmacro XScreenFail "请先在 XScreen 托盘菜单选择“退出 XScreen”，然后重试。"
  ${ElseIf} $1 != 2
    ; Access denied must not mean 'not running'.
    !insertmacro XScreenFail "无法确认 XScreen 是否已退出；为避免覆盖正在使用的文件，操作已停止。"
  ${EndIf}
  ${EndIf}
!macroend

; Program.Main uses this same named mutex before constructing its monitor
; transport. Keep the handle until Setup/Uninstall exits, including on abort.
!macro XScreenAcquireMutationLock
  ${If} $XScreenMutationMutex == ""
    System::Call 'kernel32::CreateMutexW(p 0, i 1, w "Local\XScreen.UltraGear") p.r0 ?e'
    Pop $1
    ${If} $0 P<> 0
      ${If} $1 == 183
        System::Call 'kernel32::CloseHandle(p r0)'
        !insertmacro XScreenFail "XScreen 在操作前已启动，请退出程序并重新运行安装器。"
      ${EndIf}
      StrCpy $XScreenMutationMutex $0
    ${Else}
      !insertmacro XScreenFail "无法锁定 XScreen 的安装文件，操作已停止。"
    ${EndIf}
  ${EndIf}
!macroend

!macro XScreenCaptureUninstallOrigin
  GetFullPathName $XScreenUninstallOrigin "$INSTDIR"
!macroend

!macro XScreenAssertPlainPath PATH
  System::Call 'kernel32::GetFileAttributesW(w "${PATH}") i.r0'
  ${If} $0 != -1
    IntOp $0 $0 & 0x400
    ${If} $0 != 0
      !insertmacro XScreenFail "安装路径包含链接或重解析点，请选择普通的 XScreen 文件夹。"
    ${EndIf}
  ${EndIf}
!macroend

; Read the destination without resolving/launching it. Failure means unowned.

!macro XScreenReadShortcut PATH
  StrCpy $XScreenLinkPath "${PATH}"
  Call ${XSCREEN_PREFIX}XScreenReadShortcut
!macroend

; Also used at the last pre-mutation checkpoint for silent /D installs.

!macro customCheckAppRunning
  !insertmacro StopIfXScreenRunning
  !ifdef BUILD_UNINSTALLER
    ; Silent un.onInit calls this before initMultiUser. Only the mutex can be
    ; checked at that point; customUnInit validates the initialized directory.
    ${If} $installMode != ""
      Call un.XScreenValidateDirectory
    ${EndIf}
  !else
  Call ${XSCREEN_PREFIX}XScreenValidateDirectory
    ; Validate the old directory before owned-file cleanup during an upgrade.
    ${If} $XScreenOldDir != ""
    ${AndIf} $XScreenOldDir != $INSTDIR
      StrCpy $XScreenTargetDir $INSTDIR
      StrCpy $INSTDIR $XScreenOldDir
      Call XScreenValidateDirectory
      StrCpy $INSTDIR $XScreenTargetDir
    ${EndIf}
    Call XScreenCheckShortcutCollisions
    !insertmacro XScreenAcquireMutationLock
  !endif
!macroend

!macro customInit
  !insertmacro StopIfXScreenRunning
  ${IfNot} ${AtLeastBuild} 19041
    !insertmacro XScreenFail "XScreen 需要 Windows 10 版本 2004（内部版本 19041）或更新版本。"
  ${EndIf}
  ${If} $installMode != "CurrentUser"
    !insertmacro XScreenFail "此安装包仅支持当前用户安装。"
  ${EndIf}
  ReadRegStr $XScreenOldDir HKCU "${INSTALL_REGISTRY_KEY}" "InstallLocation"
  ${If} $XScreenOldDir == ""
    ReadRegStr $XScreenOldDir HKCU "Software\XScreen\Install" "InstallDir"
  ${EndIf}
  ${If} $XScreenOldDir != ""
    GetFullPathName $XScreenOldDir "$XScreenOldDir"
  ${EndIf}
  StrCpy $DesktopChoice 0
  !insertmacro XScreenReadShortcut "$DESKTOP\XScreen.lnk"
  ${If} $XScreenOldDir != ""
  ${AndIf} $XScreenLinkTarget == "$XScreenOldDir\XScreen.exe"
    StrCpy $DesktopChoice 1
  ${EndIf}
!macroend

!macro customUnInit
  !insertmacro StopIfXScreenRunning
  Call un.XScreenValidateDirectory
  ${If} $XScreenUninstallOrigin != $INSTDIR
    !insertmacro XScreenFail "卸载器的原始目录与当前安装登记不一致，已停止；请使用当前安装目录的卸载入口。"
  ${EndIf}
!macroend

!macro customInstallMode
  ; Hide the otherwise selectable all-users choice when Setup is run as admin.
  StrCpy $isForceCurrentInstall "1"
!macroend


!macro customPageAfterChangeDir
  Page custom XScreenOptions XScreenOptionsLeave
  Function XScreenOptions
    nsDialogs::Create 1018
    Pop $0
    ${If} $0 == error
      !insertmacro XScreenFail "无法创建快捷方式选项页面，安装已停止。"
    ${EndIf}
    !insertmacro MUI_HEADER_TEXT "快捷方式" "开始菜单将创建 XScreen 入口；桌面入口可选。"
    ${NSD_CreateCheckbox} 0 10u 100% 12u "创建桌面快捷方式"
    Pop $DesktopCheckbox
    ${NSD_SetState} $DesktopCheckbox $DesktopChoice
    nsDialogs::Show
  FunctionEnd
  Function XScreenOptionsLeave
    ${NSD_GetState} $DesktopCheckbox $DesktopChoice
    Call XScreenValidateDirectory
    Call XScreenCheckShortcutCollisions
  FunctionEnd
!macroend

!macro customHeader
  !include "${XSCREEN_BUILD_RESOURCES}\functions.nsh"
  !macroundef StartApp
  !include "${XSCREEN_BUILD_RESOURCES}\no-start.nsh"
  ; Reject /allusers before the stock uninstaller can request elevation.
  !macroundef setInstallModePerAllUsers
  !include "${XSCREEN_BUILD_RESOURCES}\per-user.nsh"
!macroend

!macro XScreenDeleteShortcut PATH
  !insertmacro XScreenReadShortcut "${PATH}"
  ${If} $XScreenLinkTarget == "$INSTDIR\XScreen.exe"
    Delete "${PATH}"
  ${EndIf}
!macroend

!macro customInstall
  CreateDirectory "$SMPROGRAMS\XScreen"
  ClearErrors
  CreateShortCut "$SMPROGRAMS\XScreen\XScreen.lnk" "$INSTDIR\XScreen.exe" "" "$INSTDIR\Assets\XScreen.ico"
  ${If} ${Errors}
    !insertmacro XScreenFail "开始菜单快捷方式创建失败；程序文件已安装，可从安装目录启动。"
  ${EndIf}
  ${If} $DesktopChoice == 1
    ClearErrors
    CreateShortCut "$DESKTOP\XScreen.lnk" "$INSTDIR\XScreen.exe" "" "$INSTDIR\Assets\XScreen.ico"
    ${If} ${Errors}
      !insertmacro XScreenFail "桌面快捷方式创建失败；程序文件已安装，可从开始菜单启动。"
    ${EndIf}
  ${Else}
    !insertmacro XScreenDeleteShortcut "$DESKTOP\XScreen.lnk"
  ${EndIf}
  ReadRegStr $0 HKCU "Software\XScreen\Install" "InstallDir"
  ${If} $0 != ""
    GetFullPathName $0 "$0"
    ${If} $0 == $INSTDIR
      !insertmacro XScreenDeleteOwned "Uninstall.exe"
      !insertmacro XScreenReadShortcut "$SMPROGRAMS\XScreen\卸载 XScreen.lnk"
      ${If} $XScreenLinkTarget == "$INSTDIR\Uninstall.exe"
        Delete "$SMPROGRAMS\XScreen\卸载 XScreen.lnk"
      ${EndIf}
      DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\XScreen"
      DeleteRegKey HKCU "Software\XScreen\Install"
    ${EndIf}
  ${EndIf}
!macroend

!macro XScreenDeleteOwned RELATIVE
  ${If} ${FileExists} "$INSTDIR\${RELATIVE}"
    ClearErrors
    Delete "$INSTDIR\${RELATIVE}"
    ${If} ${Errors}
      !insertmacro XScreenFail "无法删除文件 ${RELATIVE}。操作未完成，安装登记已保留，请关闭占用程序后重试。"
    ${EndIf}
  ${EndIf}
!macroend

!macro customRemoveFiles
  ; Silent stock uninstall skips the section's checkAppRunning call. Recheck
  ; identity and acquire the launch gate immediately before any file removal.
  !insertmacro customUnInit
  !insertmacro XScreenAcquireMutationLock
  !include "${XSCREEN_BUILD_RESOURCES}\generated-uninstall.nsh"
  !insertmacro XScreenDeleteShortcut "$DESKTOP\XScreen.lnk"
  !insertmacro XScreenDeleteShortcut "$SMPROGRAMS\XScreen\XScreen.lnk"
  RMDir "$SMPROGRAMS\XScreen"
!macroend
