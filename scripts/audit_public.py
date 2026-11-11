"""Audit the shareable source tree, never a user's capture/work directory."""
import argparse
from pathlib import Path
import re
import sys

ROOT=Path(__file__).resolve().parents[1]
TEXT={'.py','.cs','.hlsl','.compute','.shader','.cginc','.asmdef','.json','.md','.meta','.mat','.uss','.uxml','.svg','.txt','.cmd','.yml','.yaml'}
NAMES={'LICENSE','.gitignore','.gitattributes'}
SKIP={'.git','__pycache__','.pytest_cache'}
PATTERNS=[
    ('absolute user profile', re.compile(r'[A-Za-z]:[\\/]+Users[\\/]+[^\s/\\"<>]+',re.I)),
    ('private key', re.compile(r'-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----')),
    ('GitHub token', re.compile(r'\b(?:gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{30,})')),
    ('IPv4 address', re.compile(r'\b(?:\d{1,3}\.){3}\d{1,3}\b')),
    ('email address', re.compile(r'(?<![\w.])[A-Za-z0-9_.+-]+@[A-Za-z0-9.-]+\.(?:com|net|org|io|edu|gov|au|uk)\b',re.I)),
]


def audit(root=ROOT,deny=()):
    problems=[];files=[]
    for p in root.rglob('*'):
        relative=p.relative_to(root)
        if any(part in SKIP for part in relative.parts): continue
        if p.is_symlink(): problems.append(f'Link not allowed: {relative}');continue
        if not p.is_file():continue
        files.append(p)
        if p.stat().st_size>2*1024**2: problems.append(f'Unexpected large source file: {relative}')
        # The only bundled images are upstream editor icons.
        if p.suffix=='.png' and relative.as_posix().startswith('unity/Packages/org.nesnausk.gaussian-splatting/Editor/Icons/'): continue
        if p.suffix not in TEXT and p.name not in NAMES: problems.append(f'Unexpected file type: {relative}');continue
        if '.local.' in p.name or p.name.startswith('.env') or 'sync-conflict-' in p.name: problems.append(f'Private/conflict file: {relative}')
        try:text=p.read_text(encoding='utf-8-sig')
        except UnicodeError:problems.append(f'Non-text file: {relative}');continue
        for label,pattern in PATTERNS:
            # Upstream license/source notices may contain their authors' contact details.
            if label=='email address' and relative.as_posix().startswith('unity/Packages/'):continue
            # GPU packed-color format notation, not a network address.
            candidate=text.replace('10.10.10.2','packed-format') if label=='IPv4 address' else text
            if pattern.search(candidate):problems.append(f'{label}: {relative}')
        for term in deny:
            if term.lower() in text.lower() or term.lower() in relative.as_posix().lower():problems.append(f'Private review term found in {relative}')
    return files,problems


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--deny',action='append',default=[]);a=parser.parse_args()
    files,problems=audit(deny=a.deny)
    if problems: print('\n'.join(problems));sys.exit(1)
    print(f'PASS: {len(files)} source files; no disallowed content detected. Review changes before publishing.')
