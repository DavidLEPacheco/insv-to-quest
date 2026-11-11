# Workflow details

## Capture

Make a short practice recording before a whole building. Keep lighting and objects steady. Move slowly enough to avoid motion blur. Capture overlapping translated viewpoints, including above tables and low enough to see the floor. Work through high, middle and low views of one area, then move into the next with overlap. A 360 camera sees behind you, but occluded surfaces still require another camera position. Avoid very close passes, especially near lens seams; 8K is spread around the sphere and does not guarantee close-up detail everywhere.

This adapter uses native lens images with Spirula's calibrated fisheye rig. It does not stitch to an equirectangular image, nor split a panorama into perspective crops. Those are other valid workflows when calibrated correctly; they should not be mixed blindly with this preset.

## Outputs

| Private work item | Purpose |
| --- | --- |
| `source.json`, `identity.json` | Input metadata, hash and immutable run configuration |
| `logs/`, `stages/`, `status.json` | Commands, completion receipts and current/failure state |
| `dataset/images/` | Synchronized lens images |
| `dataset/training_masks/`, `mask-review.jpg` | Lens/operator exclusion and sample overlays |
| `registration.json` | Paired camera coverage, temporal gaps, metric status |
| `dataset/train-recon/`, `review-recon/` | Training cameras and held-out camera pairs |
| `train-pc/`, `train-quest/` | Independent training runs and full checkpoints |
| `placement-pc.json`, `placement-quest.json` | Editable import transform, spawn and joystick bounds |
| `unity-pc.json`, `unity-quest.json` | Paths to machine-local generated Unity projects containing personal assets |
| `builds/` | Shareable players that contain the selected personal scan |

For recovery, keep the training directory's `config.json` and `scene_transform.json` alongside its checkpoint directories, plus the referenced dataset. A checkpoint folder copied by itself is not a complete portable resume package: Spirula needs the parent run configuration and the training images/masks/depth data it names. The runner resumes within the existing work folder; moving an entire run to another machine may require path updates.

The held-out cameras are preserved for external comparison. This starter does not yet generate a quantitative held-out PSNR/SSIM report or automatically pick the best checkpoint. Inspect the final PLY in Spirula and compare views in VR.

Unity projects are generated under a short `%LOCALAPPDATA%/SV/<workspace-hash>/` path to avoid Windows path-length problems. They are machine-local and contain imported scan data. The work folder receipts locate them. Input-system configuration runs once in an editor invocation before the build invocation, so editor and player compilation agree.

## Recovery and storage

The source hash/config guard prevents accidentally resuming a different recording in the same workspace. A stage receipt is written only after successful completion and its validation. Training resumes only from a readable full state archive whose step and PLY agree. Partial checkpoints are ignored. Keep originals and at least one verified full state archive on another drive if loss of this computer would matter.

All checkpoints are retained by default. The script stops new stages below the configured free-space threshold and terminates a running stage below 2 GiB. This reduces disk-full failures but cannot recover unsaved work. It is not a backup system. A long capture may require much more than 100 GB. Choose a large private working drive; the drop launchers use the system drive and are better for short tests. No automatic cache cleanup is provided.

## Mask and reconstruction failures

Inspect red exclusions on the preview sheet. The generic mask is intentionally different from a hand-tuned scene mask: it removes all detected people in both lenses. It can mistake a mannequin or portrait for a person. `keep_prompt` can supply a concept to preserve. Test a new configuration in a new workspace. It cannot reconstruct an object absent or blurred in the source.

The coverage gate requires at least 90% of paired frames to register, no temporal hole longer than four seconds, and metric scale. High registration does not prove all objects are sharp or the room geometry is correct. Mirrors, glass, movement, exposure changes and weak overlap still cause problems. A non-metric result stops before training: solve the scale using a known dimension and adapt the placement/calibration stage instead of guessing a life-size room.

## Placement and viewer limits

The default floor estimate subtracts `capture_height_m` from a registered camera position near the beginning. For a raised-stick start, set that height appropriately. The stored transform undoes training normalization, converts Z-up to Unity Y-up, and preserves the necessary handedness reflection on the splat object so shading follows the same transform.

Placement JSON uses `position` (three floats), `rotation` (x/y/z/w quaternion), `scale` (three signed floats), `startPosition`, `startForward`, `walkBounds` (minX/minZ/maxX/maxZ), and `splatCount`. Adjust a private copy and rebuild into a new workspace. Rectangular movement bounds prevent an endless fall, but permit walking through scanned furniture; no reconstructed collision mesh or teleport navigation is included.

The template has no networking, avatar identities, mirrors, object pickup or day/night switching. Those application features can be built separately after proving the basic viewer. Black is the default background. The modified renderer retains generic optional helpers, but this scene uses one static scan.

## Headset diagnosis

For PC VR, first verify the headset works in the Meta Link environment. A desktop-only preview is not proof of XR output. Keep the player window visible; a hidden/minimized Unity window can stop drawing. Inspect the player log for OpenXR errors. On standalone, confirm the installed package can launch with the PC disconnected.

If everything looks pixelated, check eye/render resolution; if selected surfaces are blurry, compare the same position in the master PLY. Raising resolution can cause judder. The mobile model has a much smaller splat budget and needs its own quality/performance tests. Lowering the budget further may improve timing while making the model worse. Do not confuse compositor refresh, generated frames and actual application rendering FPS.

## Synthetic build test

No private capture is needed to check the template:

```powershell
python tests/make_sample.py "E:/ScanWork/Synthetic/input"
python scripts/pipeline.py build --ply "E:/ScanWork/Synthetic/input/synthetic.ply" --placement "E:/ScanWork/Synthetic/input/placement.json" --work "E:/ScanWork/Synthetic/pc" --target pc
```

Use the environment created by `scan.py`, or install `requirements.txt` in your own environment. Replace `pc` with `quest` to test Android packaging. A synthetic room verifies plumbing; it cannot predict a real house's fidelity or frame time.
