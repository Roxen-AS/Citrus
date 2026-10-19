Unicode true
!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "x64.nsh"
!include "FileFunc.nsh"
!ifndef GENERATED
!error "Pass GENERATED (absolute path to generated ownership includes)"
!endif
!include "${GENERATED}\identity.nsh"
Name "Citrus"
OutFile "${OUTPUT}"
InstallDir "$LOCALAPPDATA\Programs\Citrus"
RequestExecutionLevel user
SetCompressor /SOLID lzma
SetDatablockOptimize on
VIProductVersion "0.3.0.0"
VIAddVersionKey "ProductName" "Citrus"
VIAddVersionKey "FileDescription" "Citrus unsigned Windows installer candidate"
VIAddVersionKey "FileVersion" "0.3.0-preview.1"
VIAddVersionKey "LegalCopyright" "Citrus contributors"
ShowInstDetails show
ShowUninstDetails show
!define MUI_ABORTWARNING
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"
Var Guard
Var SessionGuard
Var IdentityToken
Var IdentityBuffer
Var IdentitySidText
Var IdentityLength
Var UserSid
Var SessionId
Var ProcessSnapshot
Var ProcessEntry
Var DeleteFailed
Var ExistingInventory
Var ExistingLocation
Var RecoveryIdentity
Var OwnedInstallation

!macro Helpers PREFIX
Function ${PREFIX}Fail
  MessageBox MB_OK|MB_ICONSTOP "Citrus cannot safely change this installation. Choose Quit Citrus from the tray in every session, then retry. Existing installations with a different inventory must be uninstalled first. Check that the installation path contains no links and is writable." /SD IDOK
  SetErrorLevel 23
  Abort
FunctionEnd

Function ${PREFIX}CheckPath
  Pop $0
  System::Call 'kernel32::GetFileAttributesW(w r0) i.r1 ?e'
  Pop $2
  ${If} $1 == -1
    ${If} $2 != 2
    ${AndIf} $2 != 3
      Call ${PREFIX}Fail
    ${EndIf}
  ${Else}
    IntOp $1 $1 & 0x400
    ${If} $1 != 0
      Call ${PREFIX}Fail
    ${EndIf}
  ${EndIf}
FunctionEnd

Function ${PREFIX}CleanupIdentity
  ${If} $IdentitySidText != 0
    System::Call 'kernel32::LocalFree(p $IdentitySidText)'
    StrCpy $IdentitySidText 0
  ${EndIf}
  ${If} $IdentityBuffer != 0
    System::Free $IdentityBuffer
    StrCpy $IdentityBuffer 0
  ${EndIf}
  ${If} $IdentityToken != 0
    System::Call 'kernel32::CloseHandle(p $IdentityToken)'
    StrCpy $IdentityToken 0
  ${EndIf}
FunctionEnd

