"""PLY integrity and generic metric placement; no room-specific anchors."""
import json
from pathlib import Path
import struct
import numpy as np
from scipy.spatial.transform import Rotation
from colmap_model import images


def ply_info(path):
    fields=[];count=0
    with Path(path).open('rb') as f:
        if f.readline().strip()!=b'ply' or f.readline().strip()!=b'format binary_little_endian 1.0': raise ValueError('Expected binary little-endian Gaussian PLY')
        for _ in range(256):
            line=f.readline()
            if line.startswith(b'element vertex '): count=int(line.split()[-1])
            elif line.startswith(b'element '): raise ValueError('Unexpected PLY element')
            elif line.startswith(b'property '):
                parts=line.split()
                if len(parts)!=3 or parts[1]!=b'float': raise ValueError('Expected float Gaussian properties')
                fields.append(parts[2].decode())
            elif line.strip()==b'end_header': break
        else: raise ValueError('Incomplete PLY header')
        offset=f.tell()
    required={'x','y','z','opacity','f_dc_0','f_dc_1','f_dc_2','scale_0','scale_1','scale_2','rot_0','rot_1','rot_2','rot_3'}
    if count<=0 or not required.issubset(fields) or Path(path).stat().st_size!=offset+count*len(fields)*4: raise ValueError('Incomplete or unsupported Gaussian PLY')
    return count,fields,offset


def placement(model,data,cfg):
    count,fields,offset=ply_info(model)
    a=np.asarray(json.loads((model.parent.parent/'scene_transform.json').read_text())['train_from_world']['matrix_4x4'])
    unit=np.cbrt(np.linalg.det(a[:3,:3])); orientation=a[:3,:3]/unit
    if unit<=0 or not np.allclose(orientation@orientation.T,np.eye(3),atol=1e-4): raise ValueError('Expected a positive similarity transform')
    poses=[]
    for name,header,*_ in images(data/'sparse/0'):
        if not name.startswith('cam0/'): continue
        _,qw,qx,qy,qz,tx,ty,tz,_=struct.unpack('<i7di',header)
        r=Rotation.from_quat([qx,qy,qz,qw]).as_matrix()
        poses.append((name,-r.T@np.array([tx,ty,tz]),r[2]))
    poses.sort(key=lambda p:p[0]); name,center,forward=poses[min(10,len(poses)-1)]
    # The rig's metric world frame is Z-up. Floor height is estimated, not measured.
    floor=center.copy();floor[2]-=cfg['capture_height_m']
    axes=np.array([-1.,1.,1.]);up=axes*(orientation@np.array([0,0,1]))
    target=np.array([0.,1.,0.]);q=np.r_[np.cross(up,target),1+up@target]
    if np.linalg.norm(q)<1e-8:q=np.array([1.,0.,0.,0.])
    q/=np.linalg.norm(q);r=Rotation.from_quat(q);basis=r.as_matrix()@np.diag(axes/unit)
    position=-basis@(a[:3,:3]@floor+a[:3,3])
    heading=r.apply(axes*(orientation@forward));heading[1]=0
    if np.linalg.norm(heading)<1e-6:heading=np.array([0.,0.,1.])
    heading/=np.linalg.norm(heading)
    centers=np.array([p[1] for p in poses]);mapped=(centers@a[:3,:3].T+a[:3,3])@basis.T+position
    low=mapped[:,[0,2]].min(axis=0)-.4;high=mapped[:,[0,2]].max(axis=0)+.4
    return dict(position=position.tolist(),rotation=q.tolist(),scale=(axes/unit).tolist(),startPosition=[0,0,0],startForward=heading.tolist(),walkBounds=[*low,*high],splatCount=count,anchorImage=name,floorEstimate=True,captureHeightM=cfg['capture_height_m'],note='Floor uses capture height at the anchor frame. Adjust position/scale after checking a known dimension. Bounds restrict joystick motion; they are not reconstructed walls.')
