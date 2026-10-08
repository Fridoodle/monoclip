"""Exercise actual cmd.exe -> powershell.exe argument parsing without downloads."""
import json,os,pathlib,shutil,subprocess,tempfile,unittest
ROOT=pathlib.Path(__file__).resolve().parents[1]
PROBE='''param([string]$Destination=$PSScriptRoot)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath($Destination)
@{destination=$root;scriptRoot=$PSScriptRoot} | ConvertTo-Json | Set-Content -Encoding UTF8 -LiteralPath $env:MONOCLIP_PROBE
'''
@unittest.skipUnless(os.name=='nt','Windows argument parsing test')
class LauncherTests(unittest.TestCase):
 def test_launcher_uses_exact_own_folder(self):
  with tempfile.TemporaryDirectory(prefix='MonoClip-launcher-') as temp:
   for name in ['MonoClip','Mono Clip setup','MonoClip ä test','MonoClip (setup) & test']:
    with self.subTest(folder=name):
     folder=pathlib.Path(temp)/name;folder.mkdir();cmd=folder/'Runtime einrichten.cmd'
     shutil.copy2(ROOT/'tools/runtime-setup.cmd',cmd)
     (folder/'install-runtime.ps1').write_text(PROBE,encoding='utf-8-sig')
     output=pathlib.Path(temp)/'argument-result.json';output.unlink(missing_ok=True)
     env=os.environ.copy();env['MONOCLIP_PROBE']=str(output)
     result=subprocess.run('"'+str(cmd)+'"',shell=True,input=b'\r\n\r\n',capture_output=True,env=env,timeout=30)
     self.assertTrue(output.exists(),result.stdout.decode(errors='replace')+result.stderr.decode(errors='replace'))
     data=json.loads(output.read_text(encoding='utf-8-sig'));self.assertEqual(pathlib.Path(data['destination']),folder)
     self.assertEqual(result.returncode,0)
 @unittest.skipUnless(os.environ.get('MONOCLIP_RUNTIME_TEST')=='1','Opt-in official runtime download integration test')
 def test_actual_installer_via_cmd(self):
  with tempfile.TemporaryDirectory(prefix='MonoClip full setup ') as temp:
   folder=pathlib.Path(temp)/'Mono Clip (runtime)';folder.mkdir()
   shutil.copy2(ROOT/'tools/runtime-setup.cmd',folder/'Runtime einrichten.cmd')
   shutil.copy2(ROOT/'tools/install-runtime.ps1',folder/'install-runtime.ps1')
   result=subprocess.run('"'+str(folder/'Runtime einrichten.cmd')+'"',shell=True,input=b'\r\n\r\n',capture_output=True,timeout=300)
   self.assertEqual(result.returncode,0,result.stdout.decode(errors='replace')+result.stderr.decode(errors='replace'))
   self.assertTrue((folder/'obs.dll').is_file());self.assertTrue((folder/'runtime/data/libobs/default.effect').is_file())
   origin=json.loads((folder/'OBS-ORIGIN.json').read_text(encoding='utf-8-sig'))
   self.assertEqual(origin['sha256'],'4d6e40e3ab155f56b30de517380566a206d74b63cdf5ad49aa596924768f97e1')
if __name__=='__main__':unittest.main(verbosity=2)