Function ${PREFIX}AcquireGuards
  StrCpy $IdentityToken 0
  StrCpy $IdentityBuffer 0
  StrCpy $IdentitySidText 0
  StrCpy $Guard 0
  StrCpy $SessionGuard 0
  System::Call 'kernel32::GetCurrentProcess() p.r0'
  System::Call 'advapi32::OpenProcessToken(p r0, i 0x0008, *p.r1) i.r0'
  ${If} $0 == 0
    Goto identity_error
  ${EndIf}
  StrCpy $IdentityToken $1
  System::Call 'advapi32::GetTokenInformation(p $IdentityToken, i 1, p 0, i 0, *i.r3) i.r0 ?e'
  Pop $2
  ${If} $0 != 0
  ${OrIf} $2 != 122
  ${OrIf} $3 <= 0
    Goto identity_error
  ${EndIf}
  StrCpy $IdentityLength $3
  System::Alloc $IdentityLength
  Pop $IdentityBuffer
  ${If} $IdentityBuffer == 0
    Goto identity_error
  ${EndIf}
  System::Call 'advapi32::GetTokenInformation(p $IdentityToken, i 1, p $IdentityBuffer, i $IdentityLength, *i.r3) i.r0'
  ${If} $0 == 0
    Goto identity_error
  ${EndIf}
  System::Call '*$IdentityBuffer(p.r1)'
  System::Call 'advapi32::ConvertSidToStringSidW(p r1, *p.r2) i.r0'
  ${If} $0 == 0
    Goto identity_error
  ${EndIf}
  StrCpy $IdentitySidText $2
  System::Call '*$IdentitySidText(&w${NSIS_MAX_STRLEN} .r3)'
  StrCpy $UserSid $3
  System::Call 'kernel32::GetCurrentProcessId() i.r0'
  System::Call 'kernel32::ProcessIdToSessionId(i r0, *i.r1) i.r0'
  ${If} $0 == 0
    Goto identity_error
  ${EndIf}
  StrCpy $SessionId $1
  Call ${PREFIX}CleanupIdentity
  System::Call 'kernel32::CreateMutexW(p 0, i 0, w "Global\Citrus-Install-$UserSid") p.r0'
  ${If} $0 == 0
    Call ${PREFIX}Fail
  ${EndIf}
  StrCpy $Guard $0
  System::Call 'kernel32::WaitForSingleObject(p $Guard, i 0) i.r0'
  ${If} $0 != 0
  ${AndIf} $0 != 128
    Call ${PREFIX}Fail
  ${EndIf}
  System::Call 'kernel32::CreateMutexW(p 0, i 0, w "Local\Citrus-$UserSid-$SessionId") p.r0'
  ${If} $0 == 0
    Call ${PREFIX}Fail
  ${EndIf}
  StrCpy $SessionGuard $0
  System::Call 'kernel32::WaitForSingleObject(p $SessionGuard, i 0) i.r0'
  ${If} $0 != 0
  ${AndIf} $0 != 128
    Call ${PREFIX}Fail
  ${EndIf}
  # Snapshot spans all sessions. Conservatively reject any account's app process.
  # PROCESSENTRY32W is 556 bytes in this x86 Unicode installer, pointer field is 4 bytes.
  System::Call 'kernel32::CreateToolhelp32Snapshot(i 2, i 0) p.r0'
  ${If} $0 == -1
    Call ${PREFIX}Fail
  ${EndIf}
  StrCpy $ProcessSnapshot $0
  System::Alloc 556
  Pop $ProcessEntry
  ${If} $ProcessEntry == 0
    System::Call 'kernel32::CloseHandle(p $ProcessSnapshot)'
    Call ${PREFIX}Fail
  ${EndIf}
  System::Call '*$ProcessEntry(i 556)'
  System::Call 'kernel32::Process32FirstW(p $ProcessSnapshot, p $ProcessEntry) i.r0 ?e'
  Pop $2
  process_loop:
  ${If} $0 == 0
    ${If} $2 != 18
      Goto process_error
    ${EndIf}
    Goto process_done
  ${EndIf}
  System::Call '*$ProcessEntry(i, i, i, p, i, i, i, i, i, &w260 .r1)'
  System::Call 'kernel32::lstrcmpiW(w r1, w "Citrus.Windows.exe") i.r0'
  ${If} $0 == 0
    Goto process_error
  ${EndIf}
  System::Call 'kernel32::Process32NextW(p $ProcessSnapshot, p $ProcessEntry) i.r0 ?e'
  Pop $2
  Goto process_loop
  process_error:
    System::Free $ProcessEntry
    System::Call 'kernel32::CloseHandle(p $ProcessSnapshot)'
    Call ${PREFIX}Fail
  process_done:
    System::Free $ProcessEntry
    System::Call 'kernel32::CloseHandle(p $ProcessSnapshot)'
    Return
  identity_error:
    Call ${PREFIX}CleanupIdentity
    Call ${PREFIX}Fail
FunctionEnd

Function ${PREFIX}ReleaseGuards
  ${If} $SessionGuard != 0
    System::Call 'kernel32::ReleaseMutex(p $SessionGuard)'
    System::Call 'kernel32::CloseHandle(p $SessionGuard)'
    StrCpy $SessionGuard 0
  ${EndIf}
  ${If} $Guard != 0
    System::Call 'kernel32::ReleaseMutex(p $Guard)'
    System::Call 'kernel32::CloseHandle(p $Guard)'
    StrCpy $Guard 0
  ${EndIf}
