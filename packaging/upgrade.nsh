; Preserve the pinned builder's helpers, but do not execute installed legacy
; uninstallers: versions 0.3.29-0.3.31 reject silent startup before initMultiUser.
!include "${XSCREEN_BUILD_RESOURCES}\generated-install-util.nsh"
!macroundef uninstallOldVersion
!macro uninstallOldVersion ROOT_KEY
  Call XScreenRemovePreviousVersion
!macroend
