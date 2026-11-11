"""Local native dual-fisheye INSV -> Spirula -> OpenXR viewers (Windows)."""
from __future__ import annotations
import argparse
from contextlib import contextmanager
import hashlib
import json
import os
from pathlib import Path
import platform
import shutil
import struct
import subprocess
import sys
import tarfile
import time

ROOT = Path(__file__).resolve().parents[1]
FLAGS = (subprocess.CREATE_NO_WINDOW | subprocess.BELOW_NORMAL_PRIORITY_CLASS) if os.name == 'nt' else 0


def write_json(path, value):
    path = Path(path); path.parent.mkdir(parents=True, exist_ok=True)
    tmp = path.with_suffix(path.suffix + '.partial')
    tmp.write_text(json.dumps(value, indent=2), encoding='utf-8'); tmp.replace(path)


def digest(path):
    with Path(path).open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def canonical(value):
    return hashlib.sha256(json.dumps(value, sort_keys=True).encode()).hexdigest()


def expand(value):
    return Path(os.path.expandvars(value)).expanduser().resolve()


def config(path):
    base = json.loads((ROOT/'examples/config.json').read_text())
    if path:
        supplied = json.loads(Path(path).read_text(encoding='utf-8-sig'))
        unknown = set(supplied) - set(base)
        if unknown: raise ValueError(f'Unknown configuration keys: {unknown}')
        for key, value in supplied.items():
            if isinstance(value, dict):
                unknown=set(value)-set(base[key])
                if unknown:raise ValueError(f'Unknown configuration keys in {key}: {unknown}')
                base[key].update(value)
            else: base[key] = value
    for key in ('steps', 'splat_cap'):
        if any(int(base[p][key]) <= 0 for p in ('pc', 'quest')): raise ValueError('Invalid training budget')
    if base['save_interval'] <= 0 or base['min_free_gib'] < 2: raise ValueError('Invalid disk/checkpoint settings')
    return base


def executable(cfg, name, required=True):
    value = cfg['tools'].get(name)
    p = expand(value) if value else None
    if not p:
        found = shutil.which({'unity':'Unity', 'spirula':'spirula', 'ffprobe':'ffprobe', 'adb':'adb'}.get(name, name))
        p = Path(found) if found else None
    if not p and name == 'unity':
        p = Path(os.environ.get('ProgramFiles', 'C:/Program Files'))/'Unity/Hub/Editor/6000.4.6f1/Editor/Unity.exe'
    if not p and name == 'sam_model':
        p = Path(os.environ.get('LOCALAPPDATA', Path.home()/'AppData/Local'))/'spirula-studio/models/sam2.1_hiera_tiny_f16.ggml'
    if not p and name == 'adb':
        unity = executable(cfg, 'unity', False)
        if unity: p = unity.parent/'Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe'
    if p and p.is_file(): return p.resolve()
    if required: raise FileNotFoundError(f'Set tools.{name} in your private config JSON; executable/model was not found.')
    return None


def capture_input(path):
    path = expand(path)
    if path.is_dir():
        candidates = sorted(p for p in path.iterdir() if p.suffix.lower() == '.insv')
        if len(candidates) != 1: raise ValueError('Folder must contain exactly one INSV; otherwise name the intended file explicitly.')
        path = candidates[0]
    if not path.is_file() or path.suffix.lower() != '.insv': raise ValueError('Expected an original .insv file.')
    return path


def probe(source, cfg):
    raw = subprocess.check_output([str(executable(cfg, 'ffprobe')), '-v', 'error', '-show_streams', '-show_format', '-of', 'json', str(source)], creationflags=FLAGS)
    data = json.loads(raw); tracks = [s for s in data['streams'] if s['codec_type'] == 'video']
    if len(tracks) != 2 or any(s['codec_name'] != 'hevc' or (s['width'], s['height']) != (3840,3840) for s in tracks):
        raise ValueError('This preset supports one native X6 INSV containing two 3840x3840 HEVC lens tracks. Stitched panoramas and other camera modes need another preset.')
    if tracks[0].get('nb_frames') != tracks[1].get('nb_frames') or tracks[0].get('duration_ts') != tracks[1].get('duration_ts'):
        raise ValueError('Lens tracks are not synchronized.')
    a,b = map(int, tracks[0]['avg_frame_rate'].split('/')); fps = a/b
    return dict(duration=float(data['format']['duration']), fps=fps, resolution=[3840,3840], tracks=2)


