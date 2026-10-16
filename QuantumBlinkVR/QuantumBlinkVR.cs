using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using OWML.Common;
using OWML.ModHelper;
using PSVR2Toolkit.CAPI;
using UnityEngine;
using UnityEngine.UI;

namespace QuantumBlinkVR
{
    public class QuantumBlinkVR : ModBehaviour
    {
        public static QuantumBlinkVR Instance;

        // Decides when a blink counts, in Real-time or Intentional mode.
        private static readonly BlinkFilter blinkFilter = new BlinkFilter();

        // True while a counted blink is happening. The game patches read this.
        public static bool IsBlinking => blinkFilter.IsBlinking;

        // Settings, filled in from the mod's settings by Configure().
        public static float LookingDownLimit { get; private set; } = -0.38f;
        public const float PersonalSpace = 2f;
        public static bool DebugLogging { get; private set; }

        private IpcClient eyeClient;
        private bool connected;

        private bool eyesClosed;        // the headset's blink flag last frame
        private bool ignoringClosure;   // this eye closure started while looking down, so it never counts
        private float closedGazeY;      // where the eyes pointed just before they last closed
        private int blinkCount;

        private float lastGoodLeftY;
        private float lastGoodRightY;

        // After a blink is ignored for looking down, the Calibration tab says so for this many seconds.
        // (Your eyes were closed at the time, so the message has to stay up long enough to read.)
        private const float LookDownNoticeTime = 3f;
        private float lookDownNoticeUntil = -100f;

        // The mod's settings page, which opens when you select Quantum Blink VR in Options > MODS.
        private IOptionsMenuManager menuManager;
        private TabbedSubMenu page;
        private Menu settingsTab;
        private Menu calibrationTab;
        private int closeCheckFrame = -1;    // the frame we last left one of our tabs
        private MenuOption holdTimeRow;      // only shown in Intentional mode
        private readonly List<Selectable> calibrationRows = new List<Selectable>();

        // The Calibration tab's eye and status line.
        private Texture2D eyeOpenTexture;
        private Texture2D eyeClosedTexture;
        private readonly List<RawImage> eyeImages = new List<RawImage>();
        private Text statusText;
        private string shownStatus;
        private Color menuTextColor = Color.white;
        private SettingsMenuView optionsScreen;   // owns the "Default Settings" button

        // Intentional mode's progress bar under the eye: fills while your eyes are closed, and is full
        // once the blink counts. It drains back afterwards, so you can see how far you got.
        private GameObject holdBarRow;
        private RectTransform holdBarFill;
        private Image holdBarFillImage;
        private float shownHoldProgress;
        private const float HoldBarDrainTime = 0.5f;   // seconds for a full bar to empty
        private const float HoldBarThickness = 8f;

        // The number badge on the hold time slider's handle, copied from the game's numbered sliders.
        private readonly List<Graphic> holdBadgeParts = new List<Graphic>();
        private Text holdBadgeText;
        private Graphic holdHandleCenter;   // the handle's diamond, which the game colors orange or cream

        // The status line starts with this text, which is also how we find it after OWML makes it.
        private const string StartingText = "Checking eye tracking...";

        // The Calibration tab's two eyes, laid out like the mod's logo.
        private const float EyeWidth = 260f;
        private const float EyeHeight = EyeWidth * 0.625f;   // the crop in CreateEyeRow keeps 62.5% of the image's height
        private const float EyeSpacing = 390f;               // from the middle of one eye to the middle of the other

        public void Awake()
        {
            Instance = this;
            Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly());
        }

        public void Start()
        {
            ModHelper.Console.WriteLine($"{nameof(QuantumBlinkVR)} is loaded!", MessageType.Success);

            eyeClient = IpcClient.Instance();
            connected = eyeClient.Start();

            if (connected)
                ModHelper.Console.WriteLine("Connected to PSVR2 eye tracking.", MessageType.Success);
            else
                ModHelper.Console.WriteLine("Could not connect to PSVR2 eye tracking. Was SteamVR running?", MessageType.Warning);
        }

