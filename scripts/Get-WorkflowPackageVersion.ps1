[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [long]$RunNumber,

    [Parameter(Mandatory)]
    [long]$RunAttempt
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$maximumComponent = 65534L
$componentBase = $maximumComponent + 1L

if ($RunNumber -lt 1) {
    throw 'RunNumber must be greater than zero.'
}
if ($RunAttempt -lt 1 -or $RunAttempt -gt $maximumComponent) {
    throw "RunAttempt must be between 1 and $maximumComponent."
}

[long]$buildComponent = 0
[long]$runNumberCarry = [Math]::DivRem(
    $RunNumber,
    $componentBase,
    [ref]$buildComponent)
[long]$minorComponent = 1L + $runNumberCarry

if ($minorComponent -gt $maximumComponent) {
    throw (
        "RunNumber $RunNumber exceeds the supported package-version range."
    )
}

"0.$minorComponent.$buildComponent.$RunAttempt"
