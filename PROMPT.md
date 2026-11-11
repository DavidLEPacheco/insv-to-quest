# Give this to your coding assistant

I want to process my own Insta360 X6 recording into a locally saved Gaussian splat and view it in VR using this repository. Read README.md, docs/workflow.md and docs/validation.md first.

My setup:
- Windows machine and GPU/VRAM: [please inspect if unknown]
- Camera/input recording: [path]
- Private working drive and free space: [path]
- Headset: [model]
- Target: [PC VR over Air Link / standalone Quest / both]

Check the installed tools, versions and license state before starting. Set up config.local.json without putting personal paths or credentials into tracked files. Use a machine-local Python environment and a separate private scan workspace. Start with a short room test. Explain any missing hardware or manual headset authorization step.

Use the scripts; do not rebuild the workflow from scratch. Inspect mask-review.jpg and registration.json before trusting a long run. Native dual-fisheye images require the calibrated fisheye rig; do not treat them or stitched panoramas as ordinary pinhole photos. If footage does not match the supported preset, stop and design an explicit compatible input adapter.

Keep originals and checkpoints. On failure, inspect logs and resume verified work. Report actual training steps separately from splat count. Check placement, both-eye rendering, visual defects and performance before describing a viewer as finished. A successful APK build is not a measured headset performance result. Do not move a user's camera automatically or interrupt another viewer without authorization.

Use the PC master for full quality and the separate mobile model for standalone. Do not claim identical fidelity. Help me compare the same viewpoints. Keep all personal content and generated builds out of Git; run the public audit before publishing source changes.