        // OWML calls this when the mod loads (before Start), and whenever a setting changes.
        public override void Configure(IModConfig config)
        {
            blinkFilter.IntentionalMode = config.GetSettingsValue<string>("blinkMode") == "Intentional";
            blinkFilter.HoldTime = config.GetSettingsValue<float>("holdTime");

            // Convert the angle into the gaze "y" value we compare against.
            float degrees = config.GetSettingsValue<float>("lookDownCutoff");
            LookingDownLimit = -Mathf.Sin(degrees * Mathf.Deg2Rad);

            DebugLogging = config.GetSettingsValue<bool>("debugLogging");

            ShowIntentionalRows();
        }

        public void Update()
        {
            UpdateBlinks();
            UpdateSettingsPage();
        }

        // Unity runs LateUpdate after every Update this frame, including the game's menu code.
        // The first time a tab opens, the game turns all its rows on (each row has flags for which
        // platforms it appears on), which would bring back the hidden hold time row. Hiding it again
        // here, before the frame is drawn, means it never flashes up.
        public void LateUpdate()
        {
            ShowIntentionalRows();
        }

        // Read the headset's blink flag and turn it into game blinks.
        private void UpdateBlinks()
        {
            bool closedNow = false;

            if (connected)
            {
                var data = eyeClient.RequestEyeTrackingData();
                var left = data.leftEye;
                var right = data.rightEye;

                closedNow = (left.isBlinkValid && left.blink)
                         || (right.isBlinkValid && right.blink);

                if (closedNow && !eyesClosed)
                {
                    // The eyes just closed. Looking down lowers the eyelids, which the eye cameras can
                    // mistake for a blink, so a closure that starts while looking down never counts.
                    closedGazeY = (lastGoodLeftY + lastGoodRightY) / 2f;
                    ignoringClosure = closedGazeY < LookingDownLimit;

                    if (ignoringClosure && DebugLogging)
                        ModHelper.Console.WriteLine($"Ignored: looking down (gaze Y {closedGazeY:F2})", MessageType.Info);
                }

                eyesClosed = closedNow;

                if (!closedNow)
                {
                    // The eyes are open, so the closure is over, and the gaze direction can be trusted again.
                    ignoringClosure = false;
                    if (left.isGazeDirValid) lastGoodLeftY = left.gazeDirNorm.y;
                    if (right.isGazeDirValid) lastGoodRightY = right.gazeDirNorm.y;
                }
            }

            if (ignoringClosure)
                lookDownNoticeUntil = Time.unscaledTime + LookDownNoticeTime;

            // A closure we're ignoring looks like open eyes to the filter, so it can never become a blink.
            // Unscaled time keeps the hold time in real seconds, even when the game is paused or sped up.
            BlinkEvent blinkEvent = blinkFilter.Update(closedNow && !ignoringClosure, Time.unscaledTime);

            if (blinkEvent == BlinkEvent.BlinkStarted)
                blinkCount++;

            if (DebugLogging)
            {
                if (blinkEvent == BlinkEvent.BlinkStarted)
                    ModHelper.Console.WriteLine($"Blink #{blinkCount} detected! (gaze Y {closedGazeY:F2})", MessageType.Success);
                else if (blinkEvent == BlinkEvent.ShortBlinkIgnored)
                    ModHelper.Console.WriteLine($"Ignored a {blinkFilter.LastClosureLength * 1000:F0} ms blink (shorter than the hold time)", MessageType.Info);
            }
        }

        // ---------- The settings page (Options > MODS > Quantum Blink VR) ----------

