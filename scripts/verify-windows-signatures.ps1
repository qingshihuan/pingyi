param(
    [Parameter(Mandatory)][string[]]$LiteralPath,
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$ExpectedPublisher,
    [string]$SignTool
)

$ErrorActionPreference = 'Stop'
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw 'Authenticode verification requires Windows.'
}
if ([string]::IsNullOrWhiteSpace($ExpectedPublisher)) { throw 'An expected certificate publisher is required.' }

$signToolCandidates = @(
    $SignTool,
    $env:PINGYI_SIGNTOOL,
    (Get-Command signtool.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -First 1)
) | Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Leaf) }
if (-not $signToolCandidates) {
    $windowsKitsPath = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10/bin'
    if (Test-Path -LiteralPath $windowsKitsPath -PathType Container) {
        $signToolCandidates = Get-ChildItem -LiteralPath $windowsKitsPath -Filter signtool.exe -Recurse -File |
            Where-Object { $_.DirectoryName -match '\\x64$' } |
            Sort-Object FullName -Descending | Select-Object -ExpandProperty FullName -First 1
    }
}
$verifier = $signToolCandidates | Select-Object -First 1
if (-not $verifier) { throw 'Windows SDK SignTool is required to verify release signatures.' }

foreach ($target in $LiteralPath) {
    if (-not (Test-Path -LiteralPath $target -PathType Leaf)) { throw "Missing signed file: $target" }
    $signature = Get-AuthenticodeSignature -LiteralPath $target
    if ($signature.Status -ne 'Valid' -or $signature.SignatureType -ne 'Authenticode' -or -not $signature.SignerCertificate) {
        throw "A valid embedded Authenticode signature is required: $target ($($signature.Status))."
    }
    $publisher = $signature.SignerCertificate.GetNameInfo(
        [System.Security.Cryptography.X509Certificates.X509NameType]::SimpleName, $false)
    if (-not [string]::Equals($publisher, $ExpectedPublisher, [StringComparison]::Ordinal)) {
        throw "Certificate publisher does not match the configured release identity: $target."
    }
    if (-not $signature.TimeStamperCertificate) { throw "A trusted timestamp is required: $target." }
    & $verifier verify /pa /all /tw /q $target
    if ($LASTEXITCODE -ne 0) { throw "SignTool rejected a signature or timestamp: $target." }
    Write-Host "Verified signature and timestamp: $target (publisher: $publisher)"
}
