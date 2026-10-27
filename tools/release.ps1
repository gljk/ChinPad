#requires -Version 5.1
[CmdletBinding()]
param(
    [string]$OutputDir = 'dist',
    [string]$Sdk = (Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'),
    [string]$CertificateThumbprint,
    [string]$TimestampUrl
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$projectRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$project = Join-Path $projectRoot 'ChinPad.csproj'
$livePublish = Join-Path $projectRoot 'publish'
if (-not [IO.Path]::IsPathRooted($OutputDir)) { $OutputDir = Join-Path $projectRoot $OutputDir }
$OutputDir = [IO.Path]::GetFullPath($OutputDir).TrimEnd('\', '/')
if ($OutputDir -eq $projectRoot -or $OutputDir -eq $livePublish -or
    $OutputDir.StartsWith($livePublish + '\', [StringComparison]::OrdinalIgnoreCase) -or
    $OutputDir -eq [IO.Path]::GetPathRoot($OutputDir).TrimEnd('\')) {
    throw 'Choose an artifact folder separate from the project root, drive root and live publish folder.'
}
# Refuse junction/symlink output paths, including existing ancestors, before creating or removing files.
$ancestor = $OutputDir
while ($ancestor) {
    if ((Test-Path -LiteralPath $ancestor) -and
        ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "Output path contains a junction or symbolic link: $ancestor"
    }
    $ancestor = Split-Path -Parent $ancestor
}
$Sdk = (Get-Item -LiteralPath $Sdk -ErrorAction Stop).FullName
[xml]$projectXml = [IO.File]::ReadAllText($project)
$version = [string]$projectXml.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw "Expected a numeric three-part Version in ChinPad.csproj: $version" }
if ($TimestampUrl -and -not $CertificateThumbprint) { throw '-TimestampUrl requires -CertificateThumbprint.' }

function Invoke-Checked([string]$Executable, [string[]]$Arguments) {
    $output = & $Executable @Arguments 2>&1
    $code = $LASTEXITCODE
    $output | ForEach-Object { Write-Host $_ }
    if ($code -ne 0) { throw "$Executable failed with exit code $code." }
    if (($output -join [Environment]::NewLine) -match '(?im)\bwarning\s+[A-Z]+\d+') {
        throw "$Executable produced warnings."
    }
}

$certificate = $null
if ($CertificateThumbprint) {
    $thumbprint = $CertificateThumbprint -replace '\s', ''
    if ($thumbprint -notmatch '^[0-9a-fA-F]{40}$') { throw 'CertificateThumbprint must be a 40-digit certificate thumbprint.' }
    foreach ($store in @('Cert:\CurrentUser\My', 'Cert:\LocalMachine\My')) {
        $certificate = Get-ChildItem -LiteralPath $store -CodeSigningCert |
            Where-Object Thumbprint -eq $thumbprint | Select-Object -First 1
        if ($certificate) { break }
    }
    if (-not $certificate -or -not $certificate.HasPrivateKey) { throw 'A code-signing certificate with a private key was not found.' }
}
function Sign-ReleaseFile([string]$Path) {
    if (-not $certificate) { return }
    $signing = @{LiteralPath=$Path; Certificate=$certificate; HashAlgorithm='SHA256'; ErrorAction='Stop'}
    if ($TimestampUrl) { $signing.TimestampServer = $TimestampUrl }
    $signature = Set-AuthenticodeSignature @signing
    if ($signature.Status -ne 'Valid') { throw "Signing failed for $($Path): $($signature.StatusMessage)" }
    if ($TimestampUrl -and -not $signature.TimeStamperCertificate) { throw "No timestamp was applied to $Path." }
    Write-Host "Signed: $Path"
}

$iscc = Get-Command ISCC.exe -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
$compiler = if ($iscc) { $iscc.Source } else { $null }
if (-not $compiler) {
    $installRoots = @($env:ProgramFiles, [Environment]::GetEnvironmentVariable('ProgramFiles(x86)'), (Join-Path $env:LOCALAPPDATA 'Programs'))
    foreach ($installRoot in $installRoots) {
        if (-not $installRoot) { continue }
        $candidate = Join-Path $installRoot 'Inno Setup 6\ISCC.exe'
        if (Test-Path -LiteralPath $candidate -PathType Leaf) { $compiler = $candidate; break }
    }
}
[IO.Directory]::CreateDirectory($OutputDir) | Out-Null
$stageRoot = Join-Path $OutputDir ('.chinpad-stage-' + [Guid]::NewGuid().ToString('N'))
$payload = Join-Path $stageRoot 'payload'
[IO.Directory]::CreateDirectory($payload) | Out-Null
$previousLanguage = $env:DOTNET_CLI_UI_LANGUAGE
try {
    $env:DOTNET_CLI_UI_LANGUAGE = 'en'
    Invoke-Checked $Sdk @('publish', $project, '-c', 'Release', '-o', $payload, '--no-restore',
        '-p:PublishSingleFile=true', '-p:SelfContained=true', '-p:RuntimeIdentifier=win-x64',
        '-p:DebugType=none', '-p:DebugSymbols=false', '-p:CopyRetryCount=0')
    foreach ($document in @('README.md', 'LICENSE', 'CHANGELOG.md')) {
        Copy-Item -LiteralPath (Join-Path $projectRoot $document) -Destination (Join-Path $payload $document) -Force
    }
    $exe = Join-Path $payload 'ChinPad.exe'
    if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw 'Single-file publish did not produce ChinPad.exe.' }
    $expected = @('ChinPad.exe', 'README.md', 'LICENSE', 'CHANGELOG.md') | Sort-Object
    $actual = @(Get-ChildItem -LiteralPath $payload -Force | Select-Object -ExpandProperty Name | Sort-Object)
    if (Compare-Object $expected $actual) { throw 'Unexpected publish contents; no release artifacts were copied.' }
    Sign-ReleaseFile $exe
    $zipName = "ChinPad-$version-win-x64.zip"
    $setupName = "ChinPad-$version-win-x64-setup.exe"
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory($payload, (Join-Path $stageRoot $zipName),
        [IO.Compression.CompressionLevel]::Optimal, $false)
    $artifacts = @($zipName)
    if ($compiler) {
        Invoke-Checked $compiler @("/DAppVersion=$version", "/DSourceDir=$payload", "/DArtifactDir=$stageRoot",
            (Join-Path $projectRoot 'installer\ChinPad.iss'))
        $setup = Join-Path $stageRoot $setupName
        if (-not (Test-Path -LiteralPath $setup -PathType Leaf)) { throw 'Inno Setup did not produce the expected installer.' }
        Sign-ReleaseFile $setup
        $artifacts += $setupName
    } else {
        Write-Host 'Installer skipped: Inno Setup 6 ISCC.exe was not found on PATH or in common install folders.'
    }
    $checksums = foreach ($artifact in $artifacts) {
        '{0}  {1}' -f (Get-FileHash -LiteralPath (Join-Path $stageRoot $artifact) -Algorithm SHA256).Hash.ToLowerInvariant(), $artifact
    }
    [IO.File]::WriteAllLines((Join-Path $stageRoot 'SHA256SUMS.txt'), [string[]]$checksums, (New-Object Text.UTF8Encoding($false)))
    foreach ($artifact in @($artifacts) + @('SHA256SUMS.txt')) {
        Copy-Item -LiteralPath (Join-Path $stageRoot $artifact) -Destination (Join-Path $OutputDir $artifact) -Force
    }
    # Avoid presenting a stale same-version installer as part of an installer-skipped rerun.
    if (-not $compiler -and (Test-Path -LiteralPath (Join-Path $OutputDir $setupName) -PathType Leaf)) {
        Remove-Item -LiteralPath (Join-Path $OutputDir $setupName) -Force
    }
    Write-Host "ChinPad $version release artifacts:"
    foreach ($artifact in @($artifacts) + @('SHA256SUMS.txt')) {
        $file = Get-Item -LiteralPath (Join-Path $OutputDir $artifact)
        Write-Output ('{0} ({1:N0} bytes)' -f $file.FullName, $file.Length)
    }
} finally {
    $env:DOTNET_CLI_UI_LANGUAGE = $previousLanguage
    # Only our unique, resolved staging directory may be removed recursively.
    $stageFull = [IO.Path]::GetFullPath($stageRoot)
    $prefix = $OutputDir + [IO.Path]::DirectorySeparatorChar
    if (-not $stageFull.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($stageFull) -notmatch '^\.chinpad-stage-[0-9a-f]{32}$') {
        throw 'Refusing to clean a staging directory outside the artifact folder.'
    }
    if (Test-Path -LiteralPath $stageFull) { Remove-Item -LiteralPath $stageFull -Recurse -Force }
}