        // OWML calls this each time the game builds its menus: on the title screen, and when a game loads.
        public override void SetupOptionsMenu(IOptionsMenuManager menus)
        {
            menuManager = menus;

            // OWML lists every mod in Options > MODS, and selecting one opens a settings page OWML
            // builds from default-config.json. We listen to our mod's button in that list too, and
            // open our own page with two tabs in its place. OWML removes its page by itself, the
            // same way it does whenever you leave it.
            GameObject modButton = FindModsListButton();
            if (modButton != null)
            {
                // OWML swaps the button's click component for a new one, and the old one is only
                // removed at the end of this frame, so for now there are two. Only the new one ever
                // fires, so listening to both is safe.
                foreach (SubmitAction action in modButton.GetComponents<SubmitAction>())
                    action.OnSubmitAction += OpenSettingsPage;
            }
            else
            {
                // A future OWML might name things differently. Rather than lose the Calibration tab,
                // fall back to a permanent tab of our own.
                ModHelper.Console.WriteLine("Couldn't find Quantum Blink VR in the MODS list, so adding a QUANTUM BLINK VR tab instead.", MessageType.Warning);
                BuildSettingsPage("QUANTUM BLINK VR", false);
            }
        }

        // OWML calls this just before those menus are destroyed.
        public override void CleanupOptionsMenu()
        {
            ForgetPage();
            optionsScreen = null;
        }

        // OWML names each button in the MODS list after the mod's name in manifest.json.
        private GameObject FindModsListButton()
        {
            string buttonName = "UIElement-Button-" + ModHelper.Manifest.Name;
            foreach (SubmitAction button in Resources.FindObjectsOfTypeAll<SubmitAction>())
            {
                if (button.name == buttonName)
                    return button.gameObject;
            }
            return null;
        }

        private static TabButton FindTabButton(string name)
        {
            foreach (TabButton button in Resources.FindObjectsOfTypeAll<TabButton>())
            {
                if (button.name == name)
                    return button;
            }
            return null;
        }

        private void OpenSettingsPage()
        {
            try
            {
                ClosePage();  // in case an old one is still around
                TabButton pageButton = BuildSettingsPage("MOD OPTIONS", true);
                menuManager.OpenOptionsAtTab(pageButton);
            }
            catch (System.Exception e)
            {
                // If anything goes wrong, OWML's own settings page is still there to use.
                ModHelper.Console.WriteLine($"Couldn't open the Quantum Blink VR settings page: {e}", MessageType.Error);
            }
        }

        // Builds the page: a tab with SETTINGS and CALIBRATION inside it.
        // closeWhenLeft: remove the page once you leave it, like OWML does with its own mod pages.
        private TabButton BuildSettingsPage(string tabName, bool closeWhenLeft)
        {
            var (newPage, pageButton) = menuManager.CreateTabWithSubTabs(tabName);
            var (newSettingsTab, _) = menuManager.AddSubTab(newPage, "SETTINGS");
            var (newCalibrationTab, _) = menuManager.AddSubTab(newPage, "CALIBRATION");
            page = newPage;
            settingsTab = newSettingsTab;
            calibrationTab = newCalibrationTab;

            BuildSettingsTab(menuManager, newSettingsTab, closeWhenLeft);
            BuildCalibrationTab(menuManager, newCalibrationTab);

            // The options screen's "Default Settings" button only resets the game's own settings,
            // so hide it while our page is open, the same way OWML does on its MODS tab.
            newSettingsTab.OnActivateMenu += () => ShowDefaultsButton(false);
            newCalibrationTab.OnActivateMenu += () => ShowDefaultsButton(false);
            newSettingsTab.OnDeactivateMenu += () => OnTabLeft(newCalibrationTab, closeWhenLeft);
            newCalibrationTab.OnDeactivateMenu += () => OnTabLeft(newSettingsTab, closeWhenLeft);

            return pageButton;
        }

        private void OnTabLeft(Menu otherTab, bool closeWhenLeft)
        {
            // Switching between our two tabs leaves one and opens the other,
            // so only bring the button back once neither is open.
            if (!otherTab._isActivated)
                ShowDefaultsButton(true);

            // Wait a frame before deciding the page was left: the other tab might be opening.
            if (closeWhenLeft)
                closeCheckFrame = Time.frameCount;
        }

        private bool PageIsOpen()
        {
            return (settingsTab != null && settingsTab._isActivated)
                || (calibrationTab != null && calibrationTab._isActivated);
        }

