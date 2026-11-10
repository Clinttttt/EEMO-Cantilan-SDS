#!/usr/bin/env python3
"""One-time operator configuration; reads the credential from SSH stdin, never arguments."""
import copy
import fcntl
import hashlib
import importlib.machinery
import json
import os
from pathlib import Path
import stat
import sys
import tempfile

SHA = '12b68664d1ce1c72255884e9554ca70677659eb9'
API = 'sha256:8e0c407e07d6fb46b1009a5c4d83db84c5099abfa65eb33affe5b3b4029ea691'
CLIENT = 'sha256:e13d873bd7564d735bf518da065148dbca8a3079a48bd915d1765a1a90d51e02'
GATES = {
    'stalltrack-stack-deploy': '3a08192083b7aef929b659cf3be20116ed42640871999ba86a732d402f67cfec',
    'stalltrack-client-deploy': 'bb87beecc927b08c7901dac5fdf8845426b38040292303269acc5526d22ff755',
}
SECRET = Path('/etc/stalltrack-firebase/collector-admin.json')
TARGET = '/run/secrets/stalltrack_admin.json'
STAGE = 'root-and-gate-identity'
BASE_CHECKSUM = 'b9a867e01491e7ecb3606eb727514dd9ffd7df3fa4cb7ddcfe8cd2fa9df38737'


def validate_original_base(raw, uid, gid, mode, symlink):
    # This one-time operation accepts only the observed legacy base, then makes it root-private.
    if symlink or (uid, gid, mode) != (1000, 1000, 0o664) or hashlib.sha256(raw).hexdigest() != BASE_CHECKSUM:
        raise RuntimeError('API base differs from the reviewed production baseline')


def updated_base(raw):
    lines = raw.splitlines()
    if lines[:2] != ['services:', '  api:']:
        raise RuntimeError('Unexpected API base structure')
    if any(line.startswith('  ') and not line.startswith('    ') and line != '  api:'
           for line in lines[1:] if line.strip() and not line.lstrip().startswith('#')):
        raise RuntimeError('Additional service in API base')
    if lines.count('    environment:') != 1 or any('Firebase' in line or line.startswith('    volumes:') for line in lines):
        raise RuntimeError('Existing Firebase or volume configuration requires review')
    # Insert at the environment header so later service properties cannot capture this setting.
    index = lines.index('    environment:') + 1
    lines.insert(index, '      Firebase__ServiceAccountPath: ' + TARGET)
    lines.extend(['    volumes:', '      - ' + str(SECRET) + ':' + TARGET + ':ro'])
    return '\n'.join(lines) + '\n'


def validate_delta(before, after):
    expected = copy.deepcopy(before)
    api = expected['services']['api']
    if any(key.startswith('Firebase') for key in api.get('environment', {})):
        raise RuntimeError('Existing effective Firebase settings require review')
    api.setdefault('environment', {})['Firebase__ServiceAccountPath'] = TARGET
    api.setdefault('volumes', []).append({
        'type': 'bind', 'source': str(SECRET), 'target': TARGET,
        'read_only': True, 'bind': {'create_host_path': True},
    })
    if expected != after:
        raise RuntimeError('Effective configuration differs beyond Firebase mount/path')
    if str(api['environment'].get('Database__ApplyMigrationsAtStartup', '')).lower() != 'false':
        raise RuntimeError('Startup migrations must remain disabled')


def atomic_write(path, content, uid, gid, mode):
    fd, name = tempfile.mkstemp(prefix='.fcm-', dir=path.parent)
    try:
        os.fchown(fd, uid, gid)
        os.fchmod(fd, mode)
        with os.fdopen(fd, 'wb') as stream:
            stream.write(content)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(name, path)
    finally:
        if os.path.exists(name):
            os.unlink(name)


