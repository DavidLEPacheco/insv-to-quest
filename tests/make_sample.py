"""Generate a tiny synthetic room. No photographs or personal data."""
import json
from pathlib import Path
import sys
import numpy as np


def generate(directory):
    directory=Path(directory);directory.mkdir(parents=True,exist_ok=True)
    fields=['x','y','z','nx','ny','nz',*[f'f_dc_{i}' for i in range(3)],*[f'f_rest_{i}' for i in range(45)],'opacity',*[f'scale_{i}' for i in range(3)],*[f'rot_{i}' for i in range(4)]]
    rows=[]
    def point(pos,colour):
        row=np.zeros(len(fields),dtype='<f4');row[:3]=pos
        for i,c in enumerate(colour):row[fields.index(f'f_dc_{i}')]=(c-.5)/.28209479177387814
        row[fields.index('opacity')]=3
        for i in range(3):row[fields.index(f'scale_{i}')]=np.log(.045)
        row[fields.index('rot_0')]=1;rows.append(row)
    for x in np.linspace(-2,2,65):
        for z in np.linspace(-2,4,85):point((x,0,z),(.4,.5,.55))
        for y in np.linspace(0,2.5,40):point((x,y,4),(.8,.35,.15))
    for z in np.linspace(-2,4,85):
        for y in np.linspace(0,2.5,40):point((-2,y,z),(.25,.6,.35))
    header='ply\nformat binary_little_endian 1.0\nelement vertex '+str(len(rows))+'\n'+''.join('property float '+f+'\n' for f in fields)+'end_header\n'
    model=directory/'synthetic.ply'
    with model.open('wb') as f:f.write(header.encode());f.write(np.asarray(rows,dtype='<f4').tobytes())
    (directory/'placement.json').write_text(json.dumps(dict(position=[0,0,0],rotation=[0,0,0,1],scale=[1,1,1],startPosition=[0,0,0],startForward=[0,0,1],walkBounds=[-1.8,-1.8,1.8,3.8],splatCount=len(rows)),indent=2))
    return model

if __name__=='__main__':print(generate(sys.argv[1]))