        private void ClosePage()
        {
            if (page != null)
            {
                try
                {
                    menuManager.RemoveTab(page);
                }
                catch (System.Exception e)
                {
                    // Can happen while the menus are changing. Keep the page and try again next time.
                    ModHelper.Console.WriteLine($"Couldn't remove the settings page yet: {e.Message}", MessageType.Warning);
                    return;
                }
            }
            ForgetPage();
        }

        private void ForgetPage()
        {
            page = null;
            settingsTab = null;
            calibrationTab = null;
            holdTimeRow = null;
            holdBarRow = null;
            holdBarFill = null;
            holdBarFillImage = null;
            holdBadgeParts.Clear();
            holdBadgeText = null;
            holdHandleCenter = null;
            calibrationRows.Clear();
            eyeImages.Clear();
            statusText = null;
            closeCheckFrame = -1;
        }

        // The hold time slider and progress bar only mean something in Intentional mode.
        private void ShowIntentionalRows()
        {
            bool show = blinkFilter.IntentionalMode;
            if (holdTimeRow != null && holdTimeRow.gameObject.activeSelf != show)
                holdTimeRow.gameObject.SetActive(show);
            if (holdBarRow != null && holdBarRow.activeSelf != show)
                holdBarRow.SetActive(show);
        }

        private void BuildSettingsTab(IOptionsMenuManager menus, Menu menu, bool addReturnButton)
        {
            // The same heading and Return button OWML puts at the top of every mod's page.
            IModManifest manifest = ModHelper.Manifest;
            menus.CreateLabel(menu, $"{manifest.Name} {manifest.Version} by {manifest.Author}");

            Selectable firstRow = null;
            TabButton modsTab = FindTabButton("Button-MODS");
            if (addReturnButton && modsTab != null)
            {
                var back = menus.CreateButton(menu, "Return", "Return to the mod selection list.", MenuSide.CENTER);
                back.OnSubmitAction += () => menuManager.OpenOptionsAtTab(modsTab);
                firstRow = back.GetComponent<Selectable>();
            }

            MenuOption mode = AddSetting(menus, menu, "blinkMode");
            AddSetting(menus, menu, "debugLogging");

            if (firstRow == null && mode != null)
                firstRow = mode.GetComponent<Selectable>();
            SetFirstRow(menu, firstRow);
        }

        // Which row a controller or keyboard lands on when the tab opens. OWML's copy of the game's
        // tab still points at a row from the original, which OWML deletes, so point it at ours.
        private static void SetFirstRow(Menu menu, Selectable row)
        {
            if (row != null)
                menu._selectOnActivate = row;
        }

        private void BuildCalibrationTab(IOptionsMenuManager menus, Menu menu)
        {
            // The eye, which closes whenever a blink counts.
            LoadIcons();
            if (eyeOpenTexture != null && eyeClosedTexture != null)
                CreateEyeRow(menu);
            else
                menus.CreateLabel(menu, DescribeMissingIcons());

            CreateHoldBar(menu);

            // A line under the eye saying what's happening (OWML centers labels). CreateLabel doesn't
            // hand back the text it makes, so we find it afterwards by what it says.
            menus.CreateLabel(menu, StartingText);
            statusText = FindText(menu, StartingText);
            shownStatus = null;

            menus.AddSeparator(menu, true);

            MenuOption lookDown = AddSetting(menus, menu, "lookDownCutoff");
            holdTimeRow = AddSetting(menus, menu, "holdTime");

            ShowIntentionalRows();

            // Show the hold time in seconds on the slider's handle, like the game's Gamma slider.
            // The slider has 11 positions, so 0.3 to 1.3 in default-config.json gives steps of 0.1 seconds.
            if (holdTimeRow is IOWMLSliderElement holdSlider)
            {
                float startValue = ModHelper.Config.GetSettingsValue<float>("holdTime");
                if (AddNumberBadge(holdTimeRow))
                {
                    SetBadgeNumber(startValue);
                    holdSlider.OnValueChanged += SetBadgeNumber;
                }
                else if (holdTimeRow._label != null)
                {
                    // Couldn't find the game's numbered slider to copy, so put the number in the name instead.
                    Text holdLabel = holdTimeRow._label;
                    string holdTitle = holdLabel.text;
                    ShowSeconds(holdLabel, holdTitle, startValue);
                    holdSlider.OnValueChanged += value => ShowSeconds(holdLabel, holdTitle, value);
                }
            }

            // The rows you can move between with the keyboard or a controller, top to bottom.
            calibrationRows.Clear();
            foreach (Component row in new Component[] { lookDown, holdTimeRow })
            {
                if (row != null)
                    calibrationRows.Add(row.GetComponent<Selectable>());
            }
            if (calibrationRows.Count > 0)
                SetFirstRow(menu, calibrationRows[0]);

            // Draw the eye and the status line in the same color as the menu's own labels.
            if (lookDown != null && lookDown._label != null)
            {
                menuTextColor = lookDown._label.color;
                menuTextColor.a = 1f;
            }
            if (statusText != null)
                statusText.color = menuTextColor;
            if (holdBarFillImage != null)
                holdBarFillImage.color = menuTextColor;
        }

