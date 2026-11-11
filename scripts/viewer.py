"""Create private Unity workspaces, build, and deploy only to selected devices."""
import json
import os
from pathlib import Path
import shutil
import subprocess
import time
import zipfile
from pipeline import ROOT, FLAGS, canonical, digest, executable, write_json


def build(model,placement,work,target,cfg):
    from model import ply_info
    count,*_=ply_info(model)
    if target=='quest' and count>cfg['quest']['splat_cap']: raise ValueError('Model exceeds the configured Quest splat budget; use the trained Quest model.')
    unity=executable(cfg,'unity')
    # Unity/Android tools still encounter legacy Windows path-length limits.
    local=Path(os.environ.get('LOCALAPPDATA',Path.home()/'.local/share'))
    project=local/'SV'/canonical(str(work.resolve()))[:12]/f'u-{target}'
    write_json(work/f'unity-{target}.json',dict(project=str(project)))
    fingerprint=dict(template=canonical({str(p.relative_to(ROOT/'unity')):digest(p) for p in (ROOT/'unity').rglob('*') if p.is_file() and p.suffix!='.meta'}),model=digest(model),placement=digest(placement),target=target,render_scale=cfg[target]['render_scale'])
    guard=project/'viewer-input.json'
    if guard.exists():
        previous=json.loads(guard.read_text())
        if any(previous[k]!=fingerprint[k] for k in fingerprint if k!='template'):
            raise ValueError('Viewer inputs changed. Use another --work directory for a comparison build.')
    project.mkdir(parents=True,exist_ok=True)
    required=max(6*1024**3,model.stat().st_size*5+3*1024**3)
    if shutil.disk_usage(project).free<required:
        raise RuntimeError(f'Unity cache drive needs at least {required/1024**3:.1f} GiB free before building. Cache: {project}')
    lock=project/'Temp/UnityLockfile'
    if lock.exists():
        try:
            with lock.open('r+b'): pass
        except PermissionError as error:
            raise RuntimeError('This generated Unity project is open; close its editor before building.') from error
    shutil.copytree(ROOT/'unity',project,dirs_exist_ok=True)
    write_json(guard,fingerprint)
    destination=work/'builds'/target;destination.mkdir(parents=True,exist_ok=True)
    artifact=destination/('ScanViewer.exe' if target=='pc' else 'ScanViewer.apk')
    request=dict(ply=str(model),placement=str(placement),output=str(artifact),target=target,renderScale=cfg[target]['render_scale'])
    request_path=work/f'build-{target}.json';write_json(request_path,request)
    log=work/f'build-{target}.log';started=time.time()
    args=[str(unity),'-batchmode','-quit','-accept-apiupdate','-force-d3d12','-projectPath',str(project),'-buildTarget','Win64' if target=='pc' else 'Android','-executeMethod','ScanViewer.Editor.BuildViewer.Build','-viewerRequest',str(request_path),'-logFile',str(log)]
    print(f'Building {target}; log: {log}',flush=True)
    setup=args.copy();setup[setup.index('ScanViewer.Editor.BuildViewer.Build')]='ScanViewer.Editor.BuildViewer.Configure'
    setup[setup.index(str(log))]=str(work/f'configure-{target}.log')
    def editor(command):
        with subprocess.Popen(command,creationflags=FLAGS) as child:
            try:
                while child.poll() is None:
                    if min(shutil.disk_usage(project).free,shutil.disk_usage(work).free)<2*1024**3:
                        child.terminate();child.wait()
                        raise RuntimeError('Stopped the generated Unity build before its disk filled. Free space and rerun; original inputs are intact.')
                    time.sleep(3)
                if child.returncode:raise subprocess.CalledProcessError(child.returncode,command)
            except BaseException:
                if child.poll() is None:child.terminate();child.wait()
                raise
    editor(setup)
    editor(args)
    if not artifact.is_file() or artifact.stat().st_mtime<started or '[ScanViewer] Build verified' not in log.read_text(errors='replace'): raise RuntimeError('Unity did not produce a fresh verified player.')
    if target=='quest':
        with zipfile.ZipFile(artifact) as z:
            required={'lib/arm64-v8a/libunity.so','lib/arm64-v8a/libUnityOpenXR.so','lib/arm64-v8a/libopenxr_loader.so'}
            if z.testzip() or not required.issubset(z.namelist()): raise RuntimeError('APK integrity/OpenXR validation failed')
        if list(destination.glob('*.obb')): raise RuntimeError('Expected a self-contained APK')
        verify_apk(artifact,unity)
    write_json(destination/'receipt.json',dict(**fingerprint,artifact=str(artifact),sha256=digest(artifact),bytes=artifact.stat().st_size,headset_test='Not tested by this build command'))
    return artifact


