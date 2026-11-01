Unicode true
RequestExecutionLevel user
ManifestDPIAware true
ManifestSupportedOS all
SetCompressor /SOLID zlib
Name "Inklane"
OutFile "${SETUP_OUTPUT}"
BrandingText "Inklane ${VERSION}"
InstallDir "$LOCALAPPDATA\Programs\Inklane"
ShowInstDetails nevershow
ShowUninstDetails nevershow

!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "FileFunc.nsh"
!include "WordFunc.nsh"
!include "WinVer.nsh"
!include "x64.nsh"

!define ROOT "$LOCALAPPDATA\Programs\Inklane"
!define ENTRY "Software\Microsoft\Windows\CurrentVersion\Uninstall\Inklane.Desktop"
!define LINK "$SMPROGRAMS\Inklane.lnk"
!define OWNER "Inklane.Desktop.Install.1"
!define MUI_ICON "${PRODUCT_ICON}"
!define MUI_UNICON "${PRODUCT_ICON}"
!define MUI_WELCOMEFINISHPAGE_BITMAP "${WIZARD_BITMAP}"
!define MUI_WELCOMEPAGE_TITLE "Inklane ${VERSION}"
!define MUI_WELCOMEPAGE_TEXT "$(WelcomeText)"
!define MUI_FINISHPAGE_RUN "$INSTDIR\Inklane.exe"
!define MUI_FINISHPAGE_RUN_TEXT "$(LaunchText)"
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"
!insertmacro MUI_LANGUAGE "German"

VIProductVersion "${NUMERIC_VERSION}"
VIAddVersionKey /LANG=1033 "ProductName" "Inklane"
VIAddVersionKey /LANG=1033 "CompanyName" "Inklane"
VIAddVersionKey /LANG=1033 "FileDescription" "Inklane Setup"
VIAddVersionKey /LANG=1033 "FileVersion" "${VERSION}"
VIAddVersionKey /LANG=1033 "ProductVersion" "${VERSION}"
VIAddVersionKey /LANG=1033 "LegalCopyright" "Inklane"

LangString WelcomeText ${LANG_ENGLISH} "Set up Inklane for your Windows account.$\r$\n$\r$\nOpen Inklane from the Start menu after installation."
LangString WelcomeText ${LANG_GERMAN} "Inklane für Ihr Windows-Konto einrichten.$\r$\n$\r$\nNach der Installation können Sie Inklane über das Startmenü öffnen."
LangString LaunchText ${LANG_ENGLISH} "Open Inklane"
LangString LaunchText ${LANG_GERMAN} "Inklane öffnen"
LangString Unsupported ${LANG_ENGLISH} "This version of Inklane requires Windows 11 x64."
LangString Unsupported ${LANG_GERMAN} "Diese Version von Inklane benötigt Windows 11 x64."
LangString Busy ${LANG_ENGLISH} "Another Inklane setup is running. Finish it and try again."
LangString Busy ${LANG_GERMAN} "Ein anderes Inklane-Setup wird ausgeführt. Beenden Sie es und versuchen Sie es erneut."
LangString Conflict ${LANG_ENGLISH} "Setup could not verify the existing Inklane installation or Start menu entry. Check the installation in Settings before continuing."
LangString Conflict ${LANG_GERMAN} "Die vorhandene Installation oder der Startmenüeintrag konnte nicht überprüft werden. Prüfen Sie die Installation in den Windows-Einstellungen."
LangString Downgrade ${LANG_ENGLISH} "A newer version of Inklane is already installed."
LangString Downgrade ${LANG_GERMAN} "Eine neuere Version von Inklane ist bereits installiert."
LangString Locked ${LANG_ENGLISH} "Close Inklane and any program using its installation files, then try again."
LangString Locked ${LANG_GERMAN} "Schließen Sie Inklane und alle Programme, die auf die Installationsdateien zugreifen. Versuchen Sie es dann erneut."
LangString Failed ${LANG_ENGLISH} "Setup could not complete. Check the available disk space and your access to the installation folder."
LangString Failed ${LANG_GERMAN} "Das Setup konnte nicht abgeschlossen werden. Prüfen Sie den freien Speicherplatz und Ihre Zugriffsrechte für den Installationsordner."
LangString Leftovers ${LANG_ENGLISH} "Inklane is installed. Setup could not remove all files from the previous version."
LangString Leftovers ${LANG_GERMAN} "Inklane wurde installiert. Einige Dateien der vorherigen Version konnten nicht entfernt werden."

Var OldDir
Var OldVersion
Var OldOrder
Var OldSize
Var Staged
Var Mutex

!macro Fail Message Code
  MessageBox MB_OK|MB_ICONEXCLAMATION "$(${Message})" /SD IDOK
  SetErrorLevel ${Code}
  Abort
!macroend

!macro InitUser
  SetShellVarContext current
  SetRegView 64
  System::Call 'kernel32::GetUserDefaultUILanguage() i.r0'
  IntOp $0 $0 & 0x3ff
  StrCpy $LANGUAGE ${LANG_ENGLISH}
  ${If} $0 = 7
    StrCpy $LANGUAGE ${LANG_GERMAN}
  ${EndIf}
