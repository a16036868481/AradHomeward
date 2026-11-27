from pathlib import Path
import shutil
import sqlite3
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import Mock, patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]/'trainer'))
import first_run
import login_service as service


class LoginStartupTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.standard, self.compatible = service.executables(self.root)
        self.legacy_x86 = service.legacy_x86_executable(self.root)
        self.primary = self.standard.parent/'private'
        self.mirror = self.legacy_x86.parent/'private'
        self.client = self.root/'DNF60_ACT12/ACT12/config/registration.json'

    def test_recover_verified_mirror_after_interrupted_setup(self):
        first_run.ensure_identity(self.root)
        pfx = (self.primary/'registration.pfx').read_bytes()
        password = (self.primary/'certificate-password.txt').read_bytes()
        (self.primary/'registration.pfx').unlink()
        (self.mirror/'certificate-password.txt').unlink()
        self.client.unlink()
        first_run.ensure_identity(self.root)
        self.assertEqual((self.primary/'registration.pfx').read_bytes(), pfx)
        self.assertEqual((self.mirror/'certificate-password.txt').read_bytes(), password)
        self.assertEqual(len(service.read_json(self.client)['CertificateSha256']), 64)

    def test_conflicting_pin_preserves_original(self):
        first_run.ensure_identity(self.root)
        pfx = (self.primary/'registration.pfx').read_bytes()
        config = service.read_json(self.client)
        service.save_json(self.client, dict(config, CertificateSha256='0'*64))
        with self.assertRaisesRegex(RuntimeError, 'LOGIN-CERT-MISMATCH'):
            first_run.ensure_identity(self.root)
        self.assertEqual((self.primary/'registration.pfx').read_bytes(),pfx)
        self.assertEqual(service.read_json(self.client)['CertificateSha256'],'0'*64)

    def test_no_key_replacement_for_existing_install(self):
        first_run.ensure_identity(self.root)
        for folder in (self.primary,self.mirror):
            (folder/'registration.pfx').unlink()
        with self.assertRaisesRegex(RuntimeError,'LOGIN-CERT-MISSING'):
            first_run.ensure_identity(self.root)
        self.assertFalse((self.primary/'registration.pfx').exists())

    def test_stale_pid_and_hijacked_port_do_not_receive_requests(self):
        service.save_json(self.root/'logs/login-ports.json',dict(pid=12,loginPort=9999,managementPort=8080))
        with patch.object(service,'process_path',return_value=self.compatible), \
             patch.object(service,'listeners',return_value={9999:{12},8080:{22}}), \
             patch('http.client.HTTPConnection') as http:
            with self.assertRaisesRegex(service.StartupError,'管理服务'):
                service.management_request(self.root,'/api/backups/create',{})
            http.assert_not_called()

    def test_windows_without_special_apc_probes_compatible_x64_directly(self):
        with patch.object(service, 'supports_native_apc', return_value=False), \
             patch.object(service, 'probe_runtime', return_value={'Bitness': 64}) as probe:
            image, info = service.select_backend(self.root)
        self.assertEqual(image, self.compatible)
        self.assertEqual(info['runtime']['Bitness'], 64)
        probe.assert_called_once_with(self.compatible)

    def test_standard_runtime_failure_uses_compatible_x64(self):
        self.standard.parent.mkdir(parents=True)
        self.standard.touch()
        failures = [service.StartupError('LOGIN-RUNTIME', 'x64 initialization failure'), {'Bitness': 64}]
        with patch.object(service, 'supports_native_apc', return_value=True), \
             patch.object(service, 'probe_runtime', side_effect=failures) as probe:
            image, info = service.select_backend(self.root)
        self.assertEqual(image, self.compatible)
        self.assertEqual(info['previousAttempts'][0]['code'], 'LOGIN-RUNTIME')
        self.assertEqual([call.args for call in probe.call_args_list], [(self.standard,), (self.compatible,)])

    def test_compatible_x64_session_is_reused_only_from_this_installation(self):
        session = dict(pid=12, loginPort=9999, managementPort=8080)
        table = {9999: {12}, 8080: {12}}
        service.save_json(self.root/'logs/login-ports.json', session)
        with patch.object(service, 'process_path', return_value=self.compatible):
            self.assertEqual(service.active_session(self.root, table), session)
        for image in (self.legacy_x86, self.root/'other'/self.compatible.name):
            with patch.object(service, 'process_path', return_value=image):
                self.assertIsNone(service.active_session(self.root, table))

    def test_upgrade_does_not_start_a_second_backend_while_old_x86_is_running(self):
        service.save_json(self.client, {'CertificateSha256': '0'*64})
        with patch.object(service, 'process_path', return_value=self.legacy_x86), \
             patch.object(service, 'listeners', return_value={9999: {12}, 8080: {12}}), \
             patch.object(service, 'select_backend') as select:
            with self.assertRaises(service.StartupError) as error:
                service.ensure(self.root)
            self.assertEqual(error.exception.code, 'LOGIN-RESTART-REQUIRED')
            select.assert_not_called()

    def test_runtime_probe_rejects_a_32_bit_binary(self):
        self.compatible.parent.mkdir(parents=True)
        self.compatible.touch()
        self.compatible.with_name('a12_sqlite3.dll').touch()
        result = subprocess.CompletedProcess([], 0, b'{"Bitness":32}')
        with patch.object(service.subprocess, 'run', return_value=result):
            with self.assertRaisesRegex(service.StartupError, '64 位'):
                service.probe_runtime(self.compatible)

    def test_slow_login_can_be_ready_after_twelve_seconds(self):
        child = Mock(pid=77)
        child.poll.return_value = None
        times = iter([0, 0, 14, 26])
        with patch.object(service.time,'monotonic',side_effect=lambda:next(times)), \
             patch.object(service.time,'sleep'), \
             patch.object(service,'listeners',side_effect=[{}, {}, {9999:{77},8080:{77}}]), \
             patch.object(service,'verified_tls') as tls, \
             patch.object(service,'management_request',return_value={'accounts':0,'characters':0}) as http:
            service.await_ready(child,self.root,9999,8080,'pin',None)
            tls.assert_called_once()
            http.assert_called_once()

    def test_failed_or_unresponsive_child_has_distinct_diagnostics(self):
        from io import BytesIO
        child = Mock(pid=81,returncode=1)
        child.poll.return_value = 1
        with self.assertRaises(service.StartupError) as early:
            service.await_ready(child,self.root,9999,8080,'pin',BytesIO(b'SQLite cannot open database'))
        self.assertEqual(early.exception.code,'LOGIN-DATABASE')
        child.poll.return_value = None
        with patch.object(service.time,'monotonic',side_effect=[0,91]), self.assertRaises(service.StartupError) as late:
            service.await_ready(child,self.root,9999,8080,'pin',None)
        self.assertEqual(late.exception.code,'LOGIN-READINESS-TIMEOUT')

    def run_first_setup(self):
        destination=self.root/'trainer'
        destination.mkdir(exist_ok=True)
        for name in ('first_run.py','login_service.py','pet_evolution.py'):
            shutil.copyfile(Path(first_run.__file__).with_name(name),destination/name)
        result=subprocess.run([sys.executable,'-I','-B',str(destination/'first_run.py')],capture_output=True,timeout=35)
        self.assertNotEqual(result.returncode,0)
        return service.read_json(self.root/'logs/login-startup.json')['code']

    def test_missing_or_corrupt_save_is_reported_without_replacement(self):
        db=self.root/'DNF60_ACT12/ACT12_Server/dnf_a12.sqlite'
        self.assertEqual(self.run_first_setup(),'LOGIN-DATABASE-MISSING')
        self.assertFalse(db.exists())
        bad=b'invalid save fixture'
        db.write_bytes(bad)
        self.assertEqual(self.run_first_setup(),'LOGIN-DATABASE')
        self.assertEqual(db.read_bytes(),bad)

    def test_busy_database_gets_its_own_error_code(self):
        self.primary.parent.parent.mkdir(parents=True)
        db=sqlite3.connect(self.primary.parent.parent/'dnf_a12.sqlite')
        try:
            db.execute('CREATE TABLE held (value)')
            db.execute('BEGIN EXCLUSIVE')
            self.assertEqual(self.run_first_setup(),'LOGIN-DATABASE-BUSY')
            db.rollback()
            self.assertEqual(db.execute('PRAGMA quick_check').fetchone()[0],'ok')
        finally:
            db.close()


if __name__ == '__main__':
    unittest.main()
