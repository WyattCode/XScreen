; Emitted after the stock template declares installMode and FileFunc.
!ifndef BUILD_UNINSTALLER
Function XScreenRemovePreviousVersion
  !insertmacro StopIfXScreenRunning
  ${If} $installMode != "CurrentUser"
    !insertmacro XScreenFail "仅允许升级当前用户的 XScreen 安装。"
  ${EndIf}
  ReadRegStr $XScreenOldDir HKCU "${INSTALL_REGISTRY_KEY}" "InstallLocation"
  ${If} $XScreenOldDir == ""
    ClearErrors
    StrCpy $R0 0
    Return
  ${EndIf}
  ReadRegStr $0 HKCU "${UNINSTALL_REGISTRY_KEY}" "DisplayVersion"
  ${If} $0 != "0.3.29"
  ${AndIf} $0 != "0.3.30"
  ${AndIf} $0 != "0.3.31"
    !insertmacro XScreenFail "无法确认旧安装的文件范围，已停止升级；不会运行未知的旧卸载器。"
  ${EndIf}
  StrCpy $XScreenTargetDir $INSTDIR
  StrCpy $INSTDIR $XScreenOldDir
  Call XScreenValidateDirectory
  ; Supported previous payloads are subsets of this version's owned list.
  ; Registration is kept on failure so the user can retry.
  !include "${XSCREEN_BUILD_RESOURCES}\generated-uninstall.nsh"
  !insertmacro XScreenDeleteShortcut "$DESKTOP\XScreen.lnk"
  !insertmacro XScreenDeleteShortcut "$SMPROGRAMS\XScreen\XScreen.lnk"
  RMDir "$SMPROGRAMS\XScreen"
  StrCpy $INSTDIR $XScreenTargetDir
  ClearErrors
  StrCpy $R0 0
FunctionEnd
!endif

Function ${XSCREEN_PREFIX}XScreenReadShortcut
  Push $0
  Push $1
  Push $2
  Push $3
  Push $4
  StrCpy $XScreenLinkTarget ""
  IfFileExists "$XScreenLinkPath" 0 shortcut_done
  System::Call 'ole32::CoInitialize(p 0) i.r4'
  System::Call 'ole32::CoCreateInstance(g "{00021401-0000-0000-C000-000000000046}", p 0, i 1, g "{000214F9-0000-0000-C000-000000000046}", *p .r1) i.r0'
  ${If} $0 == 0
    System::Call '$1->0(g "{0000010b-0000-0000-C000-000000000046}", *p .r2) i.r0'
    ${If} $0 == 0
      System::Call '$2->5(w "$XScreenLinkPath", i 0) i.r0'
      ${If} $0 == 0
        ; IShellLinkW::GetPath, SLGP_RAWPATH; no target resolution.
        System::Call '$1->3(w .r3, i ${NSIS_MAX_STRLEN}, p 0, i 4) i.r0'
        ${If} $0 == 0
          GetFullPathName $XScreenLinkTarget "$3"
        ${EndIf}
      ${EndIf}
      System::Call '$2->2()'
    ${EndIf}
    System::Call '$1->2()'
  ${EndIf}
  ${If} $4 >= 0
    System::Call 'ole32::CoUninitialize()'
  ${EndIf}
  shortcut_done:
  Pop $4
  Pop $3
  Pop $2
  Pop $1
  Pop $0
FunctionEnd

