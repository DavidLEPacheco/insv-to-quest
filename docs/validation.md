# Validation status

Initial source release, 11 November 2026. This is a **starter workflow**, not a claim of turnkey compatibility with every camera mode, GPU, editor version or room.

| Check | Result |
| --- | --- |
| Fresh machine-local Python bootstrap, Python 3.13.2 | Passed with the pinned NumPy 2.2.6, SciPy 1.15.3 and Pillow 11.2.1 |
| Eleven automated pipeline tests | Passed: partial PLY, incomplete checkpoint, ambiguous input/device selection, configuration typo, failed-stage receipts, changed commands, output revalidation, public payload audit, coordinate/floor conversion and pixel-identical faster mask erosion |
| Native INSV probing against a real supported recording | Passed: two synchronized 3840×3840 HEVC tracks; no footage included in the repository |
| Clean Windows Unity build | Passed with Unity 6000.4.6f1, D3D12 and OpenXR configured |
| Offscreen rendering of the synthetic room | Passed: 198,032 visible pixels in a 640×480 image, visually inspected |
| Clean Android Unity build | Passed: ARM64, Vulkan, OpenXR, self-contained APK |
| APK integrity and identity | Passed CRC, signature, expected package ID, ARM64 libraries, OpenXR loader and VR headtracking manifest feature; synthetic APK approximately 54 MiB |
| Public source audit | Passed for disallowed payloads, personal review terms, profile paths, credentials and network addresses; original upstream notices retained |
| Fresh recording through processing and training | Passed on one complete 130.497-second native recording: 420 synchronized pairs, 840/840 registered images, metric reconstruction, all depth maps, separate 1,000-step PC/Quest runs, full checkpoints and valid PLY exports |
| Real checkpoint recovery | Passed: a private copy resumed from step 500 to 510; original checkpoint hashes were unchanged |
| Generic placement for the real recording | Generated for both targets; floor height and spawn still need visual/headset verification |
| Packaging the newly trained real models | **Pending disk space.** The build preflight correctly refused to start with less than its required 6 GiB free. The earlier synthetic PC/Quest builds passed; they do not complete this real-model test |
| Generic player headset wear test | **Not yet performed.** The originating renderer was used in VR, but the new minimal rig needs its own on-device comfort, stereo and performance check |
| Large-house standalone fidelity / 72 FPS | **Not claimed.** A smaller separately trained model cannot guarantee the PC master's detail |

Builds were tested without replacing an existing headset session. No automatic headset camera movement was used. The synthetic room generator is in `tests/make_sample.py`; test binaries and logs are not part of the source repository.

During setup, a long Windows project path caused Unity package import errors. Generated Unity projects now use short machine-local paths. Switching the input-system backend initially caused editor/player serialization disagreement; configuration and build now run in separate editor invocations. A low-disk Android attempt failed; it was rebuilt successfully after reclaiming generated caches. The launcher now checks and monitors build-disk space.

CI runs Python tests and the public source audit. It does not train a scan, activate Unity or operate a headset.

## Real-recording check

The complete recording was processed from its original INSV in a separate workspace, without reusing an earlier reconstruction or trained model. Extraction took about 150 seconds. Mask overlays were visually inspected: the operator and lens rim were excluded while the room remained. All 420 pairs registered; the largest timeline gap was 0.534 seconds. Alignment took about 12 minutes, and 840 depth maps took about 11 minutes.

This was a bounded pipeline check on an NVIDIA RTX 4080 SUPER, not a maximum-quality reconstruction. Each target trained for 1,000 steps, with 500-step full checkpoints. The PC cap was 150,000 splats and the Quest cap 100,000; final exports contained 149,994 and 99,996 splats respectively. The optimizer loops took about 68 and 57 seconds, excluding setup and final evaluation. These timings do not estimate a default 30,000-step run or the performance of a large house on a headset.

The run exposed slow mask combination: replacing Pillow's minimum filter with SciPy's separable minimum filter reduced a native-frame check from about 1.1 s to 0.12 s with identical pixels. Border and grayscale equivalence are covered by the additional regression test.

Recovery was exercised with a copy of the real 500-step checkpoint **and its run configuration**, then continued to step 510. Copying only the checkpoint directory initially failed because Spirula also needs the parent `config.json`; keeping the whole training directory and referenced dataset avoids that omission. The normal runner resumes in the original workspace.

Processing is complete. Real-model Unity packaging, visual placement verification and headset checks remain pending. No private recording, model, mask preview or personal scene configuration is published here.
