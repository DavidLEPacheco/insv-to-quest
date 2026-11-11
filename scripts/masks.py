"""Bounded person tracking, native lens masks and inspection sheets."""
import os
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw, ImageFilter
from pipeline import executable, write_json


def mask_capture(run):
    data, work = run.data, run.work
    run.command('lens-masks',['sam','mask',data/'images','--out',data/'masks'])
    records=[]; samples=[]
    for cam in ('cam0','cam1'):
        photos=sorted((data/'images'/cam).glob('*.jpg'))
        for first in range(0,len(photos),192):
            start=max(0,first-4); end=min(len(photos),first+192)
            key=f'{cam}-{first:06d}'; frames=work/'mask-input'/key; predictions=work/'semantic'/key
            frames.mkdir(parents=True,exist_ok=True)
            for photo in photos[start:end]:
                link=frames/photo.name
                if not link.exists(): os.link(photo,link)
                elif not os.path.samefile(photo,link): raise ValueError('Different file at mask-batch input')
            if len(list(frames.glob('*.jpg'))) != end-start: raise ValueError('Unexpected mask-batch contents')
            def check():
                if len(list(predictions.glob('frame_*.png'))) != end-start: raise ValueError('Incomplete tracking batch')
            args=['sam','track','--model',executable(run.cfg,'sam_model'),'--detector','gdino-tiny','--frames',frames,'--text',run.cfg['operator_prompt'],'--remove-prompted','--detect-every',2,'--out',predictions]
            if run.cfg['keep_prompt']: args.extend(['--neg-text',run.cfg['keep_prompt']])
            run.command('mask-'+key,args,check)
            for i in range(first,end):
                photo=photos[i]; target=data/'training_masks'/cam/photo.with_suffix('.png').name
                target.parent.mkdir(parents=True,exist_ok=True)
                with Image.open(data/'masks'/cam/target.name) as a,Image.open(predictions/f'frame_{i-start:05d}.png') as b:
                    if a.size != b.size or a.size != (3840,3840): raise ValueError('Native mask sizes differ')
                    # White keeps a pixel. Erode kept area slightly to cover operator outlines.
                    keep=np.minimum(np.asarray(a.convert('L')),np.asarray(b.convert('L').filter(ImageFilter.MinFilter(9))))
                fraction=float((keep<128).mean())
                if fraction>.95: raise ValueError(f'Mask removes almost everything: {cam}/{photo.name}')
                tmp=target.with_suffix('.partial.png'); Image.fromarray(keep).save(tmp); tmp.replace(target)
                records.append(dict(camera=cam,image=photo.name,excluded=fraction))
                if i in set(np.linspace(0,len(photos)-1,8,dtype=int)):
                    with Image.open(photo) as im: rgb=np.asarray(im.convert('RGB').resize((320,320))).copy()
                    cut=np.asarray(Image.fromarray(keep).resize((320,320),Image.Resampling.NEAREST))<128
                    rgb[cut]=(rgb[cut]*.35+np.array([160,22,22])).clip(0,255).astype('uint8')
                    samples.append((f'{cam}/{photo.name}',Image.fromarray(rgb)))
    sheet=Image.new('RGB',(4*320,4*345),'#171c22'); draw=ImageDraw.Draw(sheet)
    for i,(name,im) in enumerate(samples[:16]):
        x,y=i%4*320,i//4*345;sheet.paste(im,(x,y+25));draw.text((x+6,y+5),name,fill='white')
    sheet.save(work/'mask-review.jpg',quality=92)
    write_json(work/'mask-statistics.json',records)
