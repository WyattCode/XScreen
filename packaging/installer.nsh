; Hook the standard electron-builder assisted installer; retain native WinUI payload.
!include "nsDialogs.nsh"
!define XSCREEN_BUILD_RESOURCES "${__FILEDIR__}"
!ifndef BUILD_UNINSTALLER
Var DesktopChoice
Var DesktopCheckbox
!endif

!macro StopIfXScreenRunning
  System::Call 'kernel32::OpenMutexW(i 0x100000, i 0, w "Local\XScreen.UltraGear") p.r0'
  ${If} $0 P<> 0
    System::Call 'kernel32::CloseHandle(p r0)'
    MessageBox MB_OK|MB_ICONEXCLAMATION "请先在 XScreen 托盘菜单选择“退出 XScreen”，然后重试。" /SD IDOK
    SetErrorLevel 2
    Abort
  ${EndIf}
!macroend

; Never invoke electron-builder's default taskkill/automatic application closing.
!macro customCheckAppRunning
  !insertmacro StopIfXScreenRunning
!macroend
!macro customInit
  !insertmacro StopIfXScreenRunning
  StrCpy $DesktopChoice 0
!macroend
!macro customUnInit
  !insertmacro StopIfXScreenRunning
!macroend

!macro customPageAfterChangeDir
  Page custom XScreenOptions XScreenOptionsLeave
  Function XScreenOptions
    nsDialogs::Create 1018
    Pop $0
    ${If} $0 == error
      Abort
    ${EndIf}
    !insertmacro MUI_HEADER_TEXT "快捷方式" "选择是否创建桌面快捷方式。"
    ${NSD_CreateCheckbox} 0 10u 100% 12u "创建桌面快捷方式"
    Pop $DesktopCheckbox
    ${NSD_SetState} $DesktopCheckbox $DesktopChoice
    nsDialogs::Show
  FunctionEnd
  Function XScreenOptionsLeave
    ${NSD_GetState} $DesktopCheckbox $DesktopChoice
  FunctionEnd
!macroend

!macro customInstall
  ${If} $DesktopChoice == 1
    CreateShortCut "$DESKTOP\XScreen.lnk" "$INSTDIR\XScreen.exe" "" "$INSTDIR\Assets\XScreen.ico"
  ${EndIf}
  ; Migrate only the old hand-written installer's entry at this exact path.
  ReadRegStr $0 HKCU "Software\XScreen\Install" "InstallDir"
  ${If} $0 == $INSTDIR
    DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\XScreen"
    DeleteRegKey HKCU "Software\XScreen\Install"
    Delete "$INSTDIR\Uninstall.exe"
    Delete "$SMPROGRAMS\XScreen\卸载 XScreen.lnk"
  ${EndIf}
!macroend

!macro customRemoveFiles
  ; Use an owned-file list, preserving runtime-data, preferences and unowned files.
  !include "${XSCREEN_BUILD_RESOURCES}\generated-uninstall.nsh"
!macroend
