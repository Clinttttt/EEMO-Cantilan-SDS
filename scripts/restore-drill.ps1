<#
.SYNOPSIS
    Rehearses a database restore from a real backup, in a throwaway container.

.DESCRIPTION
    Answers the only question a backup exists to answer: if the office lost the database this morning,
    would this file bring it back?

    Nothing here touches Azure or production. It downloads a backup artifact produced by the "Database
    backup" workflow, restores it into a disposable PostgreSQL container on this machine, verifies what
    came back, and removes the container. The live database is never connected to.

    WHY A SCRIPT AND NOT A LIST OF STEPS
        The restore workflow had never once been executed. A procedure nobody practises is not a
        procedure, and the reason it was never practised is that it restores straight into production
        with --clean, so there was no safe way to try it. This is the safe way, and it is short enough
        to run every month.

.PARAMETER RunId
    The GitHub Actions run whose backup artifact to restore. Omit to use the newest successful backup.

.PARAMETER KeepContainer
    Leave the container running afterwards so you can look through the restored data yourself.

.EXAMPLE
    From an ordinary Command Prompt (cmd.exe), use the .cmd wrapper - cmd cannot run a .ps1 directly and
    will silently do nothing if you try:
        scripts\restore-drill.cmd
        scripts\restore-drill.cmd -KeepContainer

.EXAMPLE
    From a PowerShell prompt, call this file directly:
        .\restore-drill.ps1
        .\restore-drill.ps1 -RunId 31295284398 -KeepContainer

    With -KeepContainer, open a session in the restored copy with (no local PostgreSQL needed):
        docker exec -it stalltrack-restore-drill psql -U postgres -d drill

.NOTES
    Needs: gh (logged in) and docker. pg_restore runs INSIDE the container, so no local PostgreSQL
    install is required and the client version always matches the server.
#>

[CmdletBinding()]
param(
    [string]$RunId,
    # A dump already on disk, instead of downloading one. Useful for a file taken by hand, a copy someone
    # sent you, or for proving this drill actually FAILS on a damaged backup.
    [string]$DumpPath,
    [switch]$KeepContainer
)

$ErrorActionPreference = 'Stop'

$Container = 'stalltrack-restore-drill'
$HostPort  = 55432
# Must match the production server's major version: pg_restore will not read a dump from a newer server.
$Image     = 'postgres:18'
$WorkDir   = Join-Path ([System.IO.Path]::GetTempPath()) 'stalltrack-restore-drill'

function Step($n, $text) { Write-Host "`n[$n] $text" -ForegroundColor Cyan }
function Ok($text)       { Write-Host "    $text" -ForegroundColor Green }
function Fail($text)     { Write-Host "    $text" -ForegroundColor Red }

# Runs a native command whose failure is EXPECTED and harmless - removing a container that is not there,
# probing a database that is not up yet. PowerShell turns anything a native command writes to stderr into
# a terminating error under ErrorActionPreference='Stop', so without this the script dies on a tidy-up
# step that had nothing to tidy. Returns the exit code instead of throwing.
function Try-Run {
    param([Parameter(Mandatory)][string]$Exe, [Parameter(ValueFromRemainingArguments)][string[]]$Arguments)
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & $Exe @Arguments 2>&1 | Out-Null
        return $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $previous }
}

# --- Preflight ----------------------------------------------------------------------------------
Step 0 'Checking tools'
foreach ($tool in @('gh', 'docker')) {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) { throw "$tool is not on PATH." }
}
if ((Try-Run docker version --format '{{.Server.Version}}') -ne 0) {
    throw 'Docker is not running. Start Docker Desktop and try again.'
}
Ok 'gh and docker are available'

# --- 1. Find the backup -------------------------------------------------------------------------
Step 1 'Finding the backup to restore'
if ($DumpPath) {
    if (-not (Test-Path $DumpPath)) { throw "No file at $DumpPath" }
    $dump = Get-Item $DumpPath
    Ok "Using the file given: $($dump.Name) - $([math]::Round($dump.Length / 1KB, 1)) KB"
}
elseif (-not $RunId) {
    $runs = gh run list --workflow 'Database backup' --limit 20 --json databaseId,conclusion,updatedAt | ConvertFrom-Json
    $newest = $runs | Where-Object { $_.conclusion -eq 'success' } | Sort-Object updatedAt -Descending | Select-Object -First 1
    if (-not $newest) { throw 'No successful "Database backup" run found. Run one first.' }
    $RunId = $newest.databaseId
    Ok "Newest successful backup: run $RunId ($($newest.updatedAt))"
}
else {
    Ok "Using run $RunId as asked"
}

