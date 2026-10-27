param([switch]$ConfigureFirebase)
$ErrorActionPreference = 'Stop'
$region = 'ap-southeast-1'
$instance = 'stalltrack-demo-vm-20261017'
$hostIp = '54.169.168.202'
$ip = (Invoke-RestMethod -Uri 'https://checkip.amazonaws.com' -TimeoutSec 15).Trim()
if ($ip -notmatch '^\d{1,3}(\.\d{1,3}){3}$') { throw 'Invalid workstation IPv4' }
$cidr = "$ip/32"
$temporary = Join-Path $env:TEMP ('stalltrack-fcm-preflight-' + [guid]::NewGuid().ToString('N'))
$opened = $false
$remoteDirectory = $null
$sshOptions = $null

function Invoke-AwsJson([string[]]$Arguments) {
    $result = & aws @Arguments --region $region --output json
    if ($LASTEXITCODE -ne 0) { throw 'AWS request failed' }
    return ($result | ConvertFrom-Json)
}
function Wait-Operation([string]$Id) {
    for ($attempt = 0; $attempt -lt 20; $attempt++) {
        $operation = (Invoke-AwsJson @('lightsail', 'get-operation', '--operation-id', $Id)).operation
        if ($operation.status -eq 'Succeeded') { return }
        if ($operation.status -eq 'Failed') { throw 'Firewall operation failed' }
        Start-Sleep -Seconds 2
    }
    throw 'Firewall operation timed out'
}