Function ${XSCREEN_PREFIX}XScreenValidateDirectory
  ${If} $installMode != "CurrentUser"
    !insertmacro XScreenFail "此安装包仅支持当前用户安装。"
  ${EndIf}
  StrCpy $0 $INSTDIR 2 1
  ${If} $0 != ":\"
    !insertmacro XScreenFail "请选择本地磁盘上的绝对路径。"
  ${EndIf}
  GetFullPathName $INSTDIR "$INSTDIR"
  ${GetFileName} "$INSTDIR" $0
  ${If} $0 != "XScreen"
    !insertmacro XScreenFail "请选择以 XScreen 结尾的专用安装文件夹，例如 D:\Apps\XScreen。"
  ${EndIf}
  StrCpy $2 $INSTDIR
  ${Do}
    ${If} $2 == $WINDIR
      !insertmacro XScreenFail "不能安装到 Windows 系统目录。"
    ${EndIf}
    !insertmacro XScreenAssertPlainPath "$2"
    ${GetParent} "$2" $3
    ${If} $3 == $2
    ${OrIf} $3 == ""
      ${Break}
    ${EndIf}
    StrCpy $2 $3
  ${Loop}
  !include "${XSCREEN_BUILD_RESOURCES}\generated-paths.nsh"
  ReadRegStr $XScreenOldDir HKCU "${INSTALL_REGISTRY_KEY}" "InstallLocation"
  ${If} $XScreenOldDir != ""
    GetFullPathName $XScreenOldDir "$XScreenOldDir"
  ${EndIf}
  !ifdef BUILD_UNINSTALLER
    ${If} $XScreenOldDir != $INSTDIR
      !insertmacro XScreenFail "卸载目录与当前用户的安装登记不一致，已停止以避免误删文件。"
    ${EndIf}
  !else
    ${If} $XScreenOldDir == $INSTDIR
      Return
    ${EndIf}
    ReadRegStr $0 HKCU "Software\XScreen\Install" "InstallDir"
    ${If} $0 != ""
      GetFullPathName $0 "$0"
      ${If} $0 == $INSTDIR
      ${AndIf} ${FileExists} "$INSTDIR\XScreen.exe"
      ${AndIf} ${FileExists} "$INSTDIR\XScreen.Core.dll"
        Return
      ${EndIf}
    ${EndIf}
    FindFirst $0 $1 "$INSTDIR\*"
    ${DoWhile} $1 != ""
      ${If} $1 != "."
      ${AndIf} $1 != ".."
        FindClose $0
        !insertmacro XScreenFail "该文件夹已有其他内容。请选择空的 XScreen 文件夹，或已登记的 XScreen 安装目录。"
      ${EndIf}
      FindNext $0 $1
    ${Loop}
    FindClose $0
  !endif
FunctionEnd

!ifndef BUILD_UNINSTALLER
Function XScreenCheckShortcutCollisions
  !insertmacro XScreenAssertPlainPath "$SMPROGRAMS\XScreen"
  !insertmacro XScreenAssertPlainPath "$SMPROGRAMS\XScreen\XScreen.lnk"
  !insertmacro XScreenAssertPlainPath "$DESKTOP\XScreen.lnk"
  !insertmacro XScreenReadShortcut "$SMPROGRAMS\XScreen\XScreen.lnk"
  IfFileExists "$SMPROGRAMS\XScreen\XScreen.lnk" 0 menu_ok
  ${If} $XScreenLinkTarget != "$INSTDIR\XScreen.exe"
  ${AndIf} $XScreenLinkTarget != "$XScreenOldDir\XScreen.exe"
    !insertmacro XScreenFail "开始菜单已有其他目标的 XScreen 快捷方式，请先自行改名或移走。"
  ${EndIf}
  menu_ok:
  ${If} $DesktopChoice == 1
  ${OrIf} $XScreenOldDir != ""
    ; Older released uninstallers deleted this name unconditionally, even when
    ; desktop creation was unchecked. Reject collisions before invoking them.
    !insertmacro XScreenReadShortcut "$DESKTOP\XScreen.lnk"
    IfFileExists "$DESKTOP\XScreen.lnk" 0 desktop_ok
    ${If} $XScreenLinkTarget != "$INSTDIR\XScreen.exe"
    ${AndIf} $XScreenLinkTarget != "$XScreenOldDir\XScreen.exe"
      !insertmacro XScreenFail "桌面已有其他目标的 XScreen 快捷方式，请先自行改名或取消桌面快捷方式。"
    ${EndIf}
  ${EndIf}
  desktop_ok:
FunctionEnd
!endif
