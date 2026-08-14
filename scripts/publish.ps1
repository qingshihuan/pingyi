param(
    [ValidateSet("win-x64", "linux-x64")]
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release",
    [string]$Version = "0.1.0",
    [string]$OfflineModelSource,
    [ValidateSet("Standard", "Complete")]
    [string]$Edition = "Standard",
    [string]$LlamaRuntimeSource,
    [string]$InnoCompiler,
    [string]$SignTool,
    [string]$SigningCertificateThumbprint,
    [string]$TimestampUrl = "https://timestamp.digicert.com",
    [switch]$SkipEngine,
    [switch]$BuildInstaller
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
if ($Version.IndexOfAny([IO.Path]::GetInvalidFileNameChars()) -ge 0) {
    throw "Version contains characters that are invalid in a file name."
}
$editionSuffix = if ($Edition -eq "Complete") { "-complete" } else { "" }
$output = Join-Path $projectRoot "artifacts\publish\$Runtime-$Version$editionSuffix"
$engineOutput = Join-Path $projectRoot "artifacts\engine-host\$Runtime"
$engineVenv = Join-Path $projectRoot ".venv-engine"
$engineExecutable = if ($Runtime -eq "win-x64") {
    Join-Path $engineOutput "pingyi-engine\pingyi-engine.exe"
} else {
    Join-Path $engineOutput "pingyi-engine\pingyi-engine"
}

if (-not $SkipEngine) {
    if ($Runtime -ne "win-x64") {
        throw "Build the Linux engine on Ubuntu with scripts/build-engine.sh before publishing linux-x64."
    }
    & (Join-Path $PSScriptRoot "build-engine.ps1")
    if ($LASTEXITCODE -ne 0) { throw "Failed to build the local engine." }
}
elseif (-not (Test-Path $engineExecutable)) {
    throw "The local engine is missing. Remove -SkipEngine or build it first."
}
if (-not (Test-Path -LiteralPath (Join-Path $engineVenv "Scripts\python.exe") -PathType Leaf)) {
    & (Join-Path $PSScriptRoot "setup-engine.ps1")
    if ($LASTEXITCODE -ne 0) { throw "Failed to prepare the local engine license environment." }
}

$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot "artifacts"))
$outputFullPath = [IO.Path]::GetFullPath($output)
$artifactsPrefix = $artifactsRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $outputFullPath.StartsWith($artifactsPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Publish output must stay inside $artifactsRoot"
}
if (Test-Path -LiteralPath $outputFullPath) {
    Remove-Item -LiteralPath $outputFullPath -Recurse -Force
}

dotnet publish (Join-Path $projectRoot "src\PingYi.App\PingYi.App.csproj") `
    -c $Configuration -r $Runtime --self-contained true -o $output `
    -p:Version=$Version `
    -p:PublishSingleFile=false `
    -p:PublishTrimmed=true `
    -p:TrimMode=partial `
    -p:PublishReadyToRun=false `
    -p:DebugType=None `
    -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw "Failed to publish PingYi." }

Get-ChildItem -LiteralPath $output -Recurse -File -Filter "*.pdb" | Remove-Item -Force

if (Test-Path $engineExecutable) {
    $engineTarget = Join-Path $output "engine-host"
    if (Test-Path $engineTarget) { Remove-Item -LiteralPath $engineTarget -Recurse -Force }
    New-Item -ItemType Directory -Path $engineTarget | Out-Null
    Copy-Item (Join-Path (Split-Path -Parent $engineExecutable) "*") $engineTarget -Recurse -Force
}

$offlineModelTarget = Join-Path $output "offline-models"
if ([string]::IsNullOrWhiteSpace($OfflineModelSource)) {
    & (Join-Path $PSScriptRoot "prepare-offline-models.ps1") -Destination $offlineModelTarget
} else {
    & (Join-Path $PSScriptRoot "prepare-offline-models.ps1") `
        -Source $OfflineModelSource `
        -Destination $offlineModelTarget
}
if ($LASTEXITCODE -ne 0) { throw "Failed to prepare offline baseline models." }

if ($Edition -eq "Complete") {
    if ([string]::IsNullOrWhiteSpace($LlamaRuntimeSource) -or
        -not (Test-Path -LiteralPath $LlamaRuntimeSource -PathType Container)) {
        throw "Complete edition requires -LlamaRuntimeSource with prepared Vulkan and CPU runtimes."
    }
    $llamaRuntimeTarget = Join-Path $output "llama-runtime"
    New-Item -ItemType Directory -Path $llamaRuntimeTarget | Out-Null
    Copy-Item (Join-Path $LlamaRuntimeSource "*") $llamaRuntimeTarget -Recurse -Force
    [IO.File]::WriteAllText(
        (Join-Path $output "pingyi-complete.edition"),
        "PingYi Complete`n",
        [Text.UTF8Encoding]::new($false))
}