try {
    New-Item -ItemType Directory -Path $temporary | Out-Null
    & icacls.exe $temporary /inheritance:r /grant:r "${env:USERNAME}:(OI)(CI)F" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Temporary directory ACL failed' }
    $access = (Invoke-AwsJson @('lightsail', 'get-instance-access-details', '--instance-name', $instance, '--protocol', 'ssh')).accessDetails
    if ($access.username -ne 'ubuntu' -or -not $access.privateKey -or -not $access.certKey) {
        throw 'Unexpected temporary SSH credential'
    }
    $private = Join-Path $temporary 'key'
    $certificate = Join-Path $temporary 'key-cert.pub'
    [IO.File]::WriteAllText($private, $access.privateKey.Replace("`r", ''), [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText($certificate, $access.certKey.Trim() + "`n", [Text.UTF8Encoding]::new($false))
    $access = $null
    foreach ($path in @($private, $certificate)) {
        & icacls.exe $path /inheritance:r /grant:r "${env:USERNAME}:R" | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Temporary key ACL failed' }
    }
    $ports = (Invoke-AwsJson @('lightsail', 'get-instance-port-states', '--instance-name', $instance)).portStates
    $prior = @($ports | Where-Object { $_.protocol -eq 'tcp' -and $_.fromPort -le 22 -and $_.toPort -ge 22 } | ForEach-Object { $_.cidrs })
    if ($prior -contains '0.0.0.0/0') { throw 'Unrestricted SSH baseline requires review' }
    if ($prior -notcontains $cidr) {
        $opened = $true
        $operation = Invoke-AwsJson @('lightsail', 'open-instance-public-ports', '--instance-name', $instance, '--port-info', "fromPort=22,toPort=22,protocol=tcp,cidrs=$cidr")
        Wait-Operation $operation.operation.id
    }
    # Only selected metadata is emitted; never dump container environments or release-control JSON.
    $probe = @'
set -eu
cd /home/ubuntu/stalltrack-demo
for service in api portal db caddy; do
  docker inspect "stalltrack-presentation-demo-${service}-1" --format 'service={{index .Config.Labels "com.docker.compose.service"}} id={{.Id}} image={{.Config.Image}} user={{.Config.User}} status={{.State.Status}}'
done
docker inspect stalltrack-presentation-demo-api-1 --format '{{index .Config.Labels "com.docker.compose.project.config_files"}}'
docker exec stalltrack-presentation-demo-api-1 id
sudo -n stat -c 'API_BASE_METADATA uid=%u gid=%g mode=%a' compose.v10-clean-db.yaml
sudo -n sha256sum compose.v10-clean-db.yaml
sudo -n python3 - <<'PY'
import hashlib,json,pathlib
p=pathlib.Path('/etc/stalltrack-client-release.json')
j=json.loads(p.read_text())
print('RELEASE_CONTROL', {k:j.get(k) for k in ('enabled','approval_mode','rollback_proven')})
for name in ('/usr/local/sbin/stalltrack-stack-deploy','/usr/local/sbin/stalltrack-client-deploy'):
 p=pathlib.Path(name)
 if p.exists(): print('GATE',name,hashlib.sha256(p.read_bytes()).hexdigest())
p=pathlib.Path('/etc/stalltrack-firebase/collector-admin.json')
print('FIREBASE_SECRET_EXISTS',p.exists())
if p.exists():
 s=p.stat()
 print('FIREBASE_SECRET_METADATA', 'uid='+str(s.st_uid), 'gid='+str(s.st_gid), 'mode='+oct(s.st_mode & 0o777))
PY
docker inspect stalltrack-presentation-demo-api-1 --format '{{range .Mounts}}{{if eq .Destination "/run/secrets/stalltrack_admin.json"}}FIREBASE_MOUNT target={{.Destination}} writable={{.RW}}{{end}}{{end}}'
docker inspect stalltrack-presentation-demo-api-1 --format '{{range .Config.Env}}{{println .}}{{end}}' | awk -F= '/^Firebase__/ {print "FIREBASE_SETTING_PRESENT " $1} /^Database__ApplyMigrationsAtStartup=/ {print $0}'
curl -fsS --max-time 10 https://api.stalltrack.site/health/ready
docker exec -i stalltrack-presentation-demo-db-1 sh -c 'psql -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d stalltrack_v10_presentation_clean -At' <<'SQL'
BEGIN READ ONLY;
SELECT 'collector11_registration', u."MunicipalityId", u."IsActive", count(t."Id")
FROM "Users" u LEFT JOIN "CollectorDeviceTokens" t ON t."CollectorId"=u."Id" AND t."MunicipalityId"=u."MunicipalityId"
WHERE u."Username"='collector11' AND u."UserType"='Collector' AND NOT u."IsDeleted"
GROUP BY u."MunicipalityId", u."IsActive";
ROLLBACK;
SQL
docker logs --since 15m stalltrack-presentation-demo-api-1 2>&1 | python3 -c 'import sys; s=sys.stdin.read(); markers=["FCM push disabled", "FCM push initialization failed", "FCM send failed", "Failed to push online-payment collector notification", "SenderIdMismatch", "Unregistered", "invalid_grant", "PERMISSION_DENIED"]; print("SANITIZED_FCM_LOG_COUNTS", {m:s.count(m) for m in markers})'
'@
    $probe | & ssh.exe -T -o BatchMode=yes -o IdentitiesOnly=yes -o StrictHostKeyChecking=yes -o "CertificateFile=$certificate" -o ConnectTimeout=12 -i $private "ubuntu@$hostIp" "tr -d '\r' | bash"
    if ($LASTEXITCODE -ne 0) { throw 'Read-only SSH preflight failed' }
    if ($ConfigureFirebase) {
        $sshOptions = @('-o', 'BatchMode=yes', '-o', 'IdentitiesOnly=yes', '-o', 'StrictHostKeyChecking=yes', '-o', "CertificateFile=$certificate", '-o', 'ConnectTimeout=12', '-i', $private)
        $remoteDirectory = (& ssh.exe @sshOptions "ubuntu@$hostIp" 'umask 077; mktemp -d /home/ubuntu/.fcm-activation-XXXXXXXX').Trim()
        if ($LASTEXITCODE -ne 0 -or $remoteDirectory -notmatch '^/home/ubuntu/\.fcm-activation-[A-Za-z0-9]{8}$') { throw 'Private remote directory creation failed' }
        $installer = Join-Path $PSScriptRoot 'configure-fcm-production.py'
        $expectedHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $installer).Hash.ToLowerInvariant()
        & scp.exe @sshOptions $installer "ubuntu@${hostIp}:$remoteDirectory/install.py"
        if ($LASTEXITCODE -ne 0) { throw 'Installer transfer failed' }
        $remoteHash = & ssh.exe @sshOptions "ubuntu@$hostIp" "sha256sum $remoteDirectory/install.py"
        if ($LASTEXITCODE -ne 0 -or ($remoteHash -split ' ')[0] -ne $expectedHash) { throw 'Installer checksum mismatch' }
        $credential = Join-Path $env:USERPROFILE 'Downloads/stalltrack-56b8a-firebase-adminsdk-fbsvc-19e44094fd.json'
        # SSH stdin carries the secret directly; neither arguments nor remote staging contain the key.
        Get-Content -LiteralPath $credential -Raw -Encoding UTF8 | & ssh.exe -T @sshOptions "ubuntu@$hostIp" "sudo -n python3 $remoteDirectory/install.py"
        if ($LASTEXITCODE -ne 0) { throw 'Protected Firebase configuration refused or failed' }
    }
}
finally {
    try {
        if ($remoteDirectory -and $sshOptions) {
            & ssh.exe @sshOptions "ubuntu@$hostIp" "rm -f $remoteDirectory/install.py; rmdir $remoteDirectory"
            if ($LASTEXITCODE -ne 0) { Write-Warning 'Nonsecret remote installer cleanup needs operator review' }
        }
    }
    finally {
      try {
        if ($opened) {
            $operation = Invoke-AwsJson @('lightsail', 'close-instance-public-ports', '--instance-name', $instance, '--port-info', "fromPort=22,toPort=22,protocol=tcp,cidrs=$cidr")
            Wait-Operation $operation.operation.id
            $ports = (Invoke-AwsJson @('lightsail', 'get-instance-port-states', '--instance-name', $instance)).portStates
            $remaining = @($ports | Where-Object { $_.protocol -eq 'tcp' -and $_.fromPort -eq 22 } | ForEach-Object { $_.cidrs })
            if ($remaining -contains $cidr) { throw 'Temporary SSH rule remains: operator cleanup required' }
            foreach ($existing in $prior) {
                if ($remaining -notcontains $existing) { throw 'Original SSH rule missing: operator review required' }
            }
            Write-Output 'TEMPORARY_SSH_RULE_REMOVED_ORIGINAL_CIDRS_PRESERVED'
        }
    }
      finally {
        $resolved = [IO.Path]::GetFullPath($temporary)
        $expectedParent = [IO.Path]::GetFullPath($env:TEMP).TrimEnd('\') + '\'
        if (-not $resolved.StartsWith($expectedParent, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe cleanup path' }
        foreach ($path in @($private, $certificate, $resolved)) {
            if ($path -and (Test-Path -LiteralPath $path)) {
                & icacls.exe $path /grant:r "${env:USERNAME}:F" | Out-Null
                if ($LASTEXITCODE -ne 0) { throw 'Temporary credential cleanup ACL failed' }
            }
        }
        Remove-Item -LiteralPath $resolved -Recurse -Force
      }
    }
}
