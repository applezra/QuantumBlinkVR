<img width="3900" height="1300" alt="QuantumBlinkVR Logo" src="https://github.com/user-attachments/assets/7a60bd2e-9a96-4b22-98a3-1ae8ede9f53d" />


# QuantumBlinkVR

QuantumBlinkVR is an add-on to NomaiVR that enables you to interact with quantum objects by closing your eyes. Currently only supporting the PSVR2 through PSVR2Toolkit, this mod reads eye tracking data which allows nearby quantum objects to move.

## Requirements

- Outer Wilds on PC
- [NomaiVR](https://outerwildsmods.com/mods/nomaivr/)
- A PlayStation VR2 headset connected to your PC and working in SteamVR (the official PC adapter or a third-party one)
- [PSVR2Toolkit](https://github.com/BnuuySolutions/PSVR2Toolkit) with eye tracking working. Use the **latest stable release**: experimental builds may not share eye tracking data with other programs.

## Installation

1. Install PSVR2Toolkit by following its own instructions.
2. Install QuantumBlinkVR from the [Outer Wilds Mod Manager](https://outerwildsmods.com/mod-manager/).
3. Start the game. The mod console should say **"Connected to PSVR2 eye tracking."**
4. To check it's working, go to **Options > MODS > Quantum Blink VR > CALIBRATION** and blink. The line under the eyes counts every blink that registers.

## Settings

Find these in **Options > MODS > Quantum Blink VR**. You can also change them in the Mod Manager.

**Settings tab**

- **Blink mode:** In **Real-time** mode, every blink counts the moment it happens. In **Intentional** mode, a blink only counts when you keep your eyes closed for a moment, so normal blinking doesn't affect the game. Use Intentional if quantum objects move as you open your eyes.
- **Debug logging:** Logs every blink and every quantum move to the mod console.

**Calibration tab**

- **Gaze blink cut-off:** Looking down can make the eye cameras see a blink that didn't happen. Increase it if real blinks get missed when you look down. Decrease it if looking down causes false blinks. When a blink is ignored because you were looking down, the Calibration tab says so.
- **Intentional hold time:** How long you must keep your eyes closed in Intentional mode. Only shown in Intentional mode.

## Known limitations

- Blinks that start while your eyes point far down are ignored, because a lowered eyelid looks like a blink to the eye cameras. Tilting your head down to look at something works normally. You can adjust this with **Gaze blink cut-off**.
- A blink with either eye counts, so winks count too.
- Taking the headset off and on can register a few false blinks.
- Only the PSVR2 is supported for now.
- Not yet tested together with other mods that change quantum objects, such as Quantum Space Buddies.

## Troubleshooting

If the mod console or the **CALIBRATION** tab says **"Could not connect to PSVR2 eye tracking"**, make sure SteamVR is running with your headset connected, and that you are using the latest stable PSVR2Toolkit release.

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
