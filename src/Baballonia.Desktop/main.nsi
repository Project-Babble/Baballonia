;--------------------------------
;Includes

  !include "MUI2.nsh"
  !include "logiclib.nsh"
  !include "FileFunc.nsh"

;--------------------------------
;Custom defines

  !define NAME "Baballonia"
  !define APPFILE "Baballonia.Desktop.exe"
  !define PUBLISHER "dfgHiatus - Paradigm Reality Enhancement Laboratories"
  !define VERSION "1.1.0.8"
  !define SLUG "${NAME} v${VERSION}"

;--------------------------------
;General

  Name "${NAME}"
  OutFile "${NAME} Setup.exe"
  ;Default install directory in user's AppData folder
  InstallDir "$LOCALAPPDATA\${NAME}"
  InstallDirRegKey HKCU "Software\${NAME}" ""
  RequestExecutionLevel user

;--------------------------------
;Compression
  SetCompressor /SOLID lzma ; "/FINAL" can be added to prevent anything from changing this later down the line.
  SetCompressorDictSize 32
  FileBufSize 64
  ManifestDPIAware true
  ;ManifestLongPathAware true ; disabled until the rest of the app is confirmed to have this setup right
  XPStyle on ; does this even do anything meaningful anymore?

;--------------------------------
;UI

  !define MUI_ICON "Assets\IconOpaque.ico"
  !define MUI_HEADERIMAGE
  !define MUI_WELCOMEFINISHPAGE_BITMAP "Assets\MUI_WELCOMEFINISHPAGE_BITMAP.bmp"
  !define MUI_HEADERIMAGE_BITMAP "Assets\MUI_HEADERIMAGE_BITMAP.bmp"
  !define MUI_ABORTWARNING
  !define MUI_WELCOMEPAGE_TITLE "${SLUG} Setup"

;--------------------------------
;Pages

  ;Installer pages
  !insertmacro MUI_PAGE_WELCOME
  !insertmacro MUI_PAGE_LICENSE "Assets\license.txt"
  !insertmacro MUI_PAGE_COMPONENTS
  !insertmacro MUI_PAGE_DIRECTORY
  !insertmacro MUI_PAGE_INSTFILES
  !insertmacro MUI_PAGE_FINISH

  ;Uninstaller pages
  !insertmacro MUI_UNPAGE_CONFIRM
  !insertmacro MUI_UNPAGE_INSTFILES

  ;Set UI language
  !insertmacro MUI_LANGUAGE "English"

;--------------------------------
;Section - Install App

  Section "-hidden app"
    SectionIn RO
    SetOutPath "$INSTDIR"

    ;Copy all files except Calibration, Firmware and publish folders
    File /r /x "Calibration" /x "Firmware" "bin\Release\net8.0\win-x64\publish\*"

    ;Create Firmware directory
    CreateDirectory "$INSTDIR\Firmware"

    ;Copy Windows-only Firmware tooling
    CreateDirectory "$INSTDIR\Firmware\Windows"
    SetOutPath "$INSTDIR\Firmware\Windows"
    File /r "bin\Release\net8.0\win-x64\publish\Firmware\Windows\*"

    ;Copy firmware over
    CreateDirectory "$INSTDIR\Firmware\Binaries"
    SetOutPath "$INSTDIR\Firmware\Binaries"
    File /r "bin\Release\net8.0\win-x64\publish\Firmware\Binaries\*"

    ;Create Windows-only Calibration tooling
    CreateDirectory "$INSTDIR\Calibration"
    SetOutPath "$INSTDIR\Calibration"
    File /r "bin\Release\net8.0\win-x64\publish\Calibration\Windows"

    ;Reset output path and write registry values
    SetOutPath "$INSTDIR"

    ;Get size to show in Control Panel
    ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
    IntFmt $0 "0x%08X" $0

    WriteUninstaller "$INSTDIR\Uninstall.exe"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${NAME}" "DisplayName" "${NAME}"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${NAME}" "DisplayIcon" "$INSTDIR\${APPFILE}"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${NAME}" "UninstallString" "$INSTDIR\Uninstall.exe"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${NAME}" "DisplayVersion" "${VERSION}"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${NAME}" "Publisher" "${PUBLISHER}"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${NAME}" "InstallLocation" "$INSTDIR"
    WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${NAME}" "EstimatedSize" "$0"

    CreateShortCut "$SMPROGRAMS\${Name}.lnk" "$INSTDIR\${APPFILE}"
    CreateShortCut "$SMPROGRAMS\Uninstall ${Name}.lnk" "$INSTDIR\Uninstall.exe"

    ;Launch app when finished
    ExecShell "" "$INSTDIR\${APPFILE}"

  SectionEnd

;--------------------------------
;Section - Shortcut

  Section "Desktop Shortcut" DeskShort
    CreateShortCut "$DESKTOP\${NAME}.lnk" "$INSTDIR\${APPFILE}"
  SectionEnd

;--------------------------------
;Descriptions

  ;Language strings
  LangString DESC_DeskShort ${LANG_ENGLISH} "Create Shortcut on Desktop."

  ;Assign language strings to sections
  !insertmacro MUI_FUNCTION_DESCRIPTION_BEGIN
  !insertmacro MUI_DESCRIPTION_TEXT ${DeskShort} $(DESC_DeskShort)
  !insertmacro MUI_FUNCTION_DESCRIPTION_END

;--------------------------------
;Remove empty parent directories

  Function un.RMDirUP
    !define RMDirUP '!insertmacro RMDirUPCall'

    !macro RMDirUPCall _PATH
          push '${_PATH}'
          Call un.RMDirUP
    !macroend

    ;$0 - current folder
    ClearErrors

    Exch $0
    ;DetailPrint "ASDF - $0\.."
    RMDir "$0\.."

    IfErrors Skip
    ${RMDirUP} "$0\.."
    Skip:

    Pop $0

  FunctionEnd

;--------------------------------
;Section - Uninstaller

Section "Uninstall"

  ;Prompt to delete local data
  MessageBox MB_YESNO|MB_ICONQUESTION \
    "Do you also want to delete local user data/settings (stored under %APPDATA%\ProjectBabble)?" \
    IDNO skip_userdata

  RMDir /r "$APPDATA\ProjectBabble"

  skip_userdata:

  ;Delete Shortcut
  Delete "$DESKTOP\${NAME}.lnk"

  ;Delete Uninstall
  Delete "$SMPROGRAMS\${Name}.lnk"
  Delete "$SMPROGRAMS\Uninstall ${Name}.lnk"
  DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${NAME}"
  Delete "$INSTDIR\Uninstall.exe"

  ;Delete Folder
  RMDir /r "$INSTDIR"
  ${RMDirUP} "$INSTDIR"

SectionEnd
