![Baballonia Promo](BaballoniaPromo.png)

# Baballonia

**Baballonia** is a cross-platform, hardware-agnostic VR eye and face tracking application.

## Installation

### Windows

Head to the releases tab and [download the latest installer](https://github.com/Project-Babble/Baballonia/releases/latest).

You may be prompted to download the .NET runtime for desktop apps, install it if need be.

### Linux

Head to the releases tab and [download the latest tarball](https://github.com/Project-Babble/Baballonia/releases/latest).

You may be prompted to download the .NET runtime for desktop apps, install it if need be.

### MacOS

Baballonia currently does not have an installer for MacOS. You will need to follow our build instructions and run it from source.

## Platform Compatibility

### VRChat

#### VRCFaceTracking

To use Baballonia with VRChat, you will need to use VRCFaceTracking with the `VRCFT-Babble` module.

1. Download and install the latest version of VRCFaceTracking from [Steam](https://store.steampowered.com/app/3329480/VRCFaceTracking/).
1. Install the `VRCFT-Babble` module within VRCFaceTracking.
1. Use Baballonia to set the module mode (eyes, face or both). Restart VRCFaceTracking to see your changes.

More information can be found on the [VRCFT Docs](https://docs.vrcft.io/docs/vrcft-software/vrcft\#module-registry)

#### VRC Native Eyelook

Alternatively, Baballonia also supports [VRC Native Eyelook](https://docs.vrchat.com/docs/osc-eye-tracking).

While this doesn't support lower face tracking, it supports (almost) all VRChat Avatars.

### Resonite / ChilloutVR

Existing mods *should* be compatible with Baballonia's lower face tracking.

## Supported Hardware

Baballonia supports many kinds of hardware for eye and face tracking:

| Device                            | Eyes | Face | Notes                                                       |
|-----------------------------------| ----- | ----- |-------------------------------------------------------------|
| Official Babble Face Tracker      | :x: | ✅ |                                                             |
| DIY and 3rd party Babble Trackers | :x: | ✅ |                                                             |
| Vive Facial Tracker               | :x: | ✅ | Linux Only, WIP                                             |
| DIY EyetrackVR                    | ✅ | :x: |                                                             |
| Bigscreen Beyond 2E               | ✅ | :x: |                                                             |
| Vive Pro Eye                      | ✅ | :x: | Requires [Revision](https://github.com/Blue-Doggo/ReVision) |
| Varjo Aero                        | ✅ | :x: | Requires the Varjo Streamer                                 |
| HP Reverb G2 Omnicept             | ✅ | :x: | Requires [BrokenEye](https://github.com/ghostiam/BrokenEye) |
| Pimax Crystal                     | ✅ | :x: | Requires [BrokenEye](https://github.com/ghostiam/BrokenEye) |

---

## Build Instructions

### Baballonia.Desktop

1. Run the associated ``download_dependencies`` script for your given platform (``.ps1`` on Windows, ``.sh`` on Linux).
2. If you are using an IDE, disable these projects:
- `VRCFaceTracking`
- `VRCFaceTracking.Core`
- `VRCFaceTracking.SDK`
- `VRCFaceTracking.Baballonia`
- `Baballonia.iOS`
- `Baballonia.Android`
3. Run ``dotnet build`` inside the ``src/Baballonia.Desktop`` directory, or build with your IDE

### Baballonia.Android/iOS

1. If you are using an IDE, disable these projects:
- `VRCFaceTracking`
- `VRCFaceTracking.Core`
- `VRCFaceTracking.SDK`
- `VRCFaceTracking.Baballonia`
- `Baballonia.Desktop`
- `Baballonia.iOS`, if you are building for Android
- `Baballonia.Android`, if you are building for iOS
2. Run ``dotnet build`` inside the ``src/Baballonia.Android`` or ``src/Baballonia.iOS`` directory, or build with your IDE

### VRCFaceTracking.Baballonia

1. If you are using an IDE, disable all projects except the following:
- `VRCFaceTracking.Core`
- `VRCFaceTracking.SDK`
- `VRCFaceTracking.Baballonia`
2. Run ``dotnet build`` inside the ``src/VRCFaceTracking.Baballonia`` directory, or build with your IDE

This will create a `VRCFaceTracking.Baballonia.zip` module which you can install manually.
