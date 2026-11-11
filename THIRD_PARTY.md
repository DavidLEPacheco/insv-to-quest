# Dependencies and attribution

- `unity/Packages/org.nesnausk.gaussian-splatting`: derived from Aras Pranckevičius' UnityGaussianSplatting 1.1.1, MIT. The original `LICENSE.md` and source notices are retained. GPU sorting files carry their own original notices. Local changes cover batch import, XR stereo composition, per-view sorting, visibility selection, opaque-depth interactions and optional generic dynamic-splat/lighting helpers. The clean template enables one static environment; it does not include the originating application's scenes or avatars.
- Spirula Studio: [official repository](https://github.com/harry7557558/spirula-studio), listed by its project as GPL-3.0. Installed separately and invoked as an external program. No Spirula binaries, code or model weights are bundled here. Model weights may have their own terms.
- Unity editor, runtime and packages: acquired through Unity Hub/Package Manager under their respective terms. Not included as binaries. The template pins package versions in its manifest.
- FFmpeg/ffprobe: external tool, not redistributed. Obtain a build appropriate for your platform and use.
- Python packages: NumPy, SciPy and Pillow are installed from PyPI using the pinned requirements. They retain their respective licenses.

The root MIT license applies to this repository's original scripts, template and documentation. It does not replace any dependency's license or grant rights to footage you did not capture or have permission to use.