!macroend

!macro LockSetup
  System::Call 'kernel32::CreateMutexW(p 0, i 0, w "Local\Inklane.Desktop.Setup") p.r0 ?e'
  Pop $1
  StrCpy $Mutex $0
  ${If} $0 = 0
  ${OrIf} $1 = 183
    !insertmacro Fail Busy 2
  ${EndIf}
!macroend

!macro RejectReparse Path
  System::Call 'kernel32::GetFileAttributesW(w "${Path}") i.r7'
  ${If} $7 != -1
    IntOp $7 $7 & 0x400
    ${If} $7 != 0
      !insertmacro Fail Conflict 3
    ${EndIf}
  ${EndIf}
!macroend

!macro CheckFileUnlocked Path
  ${If} ${FileExists} "${Path}"
    System::Call 'kernel32::CreateFileW(w "${Path}", i 0x40000000, i 7, p 0, i 3, i 0, p 0) p.r6'
    ${If} $6 = -1
      Goto file_locked
    ${EndIf}
    System::Call 'kernel32::CloseHandle(p r6)'
  ${EndIf}
!macroend

!macro RegisterApplication Folder Version Order Size
  WriteRegStr HKCU "${ENTRY}" "DisplayName" "Inklane"
  WriteRegStr HKCU "${ENTRY}" "Publisher" "Inklane"
  WriteRegStr HKCU "${ENTRY}" "DisplayVersion" "${Version}"
  WriteRegStr HKCU "${ENTRY}" "InstallLocation" "${Folder}"
  WriteRegStr HKCU "${ENTRY}" "DisplayIcon" '"${Folder}\Inklane.exe",0'
  WriteRegStr HKCU "${ENTRY}" "UninstallString" '"${Folder}\Uninstall.exe"'
  WriteRegStr HKCU "${ENTRY}" "QuietUninstallString" '"${Folder}\Uninstall.exe" /S'
  WriteRegDWORD HKCU "${ENTRY}" "EstimatedSize" "${Size}"
  WriteRegDWORD HKCU "${ENTRY}" "NoModify" 1
  WriteRegDWORD HKCU "${ENTRY}" "NoRepair" 1
  WriteRegStr HKCU "${ENTRY}" "VersionOrder" "${Order}"
  WriteRegStr HKCU "${ENTRY}" "Family" "${OWNER}"
!macroend

!include "${PAYLOAD_MANIFEST}"

Function .onInit
  !insertmacro InitUser
  ${IfNot} ${IsNativeAMD64}
  ${OrIfNot} ${AtLeastBuild} 22000
    !insertmacro Fail Unsupported 2
  ${EndIf}
  !insertmacro LockSetup
  !insertmacro RejectReparse "$LOCALAPPDATA\Programs"
  !insertmacro RejectReparse "${ROOT}"

  ReadRegStr $OldDir HKCU "${ENTRY}" "InstallLocation"
  ReadRegStr $OldVersion HKCU "${ENTRY}" "DisplayVersion"
  ReadRegStr $OldOrder HKCU "${ENTRY}" "VersionOrder"
  ReadRegDWORD $OldSize HKCU "${ENTRY}" "EstimatedSize"
  ReadRegStr $0 HKCU "${ENTRY}" "Family"
  ${If} $OldDir == ""
    ReadRegStr $1 HKCU "${ENTRY}" "DisplayName"
    ${If} $1 != ""
    ${OrIf} $0 != ""
    ${OrIf} ${FileExists} "${LINK}"
      !insertmacro Fail Conflict 3
    ${EndIf}
  ${Else}
    ${GetParent} "$OldDir" $1
    ${If} $1 != "${ROOT}"
    ${OrIf} $0 != "${OWNER}"
    ${OrIfNot} ${FileExists} "$OldDir\Uninstall.exe"
      !insertmacro Fail Conflict 3
    ${EndIf}
    !insertmacro RejectReparse "$OldDir"
    FileOpen $1 "$OldDir\.inklane-owner" r
    FileRead $1 $2
    FileClose $1
    ${If} $2 != "${OWNER}:$OldVersion"
      !insertmacro Fail Conflict 3
    ${EndIf}
    ${VersionCompare} "${NUMERIC_VERSION}" "$OldOrder" $1
    ${If} $1 == 2
      !insertmacro Fail Downgrade 4
    ${EndIf}
    !insertmacro CheckFileUnlocked "$OldDir\Inklane.exe"
  ${EndIf}

  ; Install into a fresh directory, including on repair. Unknown existing files are never replaced.
  StrCpy $INSTDIR "${ROOT}\app-${VERSION}"
  StrCpy $1 0
  choose_directory:
    System::Call 'kernel32::GetFileAttributesW(w "$INSTDIR") i.r0'
    ${If} $0 != -1
      IntOp $1 $1 + 1
      ${If} $1 > 999
        !insertmacro Fail Conflict 3
      ${EndIf}
      StrCpy $INSTDIR "${ROOT}\app-${VERSION}-$1"
      Goto choose_directory
    ${EndIf}
  Return
  file_locked:
    !insertmacro Fail Locked 5