FunctionEnd

Function ${PREFIX}ReadOwnership
  StrCpy $OwnedInstallation 0
  ReadRegStr $ExistingInventory HKCU "Software\Citrus" "PackageInventory"
  ReadRegStr $ExistingLocation HKCU "Software\Citrus" "InstallLocation"
  ReadRegStr $RecoveryIdentity HKCU "Software\Citrus" "RecoveryIdentity"
  ${If} $ExistingInventory != ""
  ${AndIf} $ExistingInventory != "${INVENTORY_HASH}"
    Call ${PREFIX}Fail
  ${EndIf}
  ${If} $ExistingLocation != ""
  ${AndIf} $ExistingLocation != $INSTDIR
    Call ${PREFIX}Fail
  ${EndIf}
  ${If} $RecoveryIdentity != ""
  ${AndIf} $RecoveryIdentity != "$INSTDIR|${INVENTORY_HASH}"
    Call ${PREFIX}Fail
  ${EndIf}
  ${If} $RecoveryIdentity == "$INSTDIR|${INVENTORY_HASH}"
    StrCpy $OwnedInstallation 1
    Return
  ${EndIf}
  ${If} $ExistingInventory == "${INVENTORY_HASH}"
  ${AndIf} $ExistingLocation == $INSTDIR
    StrCpy $OwnedInstallation 1
    Return
  ${EndIf}
  # Partial unrelated/legacy registration without our receipt is not ownership.
  ${If} $ExistingInventory != ""
  ${OrIf} $ExistingLocation != ""
    Call ${PREFIX}Fail
  ${EndIf}
FunctionEnd

Function ${PREFIX}CheckInstallPaths
  # Validate every existing ancestor, including LocalAppData and its parents.
  StrCpy $3 $INSTDIR
  path_loop:
    Push $3
    Call ${PREFIX}CheckPath
    ${GetParent} "$3" $4
    ${If} $4 != ""
    ${AndIf} $4 != $3
      StrCpy $3 $4
      Goto path_loop
    ${EndIf}
  !include "${GENERATED}\paths.nsh"
  Push "$INSTDIR\Uninstall.exe"
  Call ${PREFIX}CheckPath
FunctionEnd
!macroend
!insertmacro Helpers ""
!insertmacro Helpers "un."

Function CheckDestination
  Call ReadOwnership
  ${If} $OwnedInstallation == 0
    System::Call 'kernel32::GetFileAttributesW(w "$INSTDIR") i.r0'
    ${If} $0 != -1
      Call Fail
    ${EndIf}
  ${EndIf}
FunctionEnd

Function FlushOwnership
  # SetRegView does not affect direct APIs: request HKCU's 64-bit view explicitly.
  System::Call 'advapi32::RegOpenKeyExW(p 0x80000001, w "Software\Citrus", i 0, i 0x101, *p.r1) i.r0'
  ${If} $0 != 0
    Call Fail
  ${EndIf}
  System::Call 'advapi32::RegFlushKey(p r1) i.r2'
  System::Call 'advapi32::RegCloseKey(p r1)'
  ${If} $2 != 0
    Call Fail
  ${EndIf}
FunctionEnd

Function EstablishOwnership
  # Single validated receipt is durable BEFORE any install-tree creation.
  # If writing/flushing it fails, no new directory or ordinary payload exists.
  ClearErrors
  WriteRegStr HKCU "Software\Citrus" "RecoveryIdentity" "$INSTDIR|${INVENTORY_HASH}"
  IfErrors 0 +2
  Call Fail
  Call FlushOwnership
  ClearErrors
  CreateDirectory "$INSTDIR"
  IfErrors 0 +2
  Call Fail
  SetOutPath "$INSTDIR"
  ClearErrors
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  IfErrors 0 +2
  Call Fail
  # A partial/missing uninstaller can be rewritten by the same setup using the
  # receipt; a complete one already accepts that receipt without these two values.
  ClearErrors
  WriteRegStr HKCU "Software\Citrus" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "Software\Citrus" "PackageInventory" "${INVENTORY_HASH}"
  IfErrors 0 +2
  Call Fail
  Call FlushOwnership