def verify_apk(apk,unity):
    android=unity.parent/'Data/PlaybackEngines/AndroidPlayer'
    versions=[p for p in (android/'SDK/build-tools').iterdir() if (p/'aapt.exe').is_file() and (p/'lib/apksigner.jar').is_file()]
    if not versions:raise RuntimeError('Android build tools are missing; install Android support through Unity Hub.')
    version=sorted(versions,key=lambda p:tuple(int(v) for v in p.name.split('.') if v.isdigit()))[-1]
    subprocess.run([str(android/'OpenJDK/bin/java.exe'),'-jar',str(version/'lib/apksigner.jar'),'verify',str(apk)],check=True,creationflags=FLAGS)
    badging=subprocess.check_output([str(version/'aapt.exe'),'dump','badging',str(apk)],text=True,encoding='utf-8',creationflags=FLAGS)
    if "package: name='org.scanviewer.local'" not in badging or "native-code: 'arm64-v8a'" not in badging:
        raise RuntimeError('APK identity or architecture differs from the standalone viewer.')
    return True


def select_device(output,serial=None):
    rows=[line.split() for line in output.splitlines()[1:] if line.strip() and not line.startswith('*')]
    ready=[r[0] for r in rows if len(r)>=2 and r[1]=='device']
    if serial:
        if serial not in ready: raise RuntimeError('Selected device is not authorized/online. Accept USB debugging inside the headset.')
        return serial
    if len(ready)!=1: raise RuntimeError('Need exactly one authorized headset, or specify --serial. Accept debugging inside the headset; the script cannot do that for you.')
    return ready[0]


def install(apk,cfg,serial=None,open_app=False):
    if not apk.is_file(): raise FileNotFoundError(apk)
    adb=str(executable(cfg,'adb'))
    listing=subprocess.check_output([adb,'devices'],text=True,creationflags=FLAGS)
    device=select_device(listing,serial)
    subprocess.run([adb,'-s',device,'install','-r',str(apk)],check=True,creationflags=FLAGS)
    if open_app: subprocess.run([adb,'-s',device,'shell','monkey','-p','org.scanviewer.local','-c','android.intent.category.LAUNCHER','1'],check=True,creationflags=FLAGS)
    print('Installed. Open Scan Viewer in Unknown Sources. Disconnect the PC to confirm standalone operation.')


def launch(exe):
    if not exe.is_file(): raise FileNotFoundError(exe)
    env=os.environ.copy()
    runtime=Path(os.environ.get('ProgramFiles','C:/Program Files'))/'Oculus/Support/oculus-runtime/oculus_openxr_64.json'
    if runtime.is_file(): env['XR_RUNTIME_JSON']=str(runtime)
    startup=None
    if os.name=='nt':
        startup=subprocess.STARTUPINFO();startup.dwFlags|=subprocess.STARTF_USESHOWWINDOW;startup.wShowWindow=4
    subprocess.Popen([str(exe),'-screen-fullscreen','0','-screen-width','640','-screen-height','480'],cwd=exe.parent,env=env,startupinfo=startup)
    print('PC viewer open. Connect Quest Link/Air Link inside the headset. Do not minimize or hide the player window.')