        // Adds one setting from default-config.json to a tab, with the title, tooltip and choices written there.
        // Changes are saved to config.json, the same file OWML uses, so the Mod Manager app shows them too.
        private MenuOption AddSetting(IOptionsMenuManager menus, Menu menu, string key)
        {
            if (!ModHelper.Config.Settings.TryGetValue(key, out object stored) || !(stored is JObject setting))
            {
                ModHelper.Console.WriteLine($"Can't find the setting \"{key}\" in default-config.json", MessageType.Error);
                return null;
            }

            string title = setting["title"]?.ToString() ?? key;
            string tooltip = setting["tooltip"]?.ToString() ?? "";

            // OWML's options report their starting value once, the first time their tab is shown.
            // Skipping that report means only the player's own changes get saved.
            bool skippedStartingValue = false;
            void Save(object value)
            {
                if (skippedStartingValue)
                    SaveSetting(key, value);
                skippedStartingValue = true;
            }

            switch (setting["type"]?.ToString())
            {
                case "selector":
                    string[] choices = setting["options"].ToObject<string[]>();
                    int current = System.Array.IndexOf(choices, ModHelper.Config.GetSettingsValue<string>(key));
                    var selector = menus.AddSelectorInput(menu, title, choices, tooltip, false, Mathf.Max(current, 0));
                    selector.OnValueChanged += (index, choice) => Save(choice);
                    return selector as MenuOption;

                case "slider":
                    float min = setting["min"].ToObject<float>();
                    float max = setting["max"].ToObject<float>();
                    var slider = menus.AddSliderInput(menu, title, min, max, tooltip, ModHelper.Config.GetSettingsValue<float>(key));
                    // Rounding keeps config.json tidy: 0.36 instead of 0.36000001430511475.
                    slider.OnValueChanged += value => Save(System.Math.Round(value, 2));
                    return slider as MenuOption;

                case "toggle":
                    var checkbox = menus.AddCheckboxInput(menu, title, tooltip, ModHelper.Config.GetSettingsValue<bool>(key));
                    checkbox.OnValueChanged += value => Save(value);
                    return checkbox as MenuOption;

                default:
                    ModHelper.Console.WriteLine($"Don't know how to show the setting \"{key}\" in the menu", MessageType.Error);
                    return null;
            }
        }

        // Saves a setting the same way OWML's own settings pages do.
        private void SaveSetting(string key, object value)
        {
            ModHelper.Config.SetSettingsValue(key, value);
            if (DebugLogging || key == "debugLogging")
                ModHelper.Console.WriteLine($"Setting saved: {key} = {value}", MessageType.Info);
            ModHelper.Storage.Save(ModHelper.Config, "config.json");
            Configure(ModHelper.Config);
        }

