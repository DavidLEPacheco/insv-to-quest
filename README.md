# INSV to Quest

A local Windows workflow for turning an Insta360 X6 recording into a Gaussian splat and a walkable Unity viewer. Run the full model on a Windows computer over Quest Link/Air Link, or train a separate smaller model and build a standalone Quest APK.

**Validation status:** Python/CI checks pass. A fresh 130-second native recording passed extraction, masking, reconstruction, short training runs, export, checkpoint recovery and both Windows/Quest builds. The Windows player passed a startup check, and both imported models passed fixed-view editor rendering checks. The generic player still needs its own headset test. See [validation notes](docs/validation.md). Treat this as an experimental starter and try a short recording first.

This is a reusable starting point extracted from a working scan-to-VR workflow. It includes the processing runner, a clean Unity template, a modified Gaussian renderer and build/install commands. It contains **no personal scans, faces, footage, room layouts, accounts or multiplayer services**.

**Standalone is a quality/performance compromise.** This baseline trains a 400,000-splat model for Quest; it does not reproduce a multi-million-splat PC house at identical quality or promise 72 FPS. It does not include an advanced streamed LOD/occlusion system. Start with a small room and compare the result in the headset.

## You need

- An Insta360 X6 and a selfie stick for this capture preset. Input must be one original INSV with two synchronized 3840×3840 HEVC lens tracks. LRV files are not used. Other cameras, stitched panoramas and other recording modes need a different calibrated preset.
- Windows 10/11, a modern GPU supported by Spirula, and substantial SSD space. A 16 GB GPU is a practical starting point for large high-quality scans, not a guarantee. Plan for roughly 100–200+ GB of temporary work for a long house capture; images, geometry and retained checkpoints can exceed that. Start small and measure. Training can take hours or a day.
- [Python 3.11–3.13](https://www.python.org/downloads/windows/), [FFmpeg/ffprobe](https://ffmpeg.org/download.html), and [Spirula Studio](https://github.com/harry7557558/spirula-studio/releases). The command adapter was based on **v2026.10.30**. Use its Windows Vulkan build and download the SAM2 tiny / Grounding DINO models through its model manager before the first run. Geometry also uses MoGe2. New versions may change CLI arguments.
- [Unity Hub](https://unity.com/download), Unity **6000.4.6f1**, Windows build support and, for APKs, Android Build Support including SDK, NDK and OpenJDK. Sign in and activate the applicable Unity license yourself. Packages are restored by Unity.
- A Quest 3 for the tested target. For PC VR, install the Meta Quest Link Windows software and connect through Link/Air Link. For standalone installation, enable Developer Mode and accept USB debugging in the headset. An AI or script cannot accept that prompt for you.

The first Python command installs pinned Python packages into a **machine-local environment outside this repository**, keyed by hostname. Subsequent runs reuse it. Unity and Spirula are installed separately; no proprietary binaries or AI model weights are redistributed here.

## First-time setup

1. Copy `examples/config.json` to `config.local.json`. Set `tools.spirula` and `tools.ffprobe` to your installed executables. Set `tools.unity` if Unity is installed elsewhere. Use forward slashes in JSON paths, for example `C:/Tools/Spirula/spirula.exe`. `%LOCALAPPDATA%` is supported. Local config is excluded from Git.
2. Open PowerShell in this folder:

```powershell
python scan.py --config config.local.json doctor
python scan.py --config config.local.json plan
```

`doctor` lists found tools and missing paths. It does not certify GPU compatibility, a Unity license or headset connectivity. Verify those in the applications before a long run.

## Process a recording

Use a private work folder **outside the repository**. The original recording is read in place. A folder input is accepted only when it contains exactly one INSV.

```powershell
python scan.py --config config.local.json run "E:/Captures/Room.insv" --work "E:/ScanWork/Room" --target both
```

Targets are `pc`, `quest` or `both`. After one-time setup, you can also drop a recording onto **PC viewer.cmd** or **Quest viewer.cmd**. Those launchers put work under `%LOCALAPPDATA%/ScanViewer/<machine>/scans/`; choose the explicit command for a larger drive. They build without automatically installing or taking over a headset.

For build, install and PC launch in one run:

```powershell
python scan.py --config config.local.json run "E:/Captures/Room.insv" --work "E:/ScanWork/Room" --target both --install --launch
```

When both targets are selected, PC launch happens after processing/building both targets so training does not compete with your VR session. Only one authorized ADB device may be connected unless you supply `--serial`.

The runner extracts native lens images, masks the lens border and people, solves the calibrated dual-fisheye rig, checks camera coverage, reserves evaluation cameras, optionally generates depth maps, trains the requested models and builds the viewers. It saves status and logs in the work folder. Check `mask-review.jpg` early: people-shaped sculptures, portraits and mannequins can be incorrectly masked. Set `keep_prompt` or adjust the masks in a **new experiment workspace** if needed. Automated checks catch gross failures, not every visual defect.

Floor placement initially uses a recorded camera position and your configured `capture_height_m`. Metric scale comes from reconstruction; floor height is an estimate. Check it against a known dimension. You can edit the generated placement JSON and build a separate comparison with the command below. The viewer has snap turning and bounded joystick motion, no gravity and no automated camera movement. Boundaries are a rectangular capture-path envelope, not collision geometry for walls or furniture.

## Open or install later

```powershell
python scan.py launch "E:/ScanWork/Room/builds/pc/ScanViewer.exe"
python scan.py --config config.local.json install "E:/ScanWork/Room/builds/quest/ScanViewer.apk" --launch
```

The PC viewer retries OpenXR initialization while waiting for Link. Keep its small window visible. In VR: **left stick moves, right stick snap-turns**. Your head controls the camera. Physical play-area safety remains the headset's responsibility.

The standalone app is **Scan Viewer** under Unknown Sources. It embeds its splat and runs without a PC after installation. This starter uses one package ID, so installing another scan replaces the earlier Scan Viewer app. Copy the whole PC build folder when sharing PC VR; copy the APK for standalone. These built artifacts contain your scan even though the repository does not.

Wireless ADB works after you enable/pair wireless debugging with Meta's tools or Android's supported pairing workflow. Establish the connection first with your own ADB tools, then use `--serial IP:PORT`. The script does not guess network addresses, scan the LAN or bypass debugging authorization. Air Link streams rendered PC VR; wireless ADB only transfers/controls an app.

## Resume, inspect or use an existing model

Run the same processing command with the same input, config and work folder. Completed stages are checked; training resumes from the newest verified full checkpoint. Inputs/config changes require a different workspace. No source footage or checkpoint is automatically deleted. `--train-only` stops before Unity builds.

```powershell
python scan.py --config config.local.json build --ply "E:/ScanWork/Room/train-pc/step-000030000.ckpt/splat.ply" --placement "E:/ScanWork/Room/placement-pc.json" --work "E:/ScanWork/Room-preview2" --target pc
```

Keep the original recording and a verified full checkpoint if you may improve the reconstruction later. A PLY can be viewed and reused; it is not the optimizer state needed to resume training exactly. See [workflow and troubleshooting](docs/workflow.md) and [validation status](docs/validation.md).

## Ask an AI to help

Give it this repository and [PROMPT.md](PROMPT.md). The AI helps set up tools, interpret logs, check masks and fix environment-specific problems. Daily processing uses deterministic scripts; it does not require an AI model call for each video frame or training step.

## Credits and privacy

Spirula Studio performs reconstruction/training. The renderer derives from [Aras Pranckevičius' UnityGaussianSplatting](https://github.com/aras-p/UnityGaussianSplatting), with stereo, sorting, depth and visibility adaptations. Original license notices remain in the package. See [THIRD_PARTY.md](THIRD_PARTY.md).

This repository starts with fresh history. Private work, local configuration and generated players belong outside it. Before sharing changes, run `python scripts/audit_public.py`. The audit is a guard, not a substitute for reviewing what you publish. No uploads of footage or splats are implemented; initial dependency/model downloads require internet.
