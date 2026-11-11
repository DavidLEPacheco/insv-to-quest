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
| Fresh full capture through this generalized runner | **Not yet run end to end.** Commands derive from the working private workflow, and native probing is checked; generalized mask quality, automatic placement and new-capture recovery need a practice scan |
| Generic player headset wear test | **Not yet performed.** The originating renderer was used in VR, but the new minimal rig needs its own on-device comfort, stereo and performance check |
| Large-house standalone fidelity / 72 FPS | **Not claimed.** A smaller separately trained model cannot guarantee the PC master's detail |

Builds were tested without replacing an existing headset session. No automatic headset camera movement was used. The synthetic room generator is in `tests/make_sample.py`; test binaries and logs are not part of the source repository.

During setup, a long Windows project path caused Unity package import errors. Generated Unity projects now use short machine-local paths. Switching the input-system backend initially caused editor/player serialization disagreement; configuration and build now run in separate editor invocations. A low-disk Android attempt failed; it was rebuilt successfully after reclaiming generated caches. The launcher now checks and monitors build-disk space.

CI runs Python tests and the public source audit. It does not train a scan, activate Unity or operate a headset.

The real-recording run is in progress. It has already verified extraction of 420 paired native lens images. It exposed slow mask combination: replacing Pillow's minimum filter with SciPy's separable minimum filter reduced a native-frame check from about 1.1 s to 0.12 s with identical pixels. Border and grayscale equivalence are covered by the additional regression test. These are interim results, not completion of the full recording test.