        // Adds a row to a tab's list with the eyes side by side in the middle of it.
        private void CreateEyeRow(Menu menu)
        {
            // The row. Its LayoutElement tells the list how much space to give it.
            var row = new GameObject("QuantumBlinkVR-Eyes", typeof(RectTransform), typeof(LayoutElement));
            row.transform.SetParent(GetMenuList(menu), false);
            ((RectTransform)row.transform).sizeDelta = new Vector2(0f, EyeHeight + 30f);
            var layout = row.GetComponent<LayoutElement>();
            layout.minHeight = EyeHeight + 30f;
            layout.preferredHeight = EyeHeight + 30f;
            layout.flexibleWidth = 1f;

            // One eye left of the middle and one right of it (-1 and +1).
            eyeImages.Clear();
            foreach (float side in new[] { -1f, 1f })
            {
                var eye = new GameObject("Eye", typeof(RectTransform), typeof(RawImage));
                eye.transform.SetParent(row.transform, false);
                var rect = (RectTransform)eye.transform;
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(EyeWidth, EyeHeight);
                rect.anchoredPosition = new Vector2(side * EyeSpacing / 2f, 0f);

                var image = eye.GetComponent<RawImage>();
                image.texture = eyeOpenTexture;
                image.uvRect = new Rect(0f, 0.1875f, 1f, 0.625f);  // crop off the empty space above and below the eye
                image.raycastTarget = false;                        // it's only a picture, not something to click
                eyeImages.Add(image);
            }
        }

        // Adds a thin bar under the eye. Its fill reaches from the left edge to the current progress.
        private void CreateHoldBar(Menu menu)
        {
            float rowHeight = HoldBarThickness + 16f;
            var row = new GameObject("QuantumBlinkVR-HoldBar", typeof(RectTransform), typeof(LayoutElement));
            row.transform.SetParent(GetMenuList(menu), false);
            ((RectTransform)row.transform).sizeDelta = new Vector2(0f, rowHeight);
            var layout = row.GetComponent<LayoutElement>();
            layout.minHeight = rowHeight;
            layout.preferredHeight = rowHeight;
            layout.flexibleWidth = 1f;

            // The empty bar, as wide as the eyes and centered under them.
            var track = new GameObject("Track", typeof(RectTransform), typeof(Image));
            track.transform.SetParent(row.transform, false);
            var trackRect = (RectTransform)track.transform;
            trackRect.anchorMin = new Vector2(0.5f, 0.5f);
            trackRect.anchorMax = new Vector2(0.5f, 0.5f);
            trackRect.sizeDelta = new Vector2(EyeSpacing + EyeWidth, HoldBarThickness);  // from the left eye's edge to the right eye's
            var trackImage = track.GetComponent<Image>();
            trackImage.color = new Color(0.22f, 0.22f, 0.22f);  // the same dark grey as the menu's dotted lines
            trackImage.raycastTarget = false;

            // The fill. Its left edge stays put and its right edge moves with the progress.
            var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(track.transform, false);
            holdBarFill = (RectTransform)fill.transform;
            holdBarFill.anchorMin = Vector2.zero;
            holdBarFill.anchorMax = new Vector2(0f, 1f);
            holdBarFill.offsetMin = Vector2.zero;
            holdBarFill.offsetMax = Vector2.zero;
            holdBarFillImage = fill.GetComponent<Image>();
            holdBarFillImage.color = menuTextColor;
            holdBarFillImage.raycastTarget = false;

            holdBarRow = row;
            shownHoldProgress = 0f;
        }

        // Where OWML puts a tab's options: the scrolling list inside it.
        private static Transform GetMenuList(Menu menu)
        {
            return menu.transform.Find("Scroll View/Viewport/Content")
                ?? menu.transform.Find("Content")
                ?? menu.transform;
        }

        private static Text FindText(Menu menu, string content)
        {
            // "true" also searches parts of the menu that are hidden right now.
            foreach (Text text in menu.GetComponentsInChildren<Text>(true))
            {
                if (text.text == content)
                    return text;
            }
            return null;
        }

