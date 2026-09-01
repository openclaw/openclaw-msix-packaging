[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$PayloadDirectory,

    [Parameter(Mandatory)]
    [ValidateSet('x64', 'arm64')]
    [string]$Architecture,

    [Parameter(Mandatory)]
    [string]$PackageVersion,

    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9a-fA-F]{40}$')]
    [string]$SourceCommit,

    [switch]$SourceTreeDirty,

    [Parameter(Mandatory)]
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path $PSScriptRoot -Parent
$projectPath = Join-Path `
    $repositoryRoot `
    'src\OpenClaw.Launcher\OpenClaw.Launcher.csproj'
$publisher = (
    'CN=OpenClaw Foundation, O=OpenClaw Foundation, L=Mill Valley, ' +
    'S=California, C=US'
)

function Invoke-CheckedCommand {
    param(
        [Parameter(Mandatory)]
        [scriptblock]$Command,

        [Parameter(Mandatory)]
        [string]$FailureMessage
    )

    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "$FailureMessage Exit code: $LASTEXITCODE."
    }
}

function Remove-DirectoryIfPresent {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    if ([IO.Directory]::Exists($Path)) {
        [IO.Directory]::Delete($Path, $true)
    }
}

function Test-PackageVersion {
    $segments = @($PackageVersion.Split('.'))
    if ($segments.Count -ne 4) {
        throw 'PackageVersion must contain four numeric components.'
    }

    foreach ($segment in $segments) {
        [uint16]$value = 0
        if (-not [uint16]::TryParse($segment, [ref]$value)) {
            throw "Invalid MSIX package version component: $segment"
        }
        if ($value -gt 65534) {
            throw (
                "PackageVersion component $segment exceeds the .NET " +
                'assembly version maximum of 65534.'
            )
        }
    }
}

function Assert-TarDoesNotBundleNode {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    $entries = @(& tar -tzf $Path)
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to inspect payload archive: $Path"
    }

    $bundledNodeEntries = @(
        $entries |
            Where-Object {
                $_ -match '(^|[\\/])node[.]exe$' -or
                [IO.Path]::GetFileName($_) -match '^node-v\d'
            }
    )
    if ($bundledNodeEntries.Count -ne 0) {
        throw (
            'The OpenClaw payload must not bundle Node.js: ' +
            (($bundledNodeEntries | Sort-Object) -join ', ')
        )
    }
}

function Add-VswhereToPath {
    if (Get-Command vswhere.exe -CommandType Application -ErrorAction SilentlyContinue) {
        return
    }

    $vswhereDirectory = Join-Path `
        ${env:ProgramFiles(x86)} `
        'Microsoft Visual Studio\Installer'
    $vswherePath = Join-Path $vswhereDirectory 'vswhere.exe'
    if (-not (Test-Path -LiteralPath $vswherePath -PathType Leaf)) {
        throw (
            'vswhere.exe was not found. Install Visual Studio Build Tools with ' +
            'the Desktop development with C++ workload.'
        )
    }

    $env:Path = "$vswhereDirectory;$env:Path"
}

Test-PackageVersion
Add-VswhereToPath

$PayloadDirectory = (Resolve-Path -LiteralPath $PayloadDirectory).Path
$payloadArchive = Join-Path $PayloadDirectory "app-$Architecture.tar.gz"
$payloadMetadata = Join-Path $PayloadDirectory 'payload-metadata.json'
foreach ($requiredPath in @($payloadArchive, $payloadMetadata)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Required MSIX input was not found: $requiredPath"
    }
}

$payloadInfo = Get-Content -LiteralPath $payloadMetadata -Raw | ConvertFrom-Json
if (
    $payloadInfo.repository -ne 'https://github.com/openclaw/openclaw' -or
    $payloadInfo.architecture -ne $Architecture -or
    $payloadInfo.archive -ne (Split-Path $payloadArchive -Leaf) -or
    $payloadInfo.resolvedCommit -notmatch '^[0-9a-fA-F]{40}$' -or
    $payloadInfo.sha256 -notmatch '^[0-9a-fA-F]{64}$'
) {
    throw 'Payload metadata is not valid for this MSIX package.'
}

