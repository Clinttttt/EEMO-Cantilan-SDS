import copy
import importlib.util
import hashlib
from pathlib import Path
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location('fcm', Path(__file__).with_name('configure-fcm-production.py'))
fcm = importlib.util.module_from_spec(spec)
spec.loader.exec_module(fcm)


class ConfigurationSafetyTests(unittest.TestCase):
    def setUp(self):
        self.before = {'services': {'api': {'environment': {
            'Database__ApplyMigrationsAtStartup': 'false', 'ExistingSecret': 'preserve'},
            'image': 'unchanged'}, 'db': {'image': 'untouched'}}}
        self.after = copy.deepcopy(self.before)
        api = self.after['services']['api']
        api['environment']['Firebase__ServiceAccountPath'] = fcm.TARGET
        api['volumes'] = [{'type': 'bind', 'source': str(fcm.SECRET), 'target': fcm.TARGET,
                           'read_only': True, 'bind': {'create_host_path': True}}]

    def test_only_expected_mount_and_path_are_accepted(self):
        fcm.validate_delta(self.before, self.after)
        self.assertNotIn('Firebase__ServiceAccountPath', self.before['services']['api']['environment'])

    def test_image_database_secret_and_writable_mount_changes_are_rejected(self):
        for target, key, value in [('api', 'image', 'other'), ('db', 'image', 'other')]:
            after = copy.deepcopy(self.after)
            after['services'][target][key] = value
            with self.assertRaises(RuntimeError):
                fcm.validate_delta(self.before, after)
        after = copy.deepcopy(self.after)
        after['services']['api']['environment']['ExistingSecret'] = 'changed'
        with self.assertRaises(RuntimeError):
            fcm.validate_delta(self.before, after)
        self.after['services']['api']['volumes'][0]['read_only'] = False
        with self.assertRaises(RuntimeError):
            fcm.validate_delta(self.before, self.after)

    def test_startup_migrations_must_remain_disabled(self):
        for config in (self.before, self.after):
            config['services']['api']['environment']['Database__ApplyMigrationsAtStartup'] = 'true'
        with self.assertRaises(RuntimeError):
            fcm.validate_delta(self.before, self.after)

    def test_environment_setting_is_inserted_before_later_service_properties(self):
        raw = 'services:\n  api:\n    environment:\n      Existing: preserve\n    restart: unless-stopped\n'
        changed = fcm.updated_base(raw)
        self.assertIn('    environment:\n      Firebase__ServiceAccountPath: ' + fcm.TARGET, changed)
        self.assertIn('      Existing: preserve\n    restart: unless-stopped\n    volumes:', changed)
        self.assertIn(':ro\n', changed)

    def test_unknown_structure_existing_mount_or_firebase_are_rejected(self):
        for raw in ['services:\n  portal:\n', 'services:\n  api:\n  db:\n',
                    'services:\n  api:\n    environment:\n    volumes:\n',
                    'services:\n  api:\n    environment:\n      Firebase__ServiceAccountJson: existing\n']:
            with self.assertRaises(RuntimeError):
                fcm.updated_base(raw)

    def test_only_exact_observed_legacy_base_can_be_hardened(self):
        raw = b'reviewed base fixture'
        with patch.object(fcm, 'BASE_CHECKSUM', hashlib.sha256(raw).hexdigest()):
            fcm.validate_original_base(raw, 1000, 1000, 0o664, False)
            for content, uid, gid, mode, symlink in [
                (b'changed', 1000, 1000, 0o664, False),
                (raw, 1001, 1000, 0o664, False),
                (raw, 1000, 1000, 0o666, False),
                (raw, 1000, 1000, 0o664, True),
            ]:
                with self.assertRaises(RuntimeError):
                    fcm.validate_original_base(content, uid, gid, mode, symlink)


if __name__ == '__main__':
    unittest.main()
