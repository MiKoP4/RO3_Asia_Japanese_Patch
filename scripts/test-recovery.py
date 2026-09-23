"""Codex quiet-test wrapper: recovery and install regression checks in disposable clients."""
import hashlib
import pathlib
import shutil
import subprocess
import tempfile
import sys

repo = pathlib.Path(__file__).resolve().parents[1]
names = ['recovery-compatibility-manifest.json', 'LuaPayload/Localization_en.lua.bytes',
         'LuaPayload/Localization_zh_CN.lua.bytes', 'LuaPayload/Localization_zh_TW.lua.bytes']
failures = []
cases = ['restore', 'repeat', 'missing-backup', 'unknown-manifest', 'missing-state', 'install']
if len(sys.argv) > 1:
    cases = [sys.argv[1]]
for case in cases:
    try:
        with tempfile.TemporaryDirectory(prefix='ro3-recovery-') as temp:
            root = pathlib.Path(temp)
            client = root / 'Client (test) & spaces'
            client.mkdir()
            (client / 'ro3.exe').write_bytes(b'placeholder')
            plugin = client / 'BepInEx/plugins/RO3.LocalizationTablePatcher.dll'
            plugin.parent.mkdir(parents=True)
            plugin.write_bytes(b'old')
            state = client / 'BepInEx/config/RO3.RecoveryPatchState.txt'
            state.parent.mkdir()
            state.write_text('PATCHED_MANIFEST_SHA256=' + hashlib.sha256(b'patched').hexdigest())
            recovery = client / 'ro3_Data/StreamingAssets/Recovery'
            for name in names:
                p = recovery / name
                p.parent.mkdir(parents=True, exist_ok=True)
                p.write_bytes(b'patched')
                pathlib.Path(str(p) + '.ro3-ja-original').write_bytes(b'original-' + name.encode())
            if case == 'missing-backup':
                pathlib.Path(str(recovery / names[-1]) + '.ro3-ja-original').unlink()
            if case == 'unknown-manifest':
                (recovery / names[0]).write_bytes(b'updated official')
            if case == 'missing-state':
                state.unlink()
            package = root / 'package'
            shutil.copytree(repo / 'packaging', package)
            if case == 'install':
                payload = package / 'payload'
                (payload / 'BepInEx/plugins').mkdir(parents=True)
                (payload / 'winhttp.dll').write_bytes(b'test')
                (payload / 'BepInEx/plugins/RO3.LocalizationTablePatcher.dll').write_bytes(b'new')
            for _ in range(2 if case == 'repeat' else 1):
                result = subprocess.run(['cmd.exe', '/d', '/c', str(package / (
                    'Install-Japanese.bat' if case == 'install' else 'Recover-Japanese.bat')),
                    str(client), '--no-pause'], capture_output=True, timeout=30)
                expected_success = case in ('restore', 'repeat', 'install')
                if len(sys.argv) > 1:
                    print(result.stdout.decode(errors='replace'), result.stderr.decode(errors='replace'))
                assert (result.returncode == 0) == expected_success
            assert not plugin.exists() or (case == 'install' and plugin.read_bytes() == b'new')
            assert list(plugin.parent.glob('*.disabled-*'))
            for name in names:
                p = recovery / name
                backup = pathlib.Path(str(p) + '.ro3-ja-original')
                if expected_success:
                    assert p.read_bytes() == backup.read_bytes()
                elif not (case == 'unknown-manifest' and name == names[0]):
                    assert p.read_bytes() == b'patched'
                if not (case == 'missing-backup' and name == names[-1]):
                    assert backup.exists()
    except Exception:
        failures.append(case)
print(f'Quiet test run: {len(cases)-len(failures)}/{len(cases)} passed')
for case in failures:
    print(f'FAILED: {case} - scripts/test-recovery.py')
raise SystemExit(bool(failures))