$payloadHash = (
    Get-FileHash -LiteralPath $payloadArchive -Algorithm SHA256
).Hash.ToLowerInvariant()
if ($payloadInfo.sha256 -ine $payloadHash) {
    throw 'Payload hash does not match payload metadata.'
}
Assert-TarDoesNotBundleNode -Path $payloadArchive

$contentRoot = Join-Path $repositoryRoot 'content'
$openClawContent = Join-Path $contentRoot 'openclaw'
New-Item -Path $openClawContent -ItemType Directory -Force | Out-Null

$stagedPayloadArchive = Join-Path `
    $openClawContent `
    (Split-Path $payloadArchive -Leaf)
$stagedPayloadMetadata = Join-Path $openClawContent 'payload-metadata.json'
if (
    [IO.Path]::GetFullPath($payloadArchive) -ne
    [IO.Path]::GetFullPath($stagedPayloadArchive)
) {
    Copy-Item -LiteralPath $payloadArchive -Destination $stagedPayloadArchive -Force
}
if (
    [IO.Path]::GetFullPath($payloadMetadata) -ne
    [IO.Path]::GetFullPath($stagedPayloadMetadata)
) {
    Copy-Item -LiteralPath $payloadMetadata -Destination $stagedPayloadMetadata -Force
}

$temporaryRoot = if ($env:RUNNER_TEMP) {
    $env:RUNNER_TEMP
}
else {
    [IO.Path]::GetTempPath()
}
$workRoot = Join-Path `
    $temporaryRoot `
    "openclaw-msix-$Architecture-$([guid]::NewGuid().ToString('N'))"
