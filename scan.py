"""Bootstrap a machine-local Python environment and dispatch the pipeline."""
import hashlib
import os
from pathlib import Path
import platform
import subprocess
import sys
import venv

ROOT=Path(__file__).resolve().parent


def main():
    if not (3,11)<=sys.version_info[:2]<=(3,13): raise SystemExit('Use Python 3.11, 3.12 or 3.13.')
    local=Path(os.environ.get('LOCALAPPDATA',Path.home()/'.local/share'))
    key=hashlib.sha256((ROOT/'requirements.txt').read_bytes()).hexdigest()[:12]
    env=local/'ScanViewer'/platform.node()/f'python-{sys.version_info.major}{sys.version_info.minor}-{key}'
    interpreter=env/('Scripts/python.exe' if os.name=='nt' else 'bin/python')
    if not interpreter.exists(): venv.EnvBuilder(with_pip=True).create(env)
    if not (env/'ready').exists():
        subprocess.run([str(interpreter),'-m','pip','install','-r',str(ROOT/'requirements.txt')],check=True)
        (env/'ready').write_text(key)
    args=sys.argv[1:]
    if args and args[0]=='drop':
        import argparse
        parser=argparse.ArgumentParser();parser.add_argument('drop');parser.add_argument('input');parser.add_argument('--target',choices=['pc','quest','both'],default='pc')
        a=parser.parse_args(args)
        source=Path(a.input).resolve()
        identity=hashlib.sha256(str(source).encode()).hexdigest()[:12]
        work=local/'ScanViewer'/platform.node()/'scans'/identity
        private=ROOT/'config.local.json'
        if not private.exists():raise SystemExit('First create config.local.json from examples/config.json and set your tool paths. See README.md.')
        args=['--config',str(private),'run',str(source),'--work',str(work),'--target',a.target]
    raise SystemExit(subprocess.call([str(interpreter),str(ROOT/'scripts/pipeline.py'),*args]))


if __name__=='__main__':main()
