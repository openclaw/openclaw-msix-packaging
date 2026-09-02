# OpenClaw Windows MSIX

This repository builds a Windows MSIX package containing:

- one .NET 10 NativeAOT launcher exposed through the `openclaw` and `clawctl`
  app execution aliases;
- a pinned, verified build of
  [`openclaw/openclaw`](https://github.com/openclaw/openclaw).

Node.js is a device prerequisite and is never downloaded or included in the
MSIX.

The package is independent from the OpenClaw Companion application and uses a
separate `OpenClaw.Gateway` package identity. Both packages use the OpenClaw
Foundation publisher metadata established for OpenClaw's Windows packages.

## Command model

Both aliases activate the same packaged `openclaw.exe`. The launcher recovers
the alias used to start it from the native process command line and selects one
of two deliberately separate surfaces.

### `openclaw`

`openclaw` is a transparent launcher for the bundled OpenClaw CLI. It does not
own package-management commands. Every argument, including an empty argument
list, is forwarded unchanged to `node openclaw.mjs`, and the launcher returns
the exact child exit code.

Before launching, the host discovers `node.exe` on `PATH` and verifies its
version and executable architecture. It never downloads, installs, or services
Node.js.

The prepared payload must already be current. If it is missing or stale,
`openclaw` exits with an instruction to run `clawctl prepare`; it does not
extract, verify, repair, or otherwise change package state.

This manual preparation contract is intentional and maintainer-approved. On a
fresh installation, the first `openclaw` invocation reports that preparation is
required. After an MSIX update changes the packaged payload, the existing
verification marker no longer matches and `openclaw` reports that the prepared
payload is out of date. In both cases, the user runs:

```powershell
clawctl prepare
```

These readiness checks and actionable guidance are the only package-specific
behavior in the `openclaw` entrypoint. Once the prepared payload and external
Node.js prerequisite are valid, all arguments are passed through unchanged and
the child process exit code is returned unchanged.

Every OpenClaw child process runs with
`OPENCLAW_SUPERVISOR_MODE=external` and `OPENCLAW_NO_AUTO_UPDATE=1`. This makes
the MSIX package the authoritative owner of Gateway code updates without
shadowing OpenClaw commands. OpenClaw itself is responsible for enforcing
those environment flags.

### `clawctl`

`clawctl` owns only the package preparation work that the bundled OpenClaw CLI
cannot perform itself:

| Command | Behavior |
|---|---|
| `clawctl prepare` | Verify the packaged archive and prepare it when missing or outdated. |
| `clawctl verify` | Deeply verify the prepared payload without changing it. |
| `clawctl repair` | Deeply verify the prepared payload and recreate it from packaged content when invalid. |

Bare `clawctl` and `clawctl --help` print help without changing state.
Commands such as `setup`, `doctor`, `gateway`, and `uninstall` belong to the
OpenClaw CLI and must be invoked through `openclaw`.

`prepare`, `verify`, and `repair` require a compatible device-installed
Node.js runtime. Missing, outdated, malformed, or architecture-incompatible
runtimes produce an actionable error rather than a later process-launch
failure.

Preparation extracts into a temporary directory, moves any existing prepared
payload aside, and promotes the new payload only after extraction and
verification succeed. The previous payload is restored if promotion fails.
Preparation and repair refuse to replace files while a packaged OpenClaw
process is using the prepared payload.

Install the current Node.js LTS release, open a new terminal, then prepare the
payload once before using `openclaw`:

```powershell
winget install --id OpenJS.NodeJS.LTS --exact --source winget
clawctl prepare
openclaw
```

The packaged OpenClaw revision accepts Node.js
`>=22.22.3 <23 || >=24.15.0 <25 || >=25.9.0`. The launcher keeps this
requirement in one shared validator used by `clawctl` and `openclaw`.

## Selecting the OpenClaw revision

`.github\workflows\gateway-msix.yml` resolves an explicit OpenClaw ref before
building. Pull-request and `main` push runs use the pinned commit configured in
both:

- `workflow_dispatch.inputs.openclaw_ref.default`;
- the non-manual fallback in `env.OPENCLAW_REF`.

Changing only the workflow-dispatch default does not change automatic builds.
For a one-time override, run **Build OpenClaw Gateway MSIX** manually and
provide a tag, branch, or preferably a full 40-character commit SHA in
`openclaw_ref`.

The workflow records the requested ref and resolved upstream commit in
`payload-metadata.json`. `msix-metadata.json` separately records both the
packaging repository commit and bundled OpenClaw commit.

`release-policy.json` records the immutable OpenClaw commit approved for
official signing. Updating that policy requires a reviewed repository change.
Official signing runs only from `main` and verifies the workflow input, both
architecture metadata files, both MSIX hashes, the embedded manifests, and
the embedded payload metadata and hashes before requesting Azure credentials.

## Build and test

```powershell
dotnet restore .\OpenClaw.Gateway.MSIX.slnx
dotnet test .\OpenClaw.Gateway.MSIX.slnx `
  --configuration Release `
  --no-restore
```

`scripts\Build-Payload.ps1` turns an OpenClaw npm package into an
architecture-specific payload. `scripts\Build-MSIX.ps1` verifies that payload,
rejects any packaged Node.js executable or runtime archive, then creates an
unsigned NativeAOT MSIX. `scripts\Build-LocalMSIX.ps1` can reuse a successful
workflow payload or a local payload directory. The Node.js used by the payload
build jobs is build infrastructure only and is not copied into the MSIX.

Normal pull-request and push workflows publish unsigned packages for
validation. Manual runs support three signing modes:

- `unsigned` accepts any OpenClaw branch, tag, or commit and publishes unsigned
  MSIX packages;
- `test` accepts any OpenClaw ref and publishes MSIX packages signed with a
  temporary self-signed certificate plus the public `.cer` needed for local
  installation;
- `official` requires the approved immutable commit from
  `release-policy.json` and may run only from `main`.

Official signing uses the protected `release-signing` environment, Azure OIDC,
and the existing OpenClaw Artifact Signing account and certificate profile.
Test-signing private keys are generated only on the temporary GitHub runner
and are deleted before artifacts are uploaded. No signing secret or private
key is stored in the repository.

## Installed data

| Data | Default path |
|---|---|
| Prepared OpenClaw application files | `%USERPROFILE%\.openclaw-msix\app` |
| OpenClaw configuration and user state | `%USERPROFILE%\.openclaw` |
| Launcher and package-management diagnostics | `%LOCALAPPDATA%\Packages\<package-family>\LocalState\OpenClawGatewayMSIX\Logs\openclaw.log` |

The prepared gateway and OpenClaw user state are outside the immutable MSIX
installation directory. Updating or removing the MSIX does not automatically
delete those directories or stop a running Gateway. Use OpenClaw's documented
[`openclaw uninstall`](https://docs.openclaw.ai/install/uninstall) flow before
removing the MSIX. The prepared `%USERPROFILE%\.openclaw-msix` directory may
be removed manually after OpenClaw is stopped.

## Integrity and isolation boundary

`clawctl prepare`, `verify`, and `repair` verify the immutable payload archive
shipped in the MSIX. Normal `openclaw` launches load the packaged metadata and
compare its payload hash with the prepared payload marker; they do not hash the
archive or every extracted file. Re-hashing the complete prepared payload on
every launch was intentionally rejected because it substantially delayed
OpenClaw startup.

The prepared gateway directory is writable by the current user and is treated
as user-owned application state, not as a tamper-resistant trust boundary.
OpenClaw runs without elevation, so a user or another process already running
in that user's security context can modify those files. The external
supervisor and no-auto-update environment settings reduce unintended
self-updates, but do not protect the prepared payload from same-user
modification.

The longer-term design is to run the Gateway payload in a dedicated isolated
agent session rather than the interactive session where the human user is
logged in. This will provide a boundary similar in purpose to running the
Gateway in WSL, using the forthcoming isolated-session capabilities. That
isolation is not provided by the current MSIX implementation.
