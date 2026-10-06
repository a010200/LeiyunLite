import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('generator', ROOT/'Tools/Generate-OpenRazerCapabilities.py')
g = importlib.util.module_from_spec(spec)
spec.loader.exec_module(g)


class GeneratorTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.cache = ROOT/'.planning/openrazer-stage-a/upstream'/g.OPENRAZER_COMMIT
        cls.data, cls.lock = g.load_inputs(cls.cache)
        cls.profiles = g.generate(cls.data)

    def test_deterministic_and_current(self):
        first = g.render(self.profiles, self.lock)
        self.assertEqual(first, g.render(g.generate(self.data), self.lock))
        for path, content in first.items():
            self.assertEqual((ROOT/path).read_bytes(), content.encode('utf-8'), path)

    def test_inheritance_and_attribute_addition(self):
        mouse = '''class Parent(__RazerDevice):
 USB_PID=1
 USB_VID=0x1532
 METHODS=['get_dpi_xy']
 DPI_MAX=16000
 POLL_RATES=[125,500,1000]
 AVAILABLE_DPI=[400,800]
class Child(Parent):
 USB_PID=2
 METHODS=Parent.METHODS+['set_dpi_xy']
'''
        result = g.parse_classes(mouse, 'class RazerDevice: METHODS=[]')
        self.assertEqual(result[1]['METHODS'], ['get_dpi_xy','set_dpi_xy'])
        for key in ('DPI_MAX','POLL_RATES','AVAILABLE_DPI'):
            self.assertEqual(result[0][key],result[1][key])

    def test_no_execution_of_upstream(self):
        with self.assertRaises(g.InvalidInput):
            g.parse_classes("class A(__RazerDevice):\n USB_PID=1\n USB_VID=0x1532\n METHODS=__import__('os').system('whoami')", 'class RazerDevice: METHODS=[]')

    def test_duplicate_pid_rejected(self):
        with self.assertRaises(g.InvalidInput):
            g.parse_classes('class A(__RazerDevice):\n USB_PID=1\n USB_VID=0x1532\n METHODS=[]\nclass B(A): pass', 'class RazerDevice: METHODS=[]')

    def test_inheritance_cycle_and_missing_base_rejected(self):
        for source in ('class A(B): pass\nclass B(A): pass','class A(Absent): USB_PID=1'):
            with self.assertRaises(g.InvalidInput): g.parse_classes(source, 'class RazerDevice: METHODS=[]')

    def test_nested_switch_is_not_pid_evidence(self):
        source = 'switch(device->usb_pid) { case USB_DEVICE_ID_RAZER_A: case USB_DEVICE_ID_RAZER_B: switch(raw) { case 1: break; } request.transaction_id.id=0xFF; break; default: return -1; }'
        groups = g.pid_switches(source, {'USB_DEVICE_ID_RAZER_A':1,'USB_DEVICE_ID_RAZER_B':2})[0]
        self.assertEqual(set(groups), {1,2})
        self.assertEqual(g.one_tid(groups[1]), 0xff)
        with self.assertRaises(g.InvalidInput): g.one_tid('request.transaction_id.id=0x77;')

    def test_hash_mismatch_never_changes_outputs(self):
        before = {p:(ROOT/p).read_bytes() for p in g.render(self.profiles,self.lock)}
        with tempfile.TemporaryDirectory() as directory:
            cache = Path(directory)
            for path in self.lock['sha256']:
                target = cache/path
                target.parent.mkdir(parents=True,exist_ok=True)
                target.write_bytes((self.cache/path).read_bytes())
            (cache/'driver/razermouse_driver.c').write_text('tampered')
            result = subprocess.run([sys.executable,str(ROOT/'Tools/Generate-OpenRazerCapabilities.py'),'--write','--cache',str(cache)],capture_output=True)
            self.assertEqual(result.returncode,2)
            self.assertIn(b'FAIL CLOSED',result.stderr)
        for path,content in before.items(): self.assertEqual((ROOT/path).read_bytes(),content)

    def test_partial_output_failure_restores_previous_files(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory)
            (root/'a.cs').write_bytes(b'old-a')
            (root/'b.md').write_bytes(b'old-b')
            real_replace=g.os.replace
            calls=[]
            def fail_second(source,target):
                calls.append(str(target))
                if len(calls)==2: raise OSError('Injected output failure')
                return real_replace(source,target)
            with patch.object(g.os,'replace',side_effect=fail_second):
                with self.assertRaises(OSError): g.write_outputs(root,{'a.cs':'new-a','b.md':'new-b'})
            self.assertEqual((root/'a.cs').read_bytes(),b'old-a')
            self.assertEqual((root/'b.md').read_bytes(),b'old-b')
            self.assertEqual(sorted(p.name for p in root.iterdir()),['a.cs','b.md'])

    def test_full_pid_and_permission_invariants(self):
        self.assertEqual(len({p['pid'] for p in self.profiles}), len(self.profiles))
        for p in self.profiles:
            if p['set_dpi']:
                self.assertTrue(p['get_dpi'] and p['minimum'] > 0 and p['maximum'] >= p['minimum'])
                self.assertIn(p['dpi_set_tid'],(0x1f,0x3f,0xff))
            if p['set_poll']:
                self.assertTrue(p['get_poll'] and p['rates'])
            if p['set_stages']: self.assertTrue(p['stages'])
            self.assertFalse(set(p['rates'])-set(p['upstream_rates']))
        self.assertEqual(len(self.profiles),117)  # Pinned input sanity, not application logic.

    def test_protocol_representatives_and_per_operation_tid(self):
        by_pid = {p['pid']:p for p in self.profiles}
        for pid in (0x15,0x37):
            self.assertEqual((by_pid[pid]['dpi'],by_pid[pid]['dpi_tid']),('LegacyByte',0xff))
        self.assertEqual(by_pid[0x5c]['dpi_tid'],0x3f)
        for pid in (0xa5,0xa6,0xb6,0xb7,0xc0,0xc1,0xde,0xdf):
            self.assertTrue(by_pid[pid]['stages'] and by_pid[pid]['set_stages'])
        self.assertTrue(by_pid[0xb3]['receiver'])
        self.assertEqual(by_pid[0xb3]['poll_second_tid'],0xff)
        self.assertEqual(by_pid[0x91]['stages_tid'],0xff)
        self.assertEqual(by_pid[0x91]['dpi_tid'],0xff)
        self.assertEqual(by_pid[0x91]['poll_tid'],0x1f)

    def test_transport_and_discrete_dpi_are_not_guessed(self):
        by_pid = {p['pid']:p for p in self.profiles}
        for pid in (0x13,0x16,0x29): self.assertEqual(by_pid[pid]['transport'],'LegacyDirectUsbControl')
        for pid in (0x96,0x99,0xcb): self.assertEqual(by_pid[pid]['transport'],'AlternateUsbReportIndex')
        for p in self.profiles:
            if p['dpi'] == 'LegacyByte':
                self.assertNotIn(800,p['available'])
                for dpi in p['available']: self.assertEqual(dpi,int(round(int(round(dpi/6750*255,2))/255*6750,2)))

    def test_identity_local_overrides_survive(self):
        baseline=json.loads((ROOT/'Tools/OpenRazer/identity-baseline.json').read_text(encoding='utf-8'))
        self.assertEqual(baseline['00A4']['kind'],'GenericReceiver')
        self.assertEqual(baseline['0203']['kind'],'Other')
        self.assertTrue({0xde,0xdf}.issubset({p['pid'] for p in self.profiles}))


if __name__ == '__main__': unittest.main(verbosity=2)