        // Called every frame, so the page reacts the moment something changes.
        private void UpdateSettingsPage()
        {
            // We left the page last frame and didn't come back to it, so remove it.
            if (closeCheckFrame >= 0 && Time.frameCount > closeCheckFrame)
            {
                closeCheckFrame = -1;
                if (!PageIsOpen())
                    ClosePage();
            }

            if (calibrationTab != null && calibrationTab._isActivated)
                LinkVisibleRows();

            // The number badge takes the color the game gives the rest of the handle:
            // orange normally, cream while the row is selected.
            if (holdHandleCenter != null && holdBadgeParts.Count > 1 && holdBadgeParts[1] != null)
                holdBadgeParts[1].color = holdHandleCenter.color;

            // Unity's "== null" is also true once the menu has been destroyed.
            if (statusText != null)
            {
                string status = GetCalibrationStatus();
                if (status != shownStatus)
                {
                    statusText.text = status;
                    shownStatus = status;
                }
            }

            if (eyeImages.Count > 0)
            {
                Texture2D texture;
                float brightness;
                if (blinkFilter.IsBlinking)
                {
                    texture = eyeClosedTexture;
                    brightness = 1f;
                }
                else if (blinkFilter.EyesClosed)
                {
                    texture = eyeClosedTexture;  // Intentional mode: closed, but not long enough yet
                    brightness = 0.4f;
                }
                else
                {
                    texture = eyeOpenTexture;
                    brightness = connected ? 1f : 0.3f;  // dim while there's no eye tracking
                }

                Color color = menuTextColor;
                color.a *= brightness;
                foreach (RawImage eye in eyeImages)
                {
                    if (eye == null) continue;  // the menu was destroyed
                    eye.texture = texture;
                    eye.color = color;
                }
            }

            if (holdBarFill != null && holdBarRow.activeSelf)
            {
                // Fill as your eyes stay closed. Once they open, drain smoothly instead of emptying
                // at once: your eyes were closed while it filled, so this is your chance to see it.
                float progress = blinkFilter.HoldProgress(Time.unscaledTime);
                if (progress >= shownHoldProgress)
                    shownHoldProgress = progress;
                else
                    shownHoldProgress = Mathf.MoveTowards(shownHoldProgress, progress, Time.unscaledDeltaTime / HoldBarDrainTime);
                holdBarFill.anchorMax = new Vector2(shownHoldProgress, 1f);
            }
        }

        // With a keyboard or controller, up and down move between rows that the game links together.
        // A hidden row (the hold time, in Real-time mode) would break that chain, so link each
        // visible row straight to the next visible one.
        private void LinkVisibleRows()
        {
            Selectable previous = null;
            foreach (Selectable row in calibrationRows)
            {
                if (row == null || !row.gameObject.activeSelf)
                    continue;

                if (previous != null)
                {
                    Navigation down = previous.navigation;
                    if (down.mode == Navigation.Mode.Explicit && down.selectOnDown != row)
                    {
                        down.selectOnDown = row;
                        previous.navigation = down;
                    }

                    Navigation up = row.navigation;
                    if (up.mode == Navigation.Mode.Explicit && up.selectOnUp != previous)
                    {
                        up.selectOnUp = previous;
                        row.navigation = up;
                    }
                }
                previous = row;
            }
        }

        private string GetCalibrationStatus()
        {
            if (!connected)
                return "Could not connect to PSVR2 eye tracking. Was SteamVR running?";
            if (Time.unscaledTime < lookDownNoticeUntil)
                return "Ignored a blink because you were looking down.";

            string howToTest = blinkFilter.IntentionalMode ? "Close your eyes for a moment to test." : "Blink to test.";
            return $"{howToTest}     Blinks counted: {blinkCount}";
        }

        private void ShowDefaultsButton(bool visible)
        {
            if (optionsScreen == null)
                optionsScreen = FindObjectOfType<SettingsMenuView>();
            if (optionsScreen != null && optionsScreen._resetToDefaultButton != null)
                optionsScreen._resetToDefaultButton.gameObject.SetActive(visible);
        }