@contextmanager
def locked(work):
    work.mkdir(parents=True, exist_ok=True)
    with (work/'pipeline.lock').open('a+b') as f:
        f.seek(0); f.write(b'1'); f.flush(); f.seek(0)
        try:
            if os.name == 'nt':
                import msvcrt
                msvcrt.locking(f.fileno(), msvcrt.LK_NBLCK, 1)
            else:
                import fcntl
                fcntl.flock(f, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except OSError as error: raise RuntimeError('Another process owns this scan workspace.') from error
        try: yield
        finally:
            f.seek(0)
            if os.name == 'nt': msvcrt.locking(f.fileno(), msvcrt.LK_UNLCK, 1)
            else: fcntl.flock(f, fcntl.LOCK_UN)


def recovery(checkpoint):
    from model import ply_info
    with tarfile.open(checkpoint/'state.tar', 'r:') as tar:
        members = tar.getmembers()
        state = json.load(tar.extractfile('state.json'))
        expected = {'world.means.npy','world.quats.npy','world.scales.npy','world.opacities.npy'}
        if state.get('full_resume') != 1 or not expected.issubset(m.name for m in members): raise ValueError('Checkpoint cannot resume.')
        if members[-1].offset_data + members[-1].size > (checkpoint/'state.tar').stat().st_size: raise ValueError('Truncated checkpoint')
    step = int(checkpoint.name[5:14])
    if state['step'] != step or ply_info(checkpoint/'splat.ply')[0] > state['cur_num_splats']: raise ValueError('Inconsistent checkpoint')
    return step


class Run:
    def __init__(self, work, cfg):
        self.work, self.cfg = work, cfg
        self.data = work/'dataset'
        self.spirula = executable(cfg, 'spirula')

    def command(self, name, args, validate=lambda: None):
        command = list(map(str, [self.spirula, *args]))
        marker = self.work/'stages'/f'{name}.json'
        key = canonical(command)
        if marker.exists():
            if json.loads(marker.read_text())['command_hash'] != key: raise ValueError(f'{name} arguments changed; use a new workspace.')
            validate(); print(f'Already verified: {name}', flush=True); return
        self.reserve()
        log = self.work/'logs'/f'{name}.log'; log.parent.mkdir(parents=True, exist_ok=True)
        write_json(self.work/'status.json', dict(stage=name, state='running', command=command))
        started = time.time()
        print(f'Running {name}; log: {log}', flush=True)
        with log.open('w', encoding='utf-8') as stream:
            stream.write(subprocess.list2cmdline(command)+'\n'); stream.flush()
            with subprocess.Popen(command, stdout=stream, stderr=subprocess.STDOUT, creationflags=FLAGS) as process:
                try:
                    while process.poll() is None:
                        if shutil.disk_usage(self.work).free < 2*1024**3:
                            process.terminate(); process.wait()
                            raise RuntimeError('Stopped before disk filled. Existing verified checkpoints are preserved.')
                        time.sleep(3)
                    if process.returncode: raise RuntimeError(f'{name} failed ({process.returncode}); see {log}')
                except BaseException:
                    if process.poll() is None: process.terminate(); process.wait()
                    raise
        validate()
        write_json(marker, dict(command_hash=key, command=command, seconds=round(time.time()-started, 2)))

    def reserve(self):
        free = shutil.disk_usage(self.work).free/1024**3
        if free < self.cfg['min_free_gib']: raise RuntimeError(f'Only {free:.1f} GiB free; need {self.cfg["min_free_gib"]} before next stage.')

    def prepare(self, source, meta):
        from masks import mask_capture
        from colmap_model import images, split
        from PIL import Image
        def extraction_ok():
            first,second = [sorted((self.data/'images'/cam).glob('*.jpg')) for cam in ('cam0','cam1')]
            if len(first) < 100 or [p.name for p in first] != [p.name for p in second]: raise ValueError('Incomplete paired extraction or take too short (need 100 pairs).')
            if int(first[0].stem)/meta['fps'] > 2 or meta['duration']-int(first[-1].stem)/meta['fps'] > 2: raise ValueError('Extraction missed the beginning or end.')
            for p in (first[0],first[-1],second[0],second[-1]):
                with Image.open(p) as im:
                    if im.size != (3840,3840): raise ValueError('Unexpected extracted image size')
        self.command('extract', ['sam','extract',source,'--out',self.data/'images','--sync','--adaptive','--adaptive-range',2,'--skip',6,'--keep',5,'--360','off','--scale',1,'--quality',96,'--threads',2,'--start',0,'--end',meta['duration'],'--report',self.work/'frames.csv'], extraction_ok)
        mask_capture(self)
        self.command('sfm', ['sfm','auto',self.data/'images','-o',self.work/'reconstruction','--rig','dual-fisheye=cam0,cam1','--sequence','cam0,cam1','--camera-model','opencv-fisheye','--camera-mode','folder','--focal',1032,'--quality','high','--max-image-size',3000,'--max-features',10000,'--pairs','sequential','--overlap',24,'--masks',self.data/'training_masks','--data-type','video','--threads',4,'--decode-threads',2,'--telemetry',source,'--metric-gps','none'])
        models = list((self.work/'reconstruction/sparse').glob('*/images.bin'))
        if not models: raise ValueError('No reconstructed cameras')
        model = max(models, key=lambda p: struct.unpack('<Q',p.open('rb').read(8))[0]).parent
        names = {n for n,*_ in images(model)}
        extracted = sorted((self.data/'images/cam0').glob('*.jpg'))
        paired = [p for p in extracted if all(f'{cam}/{p.name}' in names for cam in ('cam0','cam1'))]
        times = [0, *[int(p.stem)/meta['fps'] for p in paired], meta['duration']]
        gaps = max(b-a for a,b in zip(times,times[1:]))
        metric = (model/'gauge.txt').is_file() and 'metric 1' in (model/'gauge.txt').read_text()
        write_json(self.work/'registration.json', dict(extracted_pairs=len(extracted), registered_pairs=len(paired), largest_gap_seconds=gaps, metric=metric))
        if len(paired)/len(extracted) < .9 or gaps > 4: raise ValueError('Camera coverage needs inspection. See registration.json; training has not started.')
        if not metric: raise ValueError('No metric reconstruction. Calibrate scale before a life-size VR build; see docs/workflow.md.')
        shutil.copytree(model, self.data/'sparse/0', dirs_exist_ok=True)
        if not (self.work/'split.json').exists(): write_json(self.work/'split.json', split(model,self.data))
        if self.cfg['depth_supervision']:
            self.command('geometry', ['geometry',self.data,'--model','moge2-vitb','--depth','--max-size',896,'--normal-format','jpg','--jpeg-quality',95])

    def train(self, target):
        settings = self.cfg[target]; steps = settings['steps']; folder = self.work/f'train-{target}'
        final = folder/f'step-{steps:09d}.ckpt'
        try:
            if recovery(final) == steps: return final/'splat.ply'
        except (OSError, ValueError, KeyError, tarfile.TarError): pass
        resume = None
        for checkpoint in sorted(folder.glob('step-*.ckpt'), reverse=True):
            try:
                if recovery(checkpoint) < steps: resume = checkpoint; break
            except (OSError, ValueError, KeyError, tarfile.TarError): continue
        start = ['--resume',resume] if resume else ['360-camera','--data',self.data,'--data-format','colmap','--colmap-recon-dir','train-recon','--mask-dir','training_masks','--apply-loss-for-mask',0,'--quality','high','--primitive','3dgs','--cap-max',settings['splat_cap'],'--sh-degree',3,'--train-resolution-divisor',settings['resolution_divisor'],'--cache-images','disk','--preallocate-splat-tensors',0,'--depth-supervision-weight',.04 if self.cfg['depth_supervision'] else 0,'--normal-supervision-weight',.01 if self.cfg['depth_supervision'] else 0,'--floater-suppression','off','--distraction-robustness','mild']
        self.command(f'train-{target}', ['train',*start,'--num-iterations',steps,'--steps-per-save',self.cfg['save_interval'],'--save-full-checkpoint',1,'--save-only-latest-checkpoint',0,'--eval-mode','interval','--eval-interval',50,'--save-eval-images',0,'--eval-lpips',0,'--disable-viewer',1,'--keep-viewer-alive',0,'--output-dir-prefix',self.work,'--output-dir-name',folder.name], lambda: recovery(final))
        return final/'splat.ply'


def plan(cfg, targets):
    return dict(stages=['probe + source hash','paired native lens extraction','lens + person masks','metric rig alignment + coverage check','held-out camera split']+(['depth/normal maps'] if cfg['depth_supervision'] else [])+[f'{t}: train {cfg[t]["steps"]} steps, cap {cfg[t]["splat_cap"]:,}; placement; Unity build' for t in targets], tools={k:str(executable(cfg,k,False) or 'NOT FOUND') for k in cfg['tools']}, checkpoint_interval=cfg['save_interval'], cleanup='No input/checkpoint deletion. Work and builds remain outside this repository.')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--config', help='Private config JSON; defaults are examples/config.json')
    sub = parser.add_subparsers(dest='command', required=True)
    sub.add_parser('doctor'); sub.add_parser('plan')
    p = sub.add_parser('run'); p.add_argument('input'); p.add_argument('--work',required=True); p.add_argument('--target', choices=['pc','quest','both'], default='pc'); p.add_argument('--train-only',action='store_true'); p.add_argument('--install',action='store_true'); p.add_argument('--launch',action='store_true'); p.add_argument('--serial')
    p = sub.add_parser('build'); p.add_argument('--ply',required=True); p.add_argument('--placement',required=True); p.add_argument('--work',required=True); p.add_argument('--target',choices=['pc','quest'],required=True)
    p = sub.add_parser('install'); p.add_argument('apk'); p.add_argument('--serial'); p.add_argument('--launch',action='store_true')
    p = sub.add_parser('launch'); p.add_argument('exe')
    args = parser.parse_args(); cfg = config(args.config)
    from viewer import build, install, launch
    if args.command in ('doctor','plan'):
        result=plan(cfg,['pc','quest']); result.update(machine=platform.node(),python=sys.version.split()[0]); print(json.dumps(result,indent=2)); return
    if args.command=='install': install(expand(args.apk),cfg,args.serial,args.launch); return
    if args.command=='launch': launch(expand(args.exe)); return
    work=expand(args.work)
    if work==ROOT or work.is_relative_to(ROOT): raise ValueError('Use a private workspace OUTSIDE the shareable repository.')
    with locked(work):
        if args.command=='build': build(expand(args.ply),expand(args.placement),work,args.target,cfg); return
        source=capture_input(args.input); meta=probe(source,cfg)
        targets=['pc','quest'] if args.target=='both' else [args.target]
        if args.install and 'quest' not in targets: raise ValueError('--install requires --target quest or both')
        if args.launch and 'pc' not in targets and not args.install: raise ValueError('For a Quest launch, also pass --install')
        identity=dict(source_sha256=digest(source), source_bytes=source.stat().st_size, configuration=cfg)
        guard=work/'identity.json'
        if guard.exists() and json.loads(guard.read_text())!=identity: raise ValueError('Input or settings changed; choose another work folder to preserve the prior run.')
        write_json(guard,identity); write_json(work/'source.json',dict(source=str(source),**meta))
        runner=Run(work,cfg)
        try:
            runner.prepare(source,meta)
            from model import placement
            outputs={}
            for target in targets:
                model=runner.train(target)
                pose=work/f'placement-{target}.json'
                if not pose.exists(): write_json(pose,placement(model,runner.data,cfg))
                outputs[target]=dict(model=str(model),placement=str(pose))
                if not args.train_only:
                    artifact=build(model,pose,work,target,cfg); outputs[target]['viewer']=str(artifact)
            # Finish all GPU work before opening a VR session.
            if not args.train_only:
                if 'quest' in outputs and args.install: install(Path(outputs['quest']['viewer']),cfg,args.serial,args.launch and 'pc' not in outputs)
                if 'pc' in outputs and args.launch: launch(Path(outputs['pc']['viewer']))
            write_json(work/'result.json',outputs); write_json(work/'status.json',dict(state='complete',outputs=outputs))
        except BaseException as error:
            write_json(work/'status.json',dict(state='failed',error=str(error))); raise


if __name__=='__main__': main()