FunctionEnd

Function .onInit
  ${IfNot} ${RunningX64}
    MessageBox MB_OK "Citrus requires Windows x64." /SD IDOK
    SetErrorLevel 23
    Abort
  ${EndIf}
  SetShellVarContext current
  SetRegView 64
  # Ignore /D overrides: ownership is constrained to this one per-user location.
  StrCpy $INSTDIR "$LOCALAPPDATA\Programs\Citrus"
  Call AcquireGuards
  Call CheckInstallPaths
  Call CheckDestination
FunctionEnd
Function .onGUIEnd
  Call ReleaseGuards
FunctionEnd
Function un.onInit
  SetShellVarContext current
  SetRegView 64
  ${If} $INSTDIR != "$LOCALAPPDATA\Programs\Citrus"
    Call un.Fail
  ${EndIf}
  Call un.ReadOwnership
  ${If} $OwnedInstallation != 1
    Call un.Fail
  ${EndIf}
  Call un.AcquireGuards
  Call un.CheckInstallPaths
FunctionEnd
Function un.onGUIEnd
  Call un.ReleaseGuards
FunctionEnd

Section "Citrus (required)" MainSection
  SectionIn RO
  Call CheckInstallPaths
  Call CheckDestination
  Call EstablishOwnership
  SetOverwrite on
  !include "${GENERATED}\install.nsh"
  ClearErrors
  CreateDirectory "$SMPROGRAMS\Citrus"
  CreateShortcut "$SMPROGRAMS\Citrus\Citrus.lnk" "$INSTDIR\Citrus.Windows.exe"
  CreateShortcut "$SMPROGRAMS\Citrus\Uninstall Citrus.lnk" "$INSTDIR\Uninstall.exe"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Citrus" "DisplayName" "Citrus (unsigned candidate)"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Citrus" "DisplayVersion" "0.3.0-preview.1"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Citrus" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Citrus" "DisplayIcon" "$INSTDIR\Citrus.Windows.exe"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Citrus" "UninstallString" '$\"$INSTDIR\Uninstall.exe$\"'
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Citrus" "QuietUninstallString" '$\"$INSTDIR\Uninstall.exe$\" /S'
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Citrus" "NoModify" 1
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Citrus" "NoRepair" 1
  IfErrors 0 +2
  Call Fail
SectionEnd
Section /o "Desktop shortcut" DesktopSection
  ClearErrors
  CreateShortcut "$DESKTOP\Citrus.lnk" "$INSTDIR\Citrus.Windows.exe"
  WriteRegDWORD HKCU "Software\Citrus" "DesktopShortcut" 1
  IfErrors 0 +2
  Call Fail
SectionEnd

Section "Uninstall"
  Call un.CheckInstallPaths
  StrCpy $DeleteFailed 0
  SetOutPath "$TEMP"
  !include "${GENERATED}\uninstall.nsh"
  ${If} $DeleteFailed != 0
    MessageBox MB_OK|MB_ICONSTOP "Some Citrus files could not be removed. Close applications using this installation and retry uninstall. Registration has been retained." /SD IDOK
    SetErrorLevel 24
    Abort
  ${EndIf}
  ReadRegStr $0 HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "Citrus"
  ${If} $0 == '$\"$INSTDIR\Citrus.Windows.exe$\" --background'
    DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "Citrus"
  ${EndIf}
  Delete "$SMPROGRAMS\Citrus\Citrus.lnk"
  Delete "$SMPROGRAMS\Citrus\Uninstall Citrus.lnk"
  RMDir "$SMPROGRAMS\Citrus"
  ReadRegDWORD $0 HKCU "Software\Citrus" "DesktopShortcut"
  ${If} $0 == 1
    Delete "$DESKTOP\Citrus.lnk"
  ${EndIf}
  ClearErrors
  Delete "$INSTDIR\Uninstall.exe"
  IfErrors 0 +2
  Call un.Fail
  DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Citrus"
  DeleteRegKey HKCU "Software\Citrus"
  RMDir "$INSTDIR"
SectionEnd