def main():
    global STAGE
    if os.geteuid() != 0:
        raise RuntimeError('Authorized root operator session required')
    os.umask(0o077)
    for name, digest in GATES.items():
        path = Path('/usr/local/sbin') / name
        metadata = path.stat()
        if path.is_symlink() or metadata.st_uid != 0 or metadata.st_mode & 0o022 or hashlib.sha256(path.read_bytes()).hexdigest() != digest:
            raise RuntimeError('Installed gate identity changed; review required')
    gate = importlib.machinery.SourceFileLoader('fcm_stack_gate', '/usr/local/sbin/stalltrack-stack-deploy').load_module()
    g = gate.g
    with open('/var/lock/stalltrack-client-deploy.lock', 'a') as lock:
        STAGE = 'deployment-lock'
        fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        STAGE = 'release-approval'
        gate.approved(SHA, CLIENT, API)
        STAGE = 'live-layout-backup-readiness'
        baseline = g.check_live()
        basefiles = g.compose_files(baseline['portal'])
        g.check_backup()
        g.readiness()
        for service, digest in [('api', API), ('portal', CLIENT)]:
            if baseline[service]['Config']['Image'].split('@')[-1] != digest:
                raise RuntimeError('Running release changed; review required')
        if baseline['api']['Config']['User'] != '1654':
            raise RuntimeError('API UID changed; review required')
        if SECRET.exists() or SECRET.is_symlink() or SECRET.parent.exists():
            raise RuntimeError('Existing Firebase secret directory requires review')
        STAGE = 'credential-validation'
        raw_secret = sys.stdin.buffer.read(65537)
        if len(raw_secret) > 65536:
            raise RuntimeError('Oversized credential')
        try:
            credential = json.loads(raw_secret.decode('utf-8-sig'))
            valid = (credential.get('type') == 'service_account'
                     and credential.get('project_id') == 'stalltrack-56b8a'
                     and credential.get('private_key', '').startswith('-----BEGIN PRIVATE KEY-----')
                     and credential.get('token_uri') == 'https://oauth2.googleapis.com/token'
                     and credential.get('client_email', '').endswith('@stalltrack-56b8a.iam.gserviceaccount.com'))
        except Exception:
            valid = False
        if not valid:
            raise RuntimeError('Invalid credential or Firebase project mismatch')
        STAGE = 'api-base-validation'
        base = g.BASE / 'compose.v10-clean-db.yaml'
        metadata = base.stat()
        original = base.read_bytes()
        validate_original_base(original, metadata.st_uid, metadata.st_gid,
                               stat.S_IMODE(metadata.st_mode), base.is_symlink())
        changed = updated_base(original.decode('utf-8')).encode('utf-8')
        argv = ['docker', 'compose', '-p', g.PROJECT]
        for filename in baseline['api']['Config']['Labels']['com.docker.compose.project.config_files'].split(','):
            argv.extend(['-f', filename])

        def compose(*args):
            return g.command(argv + list(args), timeout=150, cwd=str(g.BASE))

        STAGE = 'effective-config-before'
        before = json.loads(compose('config', '--format', 'json'))
        backup = g.BASE / 'config' / 'compose.v10-clean-db.before-fcm-activation.yaml'
        if backup.exists():
            raise RuntimeError('Existing activation backup requires review')
        atomic_write(backup, original, 0, 0, 0o600)
        installed = False
        changed_base = False
        try:
            STAGE = 'secret-install-and-config-validation'
            SECRET.parent.mkdir(mode=0o750)
            os.chown(SECRET.parent, 0, 1654)
            atomic_write(SECRET, raw_secret, 0, 1654, 0o440)
            installed = True
            changed_base = True
            atomic_write(base, changed, 0, 0, 0o600)
            after = json.loads(compose('config', '--format', 'json'))
            validate_delta(before, after)
            STAGE = 'release-revalidation'
            gate.approved(SHA, CLIENT, API)
            g.log('firebase_configuration_validated', sha=SHA)
            STAGE = 'api-recreation-and-readiness'
            compose('up', '-d', '--no-deps', '--no-build', '--pull', 'never', 'api')
            gate.stack_ready(baseline, baseline['api']['Image'], baseline['portal']['Image'])
            live = g.check_live()
            if live['portal']['Id'] != baseline['portal']['Id']:
                raise RuntimeError('Portal container changed')
            g.command(['docker', 'exec', g.CONTAINERS['api'], 'sh', '-c', 'test -r ' + TARGET + ' && test -s ' + TARGET])
            g.log('firebase_configuration_installed', sha=SHA, api_container=live['api']['Id'])
            print('FIREBASE_SECRET_MOUNT_INSTALLED_API_HEALTHY_PROTECTED_CONTAINERS_UNCHANGED')
        except Exception:
            if changed_base:
                atomic_write(base, original, metadata.st_uid, metadata.st_gid, stat.S_IMODE(metadata.st_mode))
                compose('up', '-d', '--no-deps', '--no-build', '--pull', 'never', 'api')
                gate.stack_ready(baseline, baseline['api']['Image'], baseline['portal']['Image'])
            if installed:
                SECRET.unlink()
                SECRET.parent.rmdir()
            raise RuntimeError('Firebase activation failed; configuration restored') from None


if __name__ == '__main__':
    try:
        main()
    except Exception as error:
        # Credential and resolved Compose data must never escape through exception details.
        print('FCM_ACTIVATION_REFUSED_OR_FAILED stage=' + STAGE + ' type=' + type(error).__name__, file=sys.stderr)
        sys.exit(1)
