; Compile with ISCC /DAppVersion=1.0.0 /DPayloadDir=... /DOutputDir=... jamm-cgf.iss
#ifndef AppVersion
  #error AppVersion is required
#endif
#ifndef PayloadDir
  #error PayloadDir is required
#endif
#ifndef OutputDir
  #define OutputDir "..\..\out\packages"
#endif
[Setup]
AppId={{D2E36E9D-574D-4998-A7D9-D6A58AE5B087}
AppName=JAMM-CGF
AppVersion={#AppVersion}
AppPublisher=JAMM-CGF contributors
AppPublisherURL=https://github.com/varialbe/JAMM-CGF
DefaultDirName={localappdata}\Programs\JAMM-CGF
DefaultGroupName=JAMM-CGF
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename=jamm-cgf_{#AppVersion}_win-x64_setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\cgfmm-gui.exe
LicenseFile={#PayloadDir}\LICENSE.txt
[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{group}\JAMM-CGF"; Filename: "{app}\cgfmm-gui.exe"
[Run]
Filename: "{app}\cgfmm-gui.exe"; Description: "Open JAMM-CGF"; Flags: nowait postinstall skipifsilent
