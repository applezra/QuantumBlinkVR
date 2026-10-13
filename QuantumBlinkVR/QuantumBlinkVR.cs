using System.Reflection;
using HarmonyLib;
using OWML.Common;
using OWML.ModHelper;
using PSVR2Toolkit.CAPI;
using UnityEngine;

namespace QuantumBlinkVR
{
    public class QuantumBlinkVR : ModBehaviour
    {
        public static QuantumBlinkVR Instance;

        // True while a counted blink is happening. The game patches read this.
        public static bool IsBlinking { get; private set; }

        // Settings, filled in from the mod's settings menu by Configure().
        public static float LookingDownLimit { get; private set; } = -0.38f;
        public const float PersonalSpace = 2f;
        public static bool SkipCooldown { get; private set; } = true;
        public static bool DebugLogging { get; private set; }

        private IpcClient eyeClient;
        private bool connected;

        private bool eyesClosed;
        private int blinkCount;

        private float lastGoodLeftY;
        private float lastGoodRightY;

        public void Awake()
        {
            Instance = this;
            Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly());
        }

        public void Start()
        {
            ModHelper.Console.WriteLine($"{nameof(QuantumBlinkVR)} is loaded!", MessageType.Success);

            // Read the settings once at startup.
            Configure(ModHelper.Config);

            eyeClient = IpcClient.Instance();
            connected = eyeClient.Start();

            if (connected)
                ModHelper.Console.WriteLine("Connected to PSVR2 eye tracking.", MessageType.Success);
            else
                ModHelper.Console.WriteLine("Could not connect to PSVR2 eye tracking. Was SteamVR running?", MessageType.Warning);
        }

        // OWML also calls this whenever the player changes a setting in the menu.
        public override void Configure(IModConfig config)
        {
            // Convert the angle into the gaze "y" value we compare against.
            float degrees = config.GetSettingsValue<float>("lookDownCutoff");
            LookingDownLimit = -Mathf.Sin(degrees * Mathf.Deg2Rad);

            SkipCooldown = config.GetSettingsValue<bool>("skipCooldown");
            DebugLogging = config.GetSettingsValue<bool>("debugLogging");
        }

        public void Update()
        {
            if (!connected) return;

            var data = eyeClient.RequestEyeTrackingData();
            var left = data.leftEye;
            var right = data.rightEye;

            bool closedNow = (left.isBlinkValid && left.blink)
                          || (right.isBlinkValid && right.blink);

            if (closedNow && !eyesClosed)
            {
                float lastGazeY = (lastGoodLeftY + lastGoodRightY) / 2f;

                if (lastGazeY < LookingDownLimit)
                {
                    if (DebugLogging)
                        ModHelper.Console.WriteLine($"Ignored: looking down (gaze Y {lastGazeY:F2})", MessageType.Info);
                }
                else
                {
                    blinkCount++;
                    IsBlinking = true;
                    if (DebugLogging)
                        ModHelper.Console.WriteLine($"Blink #{blinkCount} detected! (gaze Y {lastGazeY:F2})", MessageType.Success);
                }
            }

            // When the eyes open again, the blink is over.
            if (!closedNow)
                IsBlinking = false;

            eyesClosed = closedNow;

            if (!closedNow)
            {
                if (left.isGazeDirValid) lastGoodLeftY = left.gazeDirNorm.y;
                if (right.isGazeDirValid) lastGoodRightY = right.gazeDirNorm.y;
            }
        }

        public void OnDestroy()
        {
            IsBlinking = false;
            eyeClient?.Stop();
        }
    }
}