FunctionEnd

Section "Inklane"
  ClearErrors
  !insertmacro RejectReparse "$LOCALAPPDATA\Programs"
  !insertmacro RejectReparse "${ROOT}"
  CreateDirectory "${ROOT}"
  IfErrors failed
  ; CreateDirectoryW fails if a folder appeared since the initial check. Cleanup is
  ; permitted only after this process has acquired a completely new directory.
  System::Call 'kernel32::CreateDirectoryW(w "$INSTDIR", p 0) i.r0'
  ${If} $0 = 0
    !insertmacro Fail Conflict 3
  ${EndIf}
  StrCpy $Staged 1
  SetOutPath "$INSTDIR"
  !insertmacro ExtractPayload
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  FileOpen $0 "$INSTDIR\.inklane-owner" w
  FileWrite $0 "${OWNER}:${VERSION}"
  FileClose $0
  IfErrors failed

  ; Only switch the user's entry points after the new payload is completely extracted.
  ClearErrors
  SetOutPath "$INSTDIR"
  CreateShortcut "${LINK}" "$INSTDIR\Inklane.exe" "" "$INSTDIR\Inklane.exe"
  !insertmacro RegisterApplication "$INSTDIR" "${VERSION}" "${NUMERIC_VERSION}" "${SIZE_KIB}"
  IfErrors registration_failed
  StrCpy $Staged 0

  ${If} $OldDir != ""
    ; Its uninstaller recognizes that registration now belongs to this version.
    ClearErrors
    StrCpy $0 1
    ExecWait '"$OldDir\Uninstall.exe" /S _?=$OldDir' $0
    ${IfNot} ${Errors}
    ${AndIf} $0 = 0
      Delete "$OldDir\Uninstall.exe"
      RMDir "$OldDir"
    ${Else}
      MessageBox MB_OK|MB_ICONINFORMATION "$(Leftovers)" /SD IDOK
    ${EndIf}
  ${EndIf}
  SetErrorLevel 0
  Goto done

  registration_failed:
    ${If} $OldDir != ""
      SetOutPath "$OldDir"
      CreateShortcut "${LINK}" "$OldDir\Inklane.exe"
      !insertmacro RegisterApplication "$OldDir" "$OldVersion" "$OldOrder" "$OldSize"
    ${Else}
      Delete "${LINK}"
      DeleteRegKey HKCU "${ENTRY}"
    ${EndIf}
  failed:
    !insertmacro Fail Failed 6
  done:
SectionEnd

Function .onInstFailed
  ${If} $Staged == 1
    !insertmacro RemovePayload
    Delete "$INSTDIR\Uninstall.exe"
    Delete "$INSTDIR\.inklane-owner"
    SetOutPath "$TEMP"
    RMDir "$INSTDIR"
    RMDir "${ROOT}"
  ${EndIf}
FunctionEnd

Function un.onInit
  !insertmacro InitUser
  ; Do not follow an altered uninstall path or a junction into another directory.
  ${GetParent} "$INSTDIR" $0
  ${If} $0 != "${ROOT}"
    !insertmacro Fail Conflict 3
  ${EndIf}
  !insertmacro RejectReparse "$LOCALAPPDATA\Programs"
  !insertmacro RejectReparse "${ROOT}"
  !insertmacro RejectReparse "$INSTDIR"
  FileOpen $0 "$INSTDIR\.inklane-owner" r
  FileRead $0 $1
  FileClose $0
  ${If} $1 != "${OWNER}:${VERSION}"
    !insertmacro Fail Conflict 3
  ${EndIf}
  ReadRegStr $OldDir HKCU "${ENTRY}" "InstallLocation"
  ${If} $OldDir == $INSTDIR
    ReadRegStr $1 HKCU "${ENTRY}" "Family"
    ${If} $1 != "${OWNER}"
      !insertmacro Fail Conflict 3
    ${EndIf}
    !insertmacro LockSetup
  ${EndIf}
  !insertmacro CheckPayloadDirectories
  !insertmacro CheckPayloadAccess
  Return
  file_locked:
    !insertmacro Fail Locked 5
FunctionEnd

Section "Uninstall"
  !insertmacro RemovePayload
  !insertmacro VerifyRemoved
  ${If} $OldDir == $INSTDIR
    Delete "${LINK}"
    DeleteRegKey HKCU "${ENTRY}"
  ${EndIf}
  Delete "$INSTDIR\.inklane-owner"
  Delete "$INSTDIR\Uninstall.exe"
  SetOutPath "$TEMP"
  RMDir "$INSTDIR"
  RMDir "${ROOT}"
  SetErrorLevel 0
  Goto done
  removal_failed:
    !insertmacro Fail Failed 6
  done:
SectionEnd