Copy-Item -LiteralPath (Join-Path $projectRoot "LICENSE") -Destination $output -Force
Copy-Item -LiteralPath (Join-Path $projectRoot "THIRD_PARTY_NOTICES.md") -Destination $output -Force
& (Join-Path $engineVenv "Scripts\python.exe") `
    (Join-Path $PSScriptRoot "collect-release-licenses.py") $output
if ($LASTEXITCODE -ne 0) { throw "Failed to collect the release license bundle." }
& (Join-Path $engineVenv "Scripts\python.exe") `
    (Join-Path $PSScriptRoot "audit-release-dependencies.py") --require-licenses $output
if ($LASTEXITCODE -ne 0) { throw "The release dependency audit failed." }

function Get-CodeSignTool {
    if ([string]::IsNullOrWhiteSpace($SigningCertificateThumbprint)) { return $null }
    $normalizedThumbprint = $SigningCertificateThumbprint.Replace(" ", "")
    if ($normalizedThumbprint -notmatch "^[0-9A-Fa-f]{40}$") {
        throw "SigningCertificateThumbprint must be a 40-character SHA-1 certificate thumbprint."
    }
    if ($TimestampUrl -notmatch "^https://") {
        throw "TimestampUrl must use HTTPS."
    }
    $candidates = @(
        $SignTool,
        $env:PINGYI_SIGNTOOL,
        (Get-Command signtool.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -First 1)
    ) | Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Leaf) }
    if (-not $candidates) {
        $windowsKits = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"
        if (Test-Path -LiteralPath $windowsKits -PathType Container) {
            $candidates = Get-ChildItem -LiteralPath $windowsKits -Filter signtool.exe -Recurse -File `
                | Where-Object { $_.DirectoryName -match "\\x64$" } `
                | Sort-Object FullName -Descending `
                | Select-Object -ExpandProperty FullName -First 1
        }
    }
    $resolved = $candidates | Select-Object -First 1
    if (-not $resolved) {
        throw "signtool.exe was not found. Install the Windows SDK, pass -SignTool, or omit -SigningCertificateThumbprint."
    }
    return [pscustomobject]@{
        Path = $resolved
        Thumbprint = $normalizedThumbprint
    }
}

function Invoke-CodeSign([string]$TargetPath, $Signing) {
    if (-not $Signing) { return }
    & $Signing.Path sign /fd SHA256 /sha1 $Signing.Thumbprint /tr $TimestampUrl /td SHA256 /v $TargetPath
    if ($LASTEXITCODE -ne 0) { throw "Authenticode signing failed for $TargetPath" }
}

$signing = Get-CodeSignTool
if ($signing) {
    Invoke-CodeSign (Join-Path $output "PingYi.App.exe") $signing
    $engineExe = Join-Path $output "engine-host\pingyi-engine.exe"
    if (Test-Path -LiteralPath $engineExe -PathType Leaf) {
        Invoke-CodeSign $engineExe $signing
    }
}

$archivePrefix = if ($Edition -eq "Complete") { "PingYi-Complete" } else { "PingYi" }
$archive = Join-Path $projectRoot "artifacts\$archivePrefix-$Version-$Runtime.zip"
Compress-Archive -Path (Join-Path $output "*") -DestinationPath $archive -Force
$publishBytes = (Get-ChildItem -LiteralPath $output -Recurse -File | Measure-Object Length -Sum).Sum
$archiveBytes = (Get-Item -LiteralPath $archive).Length
Write-Host ("Release archive: {0} ({1:N1} MB; unpacked {2:N1} MB)" -f $archive, ($archiveBytes / 1MB), ($publishBytes / 1MB))

if ($BuildInstaller -and $Runtime -eq "win-x64") {
    $isccCandidates = @(
        $InnoCompiler,
        $env:PINGYI_ISCC,
        (Get-Command ISCC.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -First 1),
        (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe")
    ) | Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Leaf) }
    $isccPath = $isccCandidates | Select-Object -First 1
    if (-not $isccPath) {
        throw "ISCC.exe was not found. Set PINGYI_ISCC, install Inno Setup, or omit -BuildInstaller."
    }
    Write-Host "Inno Setup compiler: $isccPath"
    $innoArguments = @(
        "/DMyAppVersion=$Version",
        "/DSourceDir=$output",
        "/DEdition=$Edition"
    )
    if ($signing) {
        $innoSignCommand = '$q' + $signing.Path + '$q sign /fd SHA256 /sha1 ' + `
            $signing.Thumbprint + ' /tr ' + $TimestampUrl + ' /td SHA256 /v $f'
        $innoArguments += "/SPingYiSign=$innoSignCommand"
        $innoArguments += "/DSignToolName=PingYiSign"
    }
    $innoArguments += (Join-Path $projectRoot "packaging\windows\PingYi.iss")
    & $isccPath @innoArguments
    if ($LASTEXITCODE -ne 0) { throw "Failed to build the Windows installer." }
}