# --- 2. Download it -----------------------------------------------------------------------------
if (-not $DumpPath) {
    Step 2 'Downloading the dump'
    if (Test-Path $WorkDir) { Remove-Item $WorkDir -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $WorkDir | Out-Null
    gh run download $RunId --dir $WorkDir
    # The artifact unzips into a FOLDER named after itself, so the dump sits one level down. Searching
    # recursively avoids handing pg_restore a directory and being told the file is corrupt.
    $dump = Get-ChildItem -Recurse -File $WorkDir -Filter '*.dump' | Sort-Object Length -Descending | Select-Object -First 1
    if (-not $dump) { throw "No .dump file found in the artifact from run $RunId." }
    Ok "$($dump.Name) - $([math]::Round($dump.Length / 1KB, 1)) KB"
}

# --- 3. Throwaway database ----------------------------------------------------------------------
Step 3 'Starting a disposable PostgreSQL'
Try-Run docker rm -f $Container | Out-Null
docker run -d --name $Container -e POSTGRES_PASSWORD=drill -p "${HostPort}:5432" $Image | Out-Null
$ready = $false
foreach ($i in 1..40) {
    Start-Sleep -Seconds 3
    if ((Try-Run docker exec $Container pg_isready -U postgres) -eq 0) { $ready = $true; break }
}
if (-not $ready) { throw 'PostgreSQL container did not become ready.' }
docker exec $Container psql -U postgres -c 'CREATE DATABASE drill;' | Out-Null
Ok "$Image ready on localhost:$HostPort"

# --- 4. The restore itself ----------------------------------------------------------------------
Step 4 'Restoring'
docker cp $dump.FullName "${Container}:/tmp/backup.dump" | Out-Null
# --exit-on-error matters: without it pg_restore reports success having skipped everything it could
# not apply, which is exactly how an unusable backup passes for a good one.
# Output is left visible - a failed drill must show WHY - so stderr is tolerated rather than discarded.
$ErrorActionPreference = 'Continue'
docker exec $Container pg_restore --no-owner --no-privileges --exit-on-error -U postgres -d drill /tmp/backup.dump 2>&1
$restoreOk = ($LASTEXITCODE -eq 0)
$ErrorActionPreference = 'Stop'
if ($restoreOk) { Ok 'pg_restore completed with no errors' } else { Fail 'pg_restore reported errors' }

# --- 5. Verify what came back -------------------------------------------------------------------
Step 5 'Verifying the restored database'
$verify = @'
SELECT 'tables' AS metric, count(*)::text AS value FROM information_schema.tables WHERE table_schema='public'
UNION ALL SELECT 'indexes', count(*)::text FROM pg_indexes WHERE schemaname='public'
UNION ALL SELECT 'foreign keys', count(*)::text FROM pg_constraint c
    JOIN pg_class t ON t.oid=c.conrelid JOIN pg_namespace n ON n.oid=t.relnamespace
    WHERE n.nspname='public' AND c.contype='f'
UNION ALL SELECT 'migrations applied', count(*)::text FROM "__EFMigrationsHistory"
UNION ALL SELECT 'newest migration', COALESCE(max("MigrationId"), '(none)') FROM "__EFMigrationsHistory"
UNION ALL SELECT 'Users', count(*)::text FROM "Users"
UNION ALL SELECT 'Municipalities', count(*)::text FROM "Municipalities"
UNION ALL SELECT 'Facilities', count(*)::text FROM "Facilities"
UNION ALL SELECT 'FacilityRates', count(*)::text FROM "FacilityRates"
UNION ALL SELECT 'Stalls', count(*)::text FROM "Stalls"
UNION ALL SELECT 'Contracts', count(*)::text FROM "Contracts"
UNION ALL SELECT 'PaymentRecords', count(*)::text FROM "PaymentRecords"
UNION ALL SELECT 'AuditLogs', count(*)::text FROM "AuditLogs";
'@
$ErrorActionPreference = 'Continue'
$verify | docker exec -i $Container psql -U postgres -d drill -v ON_ERROR_STOP=1 -f - 2>&1
$verifyOk = ($LASTEXITCODE -eq 0)
$tableCount = (docker exec $Container psql -U postgres -d drill -tAc "SELECT count(*) FROM information_schema.tables WHERE table_schema='public'" 2>&1).Trim()
$ErrorActionPreference = 'Stop'

# A schema with no tables restores "successfully" and is worthless, so the count is asserted here
# rather than only printed above.
if ([int]$tableCount -lt 20) {
    Fail "Only $tableCount tables restored - expected the full schema."
    $verifyOk = $false
}

# --- 6. Clean up --------------------------------------------------------------------------------
if ($KeepContainer) {
    Step 6 'Leaving the container up as asked'
    # Through docker rather than a local psql: the machine that runs this drill needs Docker but does not
    # need PostgreSQL installed, and printing a psql command that is not on PATH sends the reader chasing
    # an install they do not need.
    Ok 'Open a session in the restored copy with:'
    Ok "    docker exec -it $Container psql -U postgres -d drill"
    Ok 'Or from a tool on this machine, if you have one:'
    Ok "    host localhost   port $HostPort   database drill   user postgres   password drill"
    Ok "Remove it when you are done with:  docker rm -f $Container"
}
else {
    Step 6 'Removing the container'
    Try-Run docker rm -f $Container | Out-Null
    Remove-Item $WorkDir -Recurse -Force -ErrorAction SilentlyContinue
    Ok 'Container and downloaded dump removed'
}

# --- Verdict ------------------------------------------------------------------------------------
Write-Host ''
if ($restoreOk -and $verifyOk) {
    Write-Host 'DRILL PASSED - this backup restores, and the schema and seed data come back.' -ForegroundColor Green
    Write-Host 'This proves the mechanism. Re-run it once the office has real volume in, so the restore' -ForegroundColor DarkGray
    Write-Host 'is also timed against a populated database.' -ForegroundColor DarkGray
    exit 0
}

Write-Host 'DRILL FAILED - this backup did not restore cleanly. Treat it as untrusted and find out why' -ForegroundColor Red
Write-Host 'now, not on the morning you need it.' -ForegroundColor Red
exit 1