$msixBuildDirectory = Join-Path $workRoot 'appx'
New-Item `
    -Path $msixBuildDirectory, $OutputDirectory `
    -ItemType Directory `
    -Force |
    Out-Null

try {
    $appxOutput = $msixBuildDirectory.TrimEnd('\') + '\'
    Write-Host "Building unsigned NativeAOT win-$Architecture MSIX with MSBuild."
    Invoke-CheckedCommand `
        -FailureMessage 'NativeAOT MSIX build failed.' `
        -Command {
            & dotnet build $projectPath `
                --configuration Release `
                --runtime "win-$Architecture" `
                --no-restore `
                "-p:Platform=$Architecture" `
                "-p:RuntimeIdentifiers=win-$Architecture" `
                -p:PublishAot=true `
                -p:SelfContained=true `
                -p:IncludePackagingContent=true `
                -p:GenerateAppxPackageOnBuild=true `
                "-p:AssemblyVersion=$PackageVersion" `
                "-p:FileVersion=$PackageVersion" `
                "-p:PackageIdentityVersion=$PackageVersion" `
                "-p:AppxPackageDir=$appxOutput" `
                -p:AppxBundle=Never `
                -p:AppxPackageSigningEnabled=false `
                -p:DebugType=None `
                --nologo
        }

    $builtPackages = @(
        Get-ChildItem `
            -LiteralPath $msixBuildDirectory `
            -Filter '*.msix' `
            -File `
            -Recurse
    )
    if ($builtPackages.Count -ne 1) {
        throw (
            "Expected one MSIX under '$msixBuildDirectory'; " +
            "found $($builtPackages.Count)."
        )
    }

    $msixName = "OpenClawGateway-$Architecture.msix"
    $msixPath = Join-Path $OutputDirectory $msixName
    Copy-Item -LiteralPath $builtPackages[0].FullName -Destination $msixPath -Force

    $expectedPackageFiles =
        [System.Collections.Generic.Dictionary[string, object]]::new(
            [System.StringComparer]::OrdinalIgnoreCase
        )
    $expectedPackageFiles.Add(
        "payload/$(Split-Path $payloadArchive -Leaf)",
        [pscustomobject]@{
            Hash = $payloadHash
        }
    )
    $packageEntries = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::OrdinalIgnoreCase
    )
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $packageArchive = [System.IO.Compression.ZipFile]::OpenRead($msixPath)
    try {
        foreach ($entry in $packageArchive.Entries) {
            if ([string]::IsNullOrEmpty($entry.Name)) {
                continue
            }

            $decodedPath = [Uri]::UnescapeDataString($entry.FullName)
            $null = $packageEntries.Add($decodedPath)
            $expectedEntry = $null
            if (
                -not $expectedPackageFiles.Remove(
                    $decodedPath,
                    [ref]$expectedEntry
                )
            ) {
                continue
            }

            $stream = $entry.Open()
            $sha256 = [Security.Cryptography.SHA256]::Create()
            try {
                $packagedHash = [Convert]::ToHexString(
                    $sha256.ComputeHash($stream)
                ).ToLowerInvariant()
            }
            finally {
                $sha256.Dispose()
                $stream.Dispose()
            }

            if ($packagedHash -ne $expectedEntry.Hash) {
                throw "MSBuild changed package content: $decodedPath"
            }

        }

        $manifestEntry = $packageArchive.GetEntry('AppxManifest.xml')
        if (-not $manifestEntry) {
            throw 'The MSIX does not contain AppxManifest.xml.'
        }

        $manifestStream = $manifestEntry.Open()
        $manifestReader = [IO.StreamReader]::new($manifestStream)
        try {
            [xml]$manifest = $manifestReader.ReadToEnd()
        }
        finally {
            $manifestReader.Dispose()
            $manifestStream.Dispose()
        }

        $aliasExtension = @(
            $manifest.SelectNodes(
                "//*[local-name()='Extension' and @Category='windows.appExecutionAlias']"
            )
        )
        if ($aliasExtension.Count -ne 1) {
            throw 'The MSIX must contain one app execution alias extension.'
        }
        if ($aliasExtension[0].Executable -ne 'openclaw.exe') {
            throw 'Both command aliases must target openclaw.exe.'
        }

        $registeredAliases = @(
            $aliasExtension[0].SelectNodes(
                ".//*[local-name()='ExecutionAlias']"
            ) |
                ForEach-Object { $_.Alias }
        )
        foreach ($requiredAlias in @('openclaw.exe', 'clawctl.exe')) {
            if ($requiredAlias -notin $registeredAliases) {
                throw "The MSIX does not register $requiredAlias."
            }
        }
    }
    finally {
        $packageArchive.Dispose()
    }

    if (-not $packageEntries.Contains('openclaw.exe')) {
        throw 'The MSIX does not contain the NativeAOT host executable.'
    }
    $bundledNodeEntries = @(
        $packageEntries |
            Where-Object {
                [IO.Path]::GetFileName($_) -ieq 'node.exe' -or
                [IO.Path]::GetFileName($_) -match '^node-v\d'
            }
    )
    if ($bundledNodeEntries.Count -ne 0) {
        throw (
            'The MSIX must not bundle Node.js: ' +
            (($bundledNodeEntries | Sort-Object) -join ', ')
        )
    }
    foreach ($managedHostArtifact in @(
        'openclaw.dll',
        'openclaw.deps.json',
        'openclaw.runtimeconfig.json'
    )) {
        if ($packageEntries.Contains($managedHostArtifact)) {
            throw "The MSIX contains managed host artifact: $managedHostArtifact"
        }
    }

    if ($expectedPackageFiles.Count -ne 0) {
        throw (
            'MSBuild omitted package content: ' +
            (
                $expectedPackageFiles.Keys |
                    Sort-Object |
                    Select-Object -First 5
            ) -join ', '
        )
    }

    $msixHash = (
        Get-FileHash -LiteralPath $msixPath -Algorithm SHA256
    ).Hash.ToLowerInvariant()
    [ordered]@{
        packagingRepository = 'https://github.com/openclaw/openclaw-windows-packaging'
        packagingCommit = $SourceCommit.ToLowerInvariant()
        sourceTreeDirty = $SourceTreeDirty.IsPresent
        payloadRepository = $payloadInfo.repository
        payloadRequestedRef = $payloadInfo.requestedRef
        payloadResolvedCommit = $payloadInfo.resolvedCommit.ToLowerInvariant()
        architecture = $Architecture
        archive = $msixName
        sha256 = $msixHash
        signed = $false
        packageVersion = $PackageVersion
        publisher = $publisher
    } | ConvertTo-Json |
        Set-Content `
            -LiteralPath (Join-Path $OutputDirectory 'msix-metadata.json') `
            -Encoding utf8

    Write-Host "Created unsigned MSIX: $msixPath"
}
finally {
    Remove-DirectoryIfPresent -Path $workRoot
}
