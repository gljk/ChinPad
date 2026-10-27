# Releasing ChinPad

1. Prepare a Windows 11 machine with the .NET 10 SDK and cached restore assets for win-x64.
   A new development checkout needs a restore once before the offline build; the release script
   always uses `--no-restore`. Install Inno Setup 6 if you want an installer.
2. Bump `<Version>` in `ChinPad.csproj` and add a short section to `CHANGELOG.md`.
   For the first public release it is `0.8.0`. `AppInfo.ProjectPage` in `AppInfo.cs` is the
   repository page shown in the About window (`github.com/gljk/ChinPad`).
3. Run the engine/UI checks, then build with the SDK executable:

   ```powershell
   & "$env:USERPROFILE\.dotnet\dotnet.exe" build --no-restore   # or plain "dotnet build" when an SDK is on PATH
   ```

4. From the project folder run:

   ```powershell
   .\tools\release.ps1
   # Explicit paths also work from any current directory:
   & "C:\path\to\ChinPad\tools\release.ps1" -OutputDir "C:\path\to\artifacts" -Sdk "$env:USERPROFILE\.dotnet\dotnet.exe"
   ```

   If Windows blocks local scripts, run `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\release.ps1` for that process only.

   `-Sdk` defaults to `%USERPROFILE%\.dotnet\dotnet.exe`. `-OutputDir` defaults to the project's
   `dist\`; relative output paths are relative to the project, regardless of the current directory.
   The script publishes Release, self-contained win-x64, single-file into a new clean staging folder
   inside that output folder, then removes the staging folder. It never uses the live `publish\`.
   Version-specific artifacts are replaced on a rerun; a stale same-version installer is removed if
   its compiler is unavailable. Other versions' artifacts are left alone.

5. Inspect the output:

   - `ChinPad-<version>-win-x64.zip` contains exactly `ChinPad.exe`, `README.md`, `LICENSE` and
     `CHANGELOG.md` at its root. No settings or logs are bundled.
   - `ChinPad-<version>-win-x64-setup.exe` is built if `ISCC.exe` is on PATH or in a common Inno
     Setup 6 install folder. Otherwise the script prints a clear installer-skip message.
     Install Inno Setup 6 and rerun if the release needs an installer. The compiler receives
     `/DAppVersion=<version>`, the staging path and the output path; no installer is ever run.
   - `SHA256SUMS.txt` covers the zip and, when built, the installer from this run. It does not
     checksum itself. Compare each artifact's `Get-FileHash -Algorithm SHA256` result with this file.

6. Extract the zip to a disposable folder and check `ChinPad.exe --version` and first-time setup.
   On your own test machine, verify install, the Start-menu shortcut, the optional startup checkbox
   and uninstall. The installer is per-user at `%LOCALAPPDATA%\Programs\ChinPad` and needs no admin.
   Close ChinPad yourself before installing an update or uninstalling. The uninstaller disables
   ordinary Start at login and leaves `%LOCALAPPDATA%\ChinPad` (settings and logs); it says so in
   its final message. An explicitly created administrator scheduled task is managed separately.

7. Create a GitHub release in the project's **Releases > Draft a new release** page. Create/select
   tag `v<version>` on the tested commit, use title `ChinPad <version>` and copy that version's short
   changelog section into the description. Attach the zip, installer (if built), and `SHA256SUMS.txt`.
   Mention Windows 11 x64, the included runtime, the first-run wizard and the signing status.
   Check the uploaded filenames and checksums, then publish the release.

## SmartScreen and signing

An unsigned download shows **Windows protected your PC**. The user can choose **More info > Run
anyway** for the downloaded ChinPad copy. A real public code-signing certificate identifies the
publisher, but a new signed download can still show a SmartScreen reputation warning.
A self-signed certificate only helps on the signer's own machines after explicitly trusting it; it
does not give other people's downloads public trust or SmartScreen reputation.
See [Microsoft's SmartScreen guidance](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation).

For public releases, obtain a code-signing certificate from a publicly trusted certificate provider.
Make its private key available in `Cert:\CurrentUser\My` (or `Cert:\LocalMachine\My`), according to
the provider's token/key instructions. Then let the release script sign the published exe **before**
zipping and building the installer, and also sign the installer container before checksums:

```powershell
$thumbprint = 'REPLACE_WITH_CERTIFICATE_THUMBPRINT'
.\tools\release.ps1 -CertificateThumbprint $thumbprint -TimestampUrl 'http://timestamp.digicert.com'
```

`-TimestampUrl` is optional. With PowerShell, use an Authenticode timestamp endpoint supported by the
certificate provider. These are the equivalent exact manual commands for an extracted staging exe;
the variable paths below are examples you replace with your own staged exe and certificate:

```powershell
$exe = 'C:\release-staging\ChinPad.exe'
$thumbprint = 'REPLACE_WITH_CERTIFICATE_THUMBPRINT'
$cert = Get-Item -LiteralPath "Cert:\CurrentUser\My\$thumbprint"
Set-AuthenticodeSignature -LiteralPath $exe -Certificate $cert -HashAlgorithm SHA256 -TimestampServer 'http://timestamp.digicert.com'
Get-AuthenticodeSignature -LiteralPath $exe
# Without a timestamp:
Set-AuthenticodeSignature -LiteralPath $exe -Certificate $cert -HashAlgorithm SHA256
```

The Windows SDK's SignTool is an alternative (run from its SDK command prompt, with the same
certificate available in the current user's My store). `/sha1` selects the certificate thumbprint;
`/fd SHA256` sets the file digest, and `/td SHA256` sets the RFC 3161 timestamp digest:

```powershell
signtool sign /s My /sha1 $thumbprint /fd SHA256 /tr 'http://timestamp.digicert.com' /td SHA256 $exe
signtool verify /pa /v $exe
# Without a timestamp:
signtool sign /s My /sha1 $thumbprint /fd SHA256 $exe
# If the certificate is in LocalMachine\My, add /sm to the sign command.
# An installer container can be signed in the same way:
signtool sign /s My /sha1 $thumbprint /fd SHA256 /tr 'http://timestamp.digicert.com' /td SHA256 'C:\artifacts\ChinPad-0.8.0-win-x64-setup.exe'
```

For personal testing only, create and trust a self-signed code-signing certificate on your own
machine, then use the same release-script, PowerShell or SignTool commands. These commands change
your own certificate stores; they are not part of building or installing ChinPad:

```powershell
$cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject 'CN=ChinPad local signing' -CertStoreLocation 'Cert:\CurrentUser\My' -NotAfter (Get-Date).AddYears(2)
$certificateFile = Join-Path $env:TEMP 'ChinPad-local-signing.cer'
Export-Certificate -Cert $cert -FilePath $certificateFile
Import-Certificate -FilePath $certificateFile -CertStoreLocation 'Cert:\CurrentUser\Root'
Import-Certificate -FilePath $certificateFile -CertStoreLocation 'Cert:\CurrentUser\TrustedPublisher'
.\tools\release.ps1 -CertificateThumbprint $cert.Thumbprint
```

The optional signing path checks for a private key and a valid resulting signature (and a timestamp
when requested). Signing the installer container does not sign Inno's embedded uninstaller; that
requires Inno Setup's separate SignTool/SignedUninstaller configuration if desired.
Never change a signed artifact after calculating its checksum. Manual signing must happen before
repacking the zip / building the installer; regenerate `SHA256SUMS.txt` after any artifact change.
Reference: [Set-AuthenticodeSignature](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.security/set-authenticodesignature)
and [SignTool](https://learn.microsoft.com/en-us/windows/win32/seccrypto/signtool).

## Updates

There is no automatic update and ChinPad makes no network requests. Users download the new zip,
exit ChinPad and replace the exe and accompanying documents, keeping any portable `settings.json`.
Installed users can rerun the new per-user installer after exiting. Settings in
`%LOCALAPPDATA%\ChinPad` are kept across updates and uninstall/reinstall.
