<img width="3900" height="1300" alt="Quantum Blink Icon" src="https://github.com/user-attachments/assets/e9c80aff-6bbc-4645-a37e-c22b4fdbc52b" />


# QuantumBlinkVR

QuantumBlinkVR is an add-on to NomaiVR that enables you to interact with quantum objects by closing your eyes. Currently only supporting the PSVR2 through PSVR2Toolkit, this mod reads eye tracking data and marks nearby quantum objects as not seen the moment you blink.

## Requirements

- Outer Wilds on PC
- [NomaiVR](https://outerwildsmods.com/mods/nomaivr/) (the Mod Manager installs it automatically with this mod)
- A PlayStation VR2 headset connected to your PC and working in SteamVR (the official PC adapter or a third-party one)
- [PSVR2Toolkit](https://github.com/BnuuySolutions/PSVR2Toolkit) with eye tracking working. Use the **latest stable release**: experimental builds may not share eye tracking data with other programs.

## Installation

1. Install PSVR2Toolkit by following its own instructions.
2. Install QuantumBlinkVR from the [Outer Wilds Mod Manager](https://outerwildsmods.com/mod-manager/).
3. Start the game. The mod console should say **"Connected to PSVR2 eye tracking."**

## Settings

You can change these in the mod's settings, in the Mod Manager or in the game's options menu.

- **Gaze blink cut-off:** Looking down can make the eye cameras see a blink that didn't happen. Increase it if real blinks get missed when you look down. Decrease it if looking down causes false blinks.
- **Instant back-to-back blinks:** Quantum objects can move on every blink, even blinks less than half a second apart. Turn off to apply the game's normal half-second wait to blinks as well.
- **Debug logging:** Logs every blink and every quantum move to the mod console.

## Known limitations

- Blinks that start while your eyes point far down are ignored, because a lowered eyelid looks like a blink to the eye cameras. Tilting your head down to look at something works normally. You can adjust this with **Gaze blink cut-off**.
- A blink with either eye counts, so winks count too.
- Taking the headset off and on can register a few false blinks.
- Only the PSVR2 is supported for now.
- Not yet tested together with other mods that change quantum objects, such as Quantum Space Buddies.

## Troubleshooting

If the mod console says **"Could not connect to PSVR2 eye tracking"**, make sure SteamVR is running with your headset connected, and that you are using the latest stable PSVR2Toolkit release.

## Future plans

- Support for other eye-tracking headsets
- A flat-screen version that tracks blinks with a webcam, so you can play without VR
- Testing with Quantum Space Buddies and other mods that change quantum objects

## Credits

- Mod by **Zra**
- [PSVR2Toolkit](https://github.com/BnuuySolutions/PSVR2Toolkit) by BnuuySolutions, which makes PSVR2 eye tracking available on PC. This mod includes its `IpcClient.cs` and `IpcProtocol.cs` files.
- [NomaiVR](https://outerwildsmods.com/mods/nomaivr/) by Raicuparta
- [OWML](https://owml.outerwildsmods.com/) and the Outer Wilds modding community
- Outer Wilds by Mobius Digital

## License

The mod's own code is released under the MIT license. `IpcClient.cs` and `IpcProtocol.cs` come from PSVR2Toolkit and remain under PSVR2Toolkit's license. This mod is free and non-commercial.
