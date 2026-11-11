import io
import json
from pathlib import Path
import struct
import sys
import tarfile
import tempfile
import unittest
from unittest.mock import patch
import numpy as np

sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'scripts'))
from pipeline import Run, canonical, capture_input, config, locked, recovery
from viewer import select_device
from model import ply_info,placement
from make_sample import generate
from audit_public import audit


class PipelineTests(unittest.TestCase):
    def setUp(self):self.tmp=tempfile.TemporaryDirectory();self.root=Path(self.tmp.name)
    def tearDown(self):self.tmp.cleanup()

    def test_ply_rejects_partial_payload(self):
        model=generate(self.root)
        self.assertGreater(ply_info(model)[0],1000)
        model.write_bytes(model.read_bytes()[:-4])
        with self.assertRaises(ValueError):ply_info(model)

    def test_input_ambiguity_requires_filename(self):
        (self.root/'a.insv').write_bytes(b'a');(self.root/'b.insv').write_bytes(b'b')
        with self.assertRaises(ValueError):capture_input(str(self.root))
        self.assertTrue(capture_input(str(self.root/'a.insv')).samefile(self.root/'a.insv'))

    def test_unauthorized_headset_is_not_selected(self):
        with self.assertRaises(RuntimeError):select_device('List of devices attached\none\tunauthorized\n')

    def test_multiple_devices_require_explicit_selection(self):
        text='List of devices attached\none\tdevice\ntwo\tdevice\n'
        with self.assertRaises(RuntimeError):select_device(text)
        self.assertEqual(select_device(text,'two'),'two')

    def test_configuration_typo_does_not_silently_use_defaults(self):
        p=self.root/'config.json';p.write_text('{"stepz": 10}')
        with self.assertRaises(ValueError):config(p)

    def test_stage_failure_never_writes_completion(self):
        run=Run.__new__(Run);run.work=self.root;run.cfg={'min_free_gib':2};run.spirula=Path(sys.executable)
        with patch.object(run,'reserve'):
            with self.assertRaises(RuntimeError):run.command('bad',['-c','raise SystemExit(9)'])
        self.assertFalse((self.root/'stages/bad.json').exists())

    def test_stage_signature_and_output_revalidation(self):
        run=Run.__new__(Run);run.work=self.root;run.cfg={'min_free_gib':2};run.spirula=Path(sys.executable)
        with patch.object(run,'reserve'):
            run.command('good',['-c','pass'])
            with self.assertRaises(ValueError):run.command('good',['-c','print(1)'])
            with self.assertRaises(FileNotFoundError):run.command('good',['-c','pass'],lambda:(self.root/'missing').read_bytes())

    def test_checkpoint_requires_full_state(self):
        ck=self.root/'step-000005000.ckpt';ck.mkdir();model=generate(ck);model.rename(ck/'splat.ply')
        with tarfile.open(ck/'state.tar','w') as tar:
            blob=json.dumps({'step':5000,'full_resume':0,'cur_num_splats':999999}).encode()
            info=tarfile.TarInfo('state.json');info.size=len(blob);tar.addfile(info,io.BytesIO(blob))
        with self.assertRaises(ValueError):recovery(ck)

    def test_public_audit_catches_private_payload(self):
        (self.root/'capture.insv').write_bytes(b'private video')
        self.assertTrue(audit(self.root)[1])

    def test_placement_converts_z_up_without_wrong_floor_offset(self):
        run=self.root/'train';ck=run/'step-000005000.ckpt';model=generate(ck)
        (run/'scene_transform.json').write_text(json.dumps({'train_from_world':{'matrix_4x4':np.eye(4).tolist()}}))
        data=self.root/'data';sparse=data/'sparse/0';sparse.mkdir(parents=True)
        with (sparse/'images.bin').open('wb') as f:
            f.write(struct.pack('<Q',1));f.write(struct.pack('<i7di',1,1,0,0,0,0,0,-1.6,1));f.write(b'cam0/00001.jpg\0');f.write(struct.pack('<Q',0))
        p=placement(model,data,{'capture_height_m':1.6})
        from scipy.spatial.transform import Rotation
        basis=Rotation.from_quat(p['rotation']).as_matrix()@np.diag(p['scale'])
        floor=basis@np.array([0,0,0])+p['position'];eye=basis@np.array([0,0,1.6])+p['position']
        self.assertAlmostEqual(floor[1],0);self.assertAlmostEqual(eye[1],1.6)
        self.assertLess(np.linalg.det(basis),0)


if __name__=='__main__':unittest.main()