        // The game's Gamma and Field of View sliders have three extra parts on their handle: a bigger
        // badge outline, the badge itself, and the number. OWML's sliders are copied from one without
        // them, so we copy those three parts onto our slider's handle.
        private bool AddNumberBadge(MenuOption sliderRow)
        {
            Slider slider = sliderRow.GetComponentInChildren<Slider>(true);
            Transform numberedHandle = FindNumberedHandle();
            if (slider == null || slider.handleRect == null || numberedHandle == null)
                return false;

            string[] partNames = { "EnlargedHandleBG", "EnlargedHandleCenter", "HandleDisplay" };
            foreach (string partName in partNames)
            {
                if (numberedHandle.Find(partName) == null)
                    return false;
            }

            holdBadgeParts.Clear();
            foreach (string partName in partNames)
            {
                GameObject copy = Instantiate(numberedHandle.Find(partName).gameObject, slider.handleRect, false);
                copy.name = partName;
                Graphic graphic = copy.GetComponent<Graphic>();
                if (graphic != null)
                    graphic.enabled = true;  // the game hides these on unselected rows; we always show them
                holdBadgeParts.Add(graphic);
            }

            // The game only shows the number while a row is selected. We always show it, colored like the
            // handle's diamond underneath (UpdateSettingsPage copies that color across every frame).
            holdHandleCenter = slider.handleRect.Find("HandleCenter")?.GetComponent<Graphic>();
            if (holdBadgeParts[1] != null)
                holdBadgeParts[1].color = holdHandleCenter != null ? holdHandleCenter.color : menuTextColor;

            holdBadgeText = holdBadgeParts[2] as Text;
            return holdBadgeText != null;
        }

        // The handle of one of the game's numbered sliders. Its number is the slider's "secondary text".
        private static Transform FindNumberedHandle()
        {
            foreach (SliderElement element in Resources.FindObjectsOfTypeAll<SliderElement>())
            {
                Text number = element._secondaryTextField;
                if (number != null && number.name == "HandleDisplay" && number.transform.parent != null)
                    return number.transform.parent;
            }
            return null;
        }

        private void SetBadgeNumber(float seconds)
        {
            if (holdBadgeText != null)
                holdBadgeText.text = seconds.ToString("0.0");
        }

        private static void ShowSeconds(Text label, string title, float seconds)
        {
            if (label != null)
                label.text = $"{title}: {seconds:0.0} s";
        }

        // Says where the mod looked for the eye pictures and what it found there,
        // so a misnamed file (like "eye_open.png.png") is easy to spot.
        private string DescribeMissingIcons()
        {
            string folder = Path.Combine(ModHelper.Manifest.ModFolderPath, "assets");
            if (!Directory.Exists(folder))
                return $"Eye images not found: there's no assets folder in {ModHelper.Manifest.ModFolderPath}";

            var names = new List<string>();
            foreach (string file in Directory.GetFiles(folder))
                names.Add($"\"{Path.GetFileName(file)}\"");
            string found = names.Count > 0 ? string.Join(", ", names.ToArray()) : "nothing";
            return $"Eye images not found. Looked for \"eye_open.png\" and \"eye_closed.png\" in {folder} and found: {found}";
        }

        // Loads the eye pictures the first time they're needed.
        private void LoadIcons()
        {
            if (eyeOpenTexture == null)
                eyeOpenTexture = LoadIcon("eye_open.png");
            if (eyeClosedTexture == null)
                eyeClosedTexture = LoadIcon("eye_closed.png");
        }

        // Loads one of the mod's PNG images from its assets folder.
        private Texture2D LoadIcon(string fileName)
        {
            string path = Path.Combine(ModHelper.Manifest.ModFolderPath, "assets", fileName);
            if (!File.Exists(path))
            {
                ModHelper.Console.WriteLine($"Missing image: {path}", MessageType.Error);
                return null;
            }

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, true);
            texture.LoadImage(File.ReadAllBytes(path));

            // The icons are white line art, so make every pixel white and keep only the transparency.
            // That removes dark edges left over from exporting, and lets us tint the icon any color.
            Color32[] pixels = texture.GetPixels32();
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i].r = 255;
                pixels[i].g = 255;
                pixels[i].b = 255;
            }
            texture.SetPixels32(pixels);
            texture.Apply(true);  // true: also make the smaller copies used when it's drawn small

            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Trilinear;
            return texture;
        }

        public void OnDestroy()
        {
            blinkFilter.Reset();
            eyeClient?.Stop();
        }
    }
}