# OpenClaw Gateway MSIX repository guidance

## Build and test commands

The repository is Windows-focused and pins the .NET 10 SDK through `global.json`.
Run commands from the repository root in PowerShell 7 (`pwsh`).

```powershell
# Restore and build the launcher and tests without packaging content.
dotnet restore .\OpenClaw.Gateway.MSIX.slnx
dotnet build .\OpenClaw.Gateway.MSIX.slnx --configuration Release --no-restore

# Publish the launcher through the NativeAOT toolchain without MSIX content.
$vsInstaller = Join-Path `
  ([Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFilesX86)) `
  'Microsoft Visual Studio\Installer'
$env:Path = "$vsInstaller;$env:Path"
dotnet publish .\src\OpenClaw.Gateway.Launcher\OpenClaw.Gateway.Launcher.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained

# Run all .NET tests.
dotnet test .\OpenClaw.Gateway.MSIX.slnx `
  --configuration Release `
  --no-restore

# Run one xUnit test by fully qualified name.
dotnet test .\tests\OpenClaw.Gateway.Launcher.Tests\OpenClaw.Gateway.Launcher.Tests.csproj `
  --configuration Release `
  --filter "FullyQualifiedName=OpenClaw.Gateway.Launcher.Tests.PayloadStagerTests.StageAsyncExtractsAndReusesVerifiedPayload"

# Exercise the official-signing policy checks.
.\scripts\Test-SigningInputs.Tests.ps1
```

To compose an unsigned local MSIX from the latest successful `main` workflow
payload, use:

```powershell
.\scripts\Build-LocalMSIX.ps1 -Architecture x64
```

Pass `-PayloadDirectory <path>` to use an already-built payload instead of
downloading one with `gh`. MSIX composition requires Visual Studio Build Tools
with the Desktop development with C++ workload and the Windows SDK. Build x64
and ARM64 separately.

## Architecture

- `OpenClaw.Gateway.Launcher` is a .NET 10 NativeAOT executable packaged as
  `openclaw.exe`. `Package.appxmanifest` exposes it through the `openclaw.exe`
  app execution alias and declares the `OpenClaw.Gateway` MSIX identity.
- The package contains an architecture-specific OpenClaw tarball and official
  Node.js runtime. `HostOptions` resolves packaged inputs and the prepared
  per-user installation at `%USERPROFILE%\.openclaw-msix\app`.
- Every launch goes through `PayloadStager`. It validates payload metadata,
  architecture, and SHA-256; takes a non-inheritable cross-process install
  lock; recovers interrupted promotions; and atomically replaces the prepared
  directory through `.staging` and `.previous` siblings.
- A no-argument launch is the preparation/repair UI. Existing installations
  default to marker-based fast verification; the repair choice performs full
  inventory and per-file verification. Any invocation with arguments stages
  as needed, then forwards every argument unchanged to `openclaw.mjs`.
- `GatewayLauncher` starts Node without a shell, uses `ArgumentList`, inherits
  the console streams, and sets `OPENCLAW_SUPERVISOR_MODE=external` plus
  `OPENCLAW_NO_AUTO_UPDATE=1`. The child process exit code is the launcher exit
  code.
- Diagnostics are written to packaged LocalState (or
  `%LOCALAPPDATA%\OpenClawGatewayMSIX` outside an MSIX context) with a named
  mutex so concurrent processes append complete records.
- The GitHub workflow first builds and packs a pinned
  `openclaw/openclaw` revision on Linux. Windows matrix jobs use
  `Build-Payload.ps1` to produce x64/ARM64 tarballs and metadata, then
  `Build-MSIX.ps1` to verify the payload, download and verify Node.js, publish
  the NativeAOT host, validate package contents, and emit MSIX metadata.
- Unsigned artifacts are the normal PR/push output. Test signing uses a
  temporary runner-local certificate. Official signing is gated to `main` and
  the immutable upstream commit in `release-policy.json`; signing inputs are
  validated before Azure credentials are requested.

## Repository conventions

- Ordinary builds and tests must leave `IncludePackagingContent` unset.
  Packaging builds set it to `true`, supply a runtime identifier and platform,
  and use `obj\packaging` through `Directory.Build.props` to isolate MSIX
  intermediates.
- Treat launcher arguments as OpenClaw-owned. Do not add host-only switches,
  consume `--`, rewrite arguments, or block upstream commands; tests explicitly
  protect transparent forwarding.
- Preserve the staging transaction and fast-marker behavior when changing
  payload preparation. The immutable packaged archive is always hashed, while
  full extracted-file hashing is reserved for explicit repair or migration.
- Payload extraction is a security boundary: retain entry-count and extracted
  size limits, reject links and unsafe/duplicate Windows paths, build a trusted
  inventory from the archive, and promote only after verification succeeds.
- Keep x64 and ARM64 behavior synchronized across the workflow matrix, scripts,
  project runtime identifiers, manifest content, payload metadata, and signing
  validation.
- Metadata files are part of the release trust chain, not incidental build
  output. Changes to their fields must be coordinated across payload creation,
  MSIX creation, signing validation, workflow artifacts, and tests.
- Keep the workflow's manual `openclaw_ref` default and automatic
  `env.OPENCLAW_REF` fallback identical. Official-release changes also update
  the reviewed immutable commit in `release-policy.json`.
- Use source-generated `System.Text.Json` metadata through
  `OpenClawJsonContext`; the launcher is NativeAOT and should not introduce
  reflection-based serialization. `dotnet build` and the xUnit suite exercise
  a JIT build, so run the NativeAOT publish path when changing host JSON,
  reflection, interop, or trimming-sensitive code.
- Package versions have four numeric components that each fit in `UInt16`.
  Package dependency versions belong in `Directory.Packages.props`.
- PowerShell build scripts fail fast with `$ErrorActionPreference = 'Stop'`
  and must also check `$LASTEXITCODE` after native tools. Preserve metadata and
  hash validation rather than relying only on command success.
- Tests create isolated temporary directories through `TestDirectory`; extend
  those fixtures instead of reading or modifying real OpenClaw profile,
  packaged LocalState, or installed MSIX data.
