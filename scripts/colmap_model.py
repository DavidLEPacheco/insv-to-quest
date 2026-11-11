"""Small COLMAP binary utilities for synchronized validation splits and review cameras."""
import json,shutil,struct
from pathlib import Path
import numpy as np

def images(folder):
    result=[]
    with (Path(folder)/'images.bin').open('rb') as f:
        n,=struct.unpack('<Q',f.read(8))
        for _ in range(n):
            header=f.read(64);name=bytearray()
            while True:
                b=f.read(1)
                if not b:raise ValueError('Truncated image record')
                if b==b'\0':break
                name+=b
            length=f.read(8);points=f.read(struct.unpack('<Q',length)[0]*24)
            result.append((name.decode(),header,length,points))
    return result

def partition(src,dst,frames):
    src,dst=Path(src),Path(dst);dst.mkdir(parents=True,exist_ok=True);ids=set()
    with (dst/'images.bin').open('wb') as f:
        f.write(struct.pack('<Q',len(frames)))
        for name,header,length,points in frames:
            ids.add(struct.unpack_from('<i',header)[0]);f.write(header+name.encode()+b'\0'+length+points)
    with (src/'points3D.bin').open('rb') as inp,(dst/'points3D.bin').open('wb') as out:
        total,=struct.unpack('<Q',inp.read(8));out.write(bytes(8));count=0
        for _ in range(total):
            header=inp.read(43);n,=struct.unpack('<Q',inp.read(8))
            track=[(i,j) for i,j in struct.iter_unpack('<ii',inp.read(n*8)) if i in ids]
            if track:
                out.write(header+struct.pack('<Q',len(track)))
                for i,j in track:out.write(struct.pack('<ii',i,j))
                count+=1
        out.seek(0);out.write(struct.pack('<Q',count))
    for name in ('cameras.bin','gauge.txt','rigs.txt'):
        if (src/name).is_file():shutil.copyfile(src/name,dst/name)

def split(src,dataset,holdout_pairs=16):
    frames=images(src);names={n for n,*_ in frames}
    paired=sorted({Path(n).name for n in names if all(f'{cam}/{Path(n).name}' in names for cam in ('cam0','cam1'))})
    if len(paired)<100:raise ValueError('Too few complete rig positions for this capture')
    if not 1<=holdout_pairs<=len(paired)-30:raise ValueError('Invalid number of withheld rig positions')
    held=set(paired[i] for i in np.linspace(15,len(paired)-16,holdout_pairs,dtype=int))
    train=[f for f in frames if Path(f[0]).name not in held]
    review=[f for f in frames if Path(f[0]).name in held]
    partition(src,dataset/'train-recon',train);partition(src,dataset/'review-recon',review)
    return {'held_out_pairs':sorted(held),'training_lenses':len(train),'review_lenses':len(review),'registered_lenses':len(frames)}

def align_review(src,dst,transform):
    from scipy.spatial.transform import Rotation
    A=np.asarray(json.loads(Path(transform).read_text())['train_from_world']['matrix_4x4'])
    scale=np.cbrt(np.linalg.det(A[:3,:3]));rot=A[:3,:3]/scale
    if scale<=0 or not np.allclose(rot@rot.T,np.eye(3),atol=1e-4):raise ValueError('Unexpected training transform')
    frames=[]
    for name,header,length,points in images(src):
        i,qw,qx,qy,qz,tx,ty,tz,cam=struct.unpack('<i7di',header)
        R=Rotation.from_quat([qx,qy,qz,qw]).as_matrix();center=-R.T@np.array([tx,ty,tz])
        newR=R@rot.T;newC=A[:3,:3]@center+A[:3,3];x,y,z,p=Rotation.from_matrix(newR).as_quat()
        frames.append((name,struct.pack('<i7di',i,p,x,y,z,*(-newR@newC),cam),length,points))
    partition(src,dst,frames)
    with (Path(dst)/'points3D.bin').open('r+b') as f:
        n,=struct.unpack('<Q',f.read(8))
        for _ in range(n):
            start=f.tell();header=f.read(43);xyz=np.array(struct.unpack_from('<3d',header,8))
            f.seek(start+8);f.write(struct.pack('<3d',*(A[:3,:3]@xyz+A[:3,3])));f.seek(start+43)
            track,=struct.unpack('<Q',f.read(8));f.seek(track*8,1)
