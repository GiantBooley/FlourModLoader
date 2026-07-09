using BepInEx;
using BepInEx.Logging;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using System;
using System.IO;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using System.Linq;
using I2.Loc;

namespace FPModLoader;

// why i did virtual class instead of interface: the mod loader can set monobehaviour enabled on mods so the fixedupdate doesnt get called
public abstract class FPMod : BaseUnityPlugin {
    // call in every mod: FPModLoaderPlugin.Instance.RegisterMod(this);

    // metadata
    public abstract string author { get; }
    public abstract string description { get; }

    // callbacks
    public virtual void OnModEnable() {}
    public virtual void OnModDisable() {}
    public virtual void OnGameStateChange(GameState oldState, GameState newState, Scene scene) {}
    public virtual void OnPause() {}
    public virtual void OnUnpause() {}

    // getters
    public virtual GameObject GetConfigEditor() => null;
    public virtual List<GameObject> GetUIElements() => new List<GameObject>();
};


[BepInPlugin("goi.flowerpot.fpmodloader", "FlowerPot Mod Loader", "1.0.0.0")]
[BepInProcess("GettingOverIt.exe")]
[BepInDependency("com.bepis.bepinex.configurationmanager")]
public class FPModLoaderPlugin : FPMod {

    public override string author => "Flower Pot";
    public override string description => "mod loader";

    // singleton
    public static FPModLoaderPlugin Instance { get; private set; }

    public void RegisterMod(FPMod mod) {
        string guid = mod.Info.Metadata.GUID;
        // disable mod if mod is disabled
        _enabledModConfigs.Add(guid, Config.Bind("Enabled Mods", guid, false));
        if (!_enabledModConfigs[guid].Value) DisableMod(mod);
        _mods.Add(guid, mod);
        if (modLoaderPanel != null) {
            AddModToModMenu(mod);
        }
    }

    internal static new ManualLogSource Logger;

    private ConfigurationManager.ConfigurationManager _configurationManager;

    private TMP_FontAsset _nowayFont;
    private Transform _menuTransform;

    private GameObject _settingsContainer;
    private GameObject _modSettingsContainer;

    private Dictionary<string, FPMod> _mods;
    private Dictionary<string, ConfigEntry<bool>> _enabledModConfigs;

    public AssetBundle modLoaderUIBundle;
    // prefabs
    public GameObject modLoaderPanelPrefab;
    public GameObject templateModRowPrefab;
    public GameObject templateSettingRowPrefab;
    public GameObject templateCheckboxPrefab;
    public GameObject templateSettingsContainerPrefab;
    public GameObject templateInputFieldPrefab;

    public GameObject modLoaderPanel;
    private string _pluginFolder = "BepInEx/plugins/FPModLoader";


    private void Awake() {
        Instance = this;

        // Plugin startup logic
        Logger = base.Logger;
        Logger.LogInfo($"Plugin FPModLoader is loaded!");

        // patch with Harmony
        var harmony = new Harmony("goi.flowerpot.fpmodloader");
        harmony.PatchAll();

        _nowayFont = Resources.FindObjectsOfTypeAll<TMP_FontAsset>().FirstOrDefault(f => f.name == "Noway Regular SDF");

        _configurationManager = (ConfigurationManager.ConfigurationManager)Chainloader.PluginInfos["com.bepis.bepinex.configurationmanager"].Instance;

        // load ui
        modLoaderUIBundle = AssetBundle.LoadFromFile(Path.Combine(Path.GetDirectoryName(Application.dataPath), _pluginFolder, "modloaderui.scene"));
        modLoaderPanelPrefab = modLoaderUIBundle.LoadAsset<GameObject>("ModLoader.prefab");
        templateModRowPrefab = modLoaderUIBundle.LoadAsset<GameObject>("TemplateModRow.prefab");
        templateSettingRowPrefab = modLoaderUIBundle.LoadAsset<GameObject>("TemplateSettingRow.prefab");
        templateCheckboxPrefab = modLoaderUIBundle.LoadAsset<GameObject>("TemplateCheckbox.prefab");
        templateSettingsContainerPrefab = modLoaderUIBundle.LoadAsset<GameObject>("TemplateSettingsContainer.prefab");
        templateInputFieldPrefab = modLoaderUIBundle.LoadAsset<GameObject>("TemplateInputField.prefab");

        // Set OnSceneLoaded to execute when a scene is loaded
        SceneManager.sceneLoaded += OnSceneLoaded;

        // init goiInterface
        GOIConstants.timer = 0f;
        GOIConstants.gameState = GameState.MainMenu;
        GOIConstants.CreateConstant<float>("timeScale", 1f, timeScale => Time.timeScale = timeScale);
        GOIConstants.AddModifier("timeScale", new LambdaModifier<float>(timeScale => timeScale * 0f, 0, "pause", false));
        GOIConstants.CreateConstant<Vector2>("gravity", new Vector2(0f, -30f), gravity => Physics2D.gravity = gravity);
        GOIConstants.AddModifier("gravity", new LambdaModifier<Vector2>(gravity => gravity * 0f, 0, "zerograv", false));


        _mods = new Dictionary<string, FPMod>();
        _enabledModConfigs = new Dictionary<string, ConfigEntry<bool>>();

        RegisterMod(this);
        _enabledModConfigs[Info.Metadata.GUID].Value = true;
    }
    private void LateUpdate() {
        GOIConstants.timer += Time.deltaTime;
    }
    // mod management
    public void ChangeGameState(GameState newState, Scene scene) {
        GameState oldState = GOIConstants.gameState;
        GOIConstants.gameState = newState;
        foreach (var mod in _mods) {
            if (!_enabledModConfigs[mod.Value.Info.Metadata.GUID].Value) continue;
            mod.Value.OnGameStateChange(oldState, newState, scene);
        }
    }
    private void EnableMod(FPMod mod) {
        mod.enabled = true;
        _enabledModConfigs[mod.Info.Metadata.GUID].Value = true;
        mod.OnModEnable();
    }
    private void DisableMod(FPMod mod) {
        mod.OnModDisable();
        mod.enabled = false;
        _enabledModConfigs[mod.Info.Metadata.GUID].Value = false;
    }

    public bool IsModEnabled(string guid) {
        return _enabledModConfigs[guid].Value;
    }

    public override void OnGameStateChange(GameState oldState, GameState newState, Scene scene) {
        if (newState == GameState.InGame)  {
            // set timer to 0
            GOIConstants.timer = 0f;

            // find player and camera
            PlayerControl player = UnityEngine.Object.FindObjectOfType<PlayerControl>();
            if (player == null) {
                Logger.LogWarning("PlayerControl not found in scene");
            } else {
                GOIConstants.playerCtrl = player;
                GOIConstants.player = player.gameObject;
            }
            CameraControl camera = UnityEngine.Object.FindObjectOfType<CameraControl>();
            if (camera == null) {
                Logger.LogWarning("CameraControl not found in scene");
            } else {
                GOIConstants.cameraCtrl = camera;
                GOIConstants.mainCamera = camera.gameObject;
            }
            // find canvas
            GOIConstants.settingsManager = UnityEngine.Object.FindObjectOfType<SettingsManager>();
            if (GOIConstants.settingsManager == null) {
                Logger.LogWarning("SettingsManager not found in scene");
            } else {
                GOIConstants.canvas = GOIConstants.settingsManager.transform.parent.gameObject;
            }


            GameObject menuObject = Resources.FindObjectsOfTypeAll<GameObject>().FirstOrDefault(go => go.name == "InGame Menu");
            if (menuObject == null) Logger.LogWarning("Couldn't find InGame Menu");
            else _menuTransform = menuObject.transform;

            //StartCoroutine(enableMenu());


            // Load font
            /*if (nowayFont == null) {
                GameObject fontTextObject = Resources.FindObjectsOfTypeAll<GameObject>().FirstOrDefault(go => go.name == "Mouse sensitivity");
                if (fontTextObject != null) {
                    TMP_Text tmpText = fontTextObject.GetComponent<TMP_Text>();
                    if (tmpText == null) Logger.LogWarning("Text has no TMP_Text");
                    else {
                        nowayFont = tmpText.font;
                    }
                } else {
                    Logger.LogWarning("Failed to find mouse sensitivity text for font");
                }
            }*/

            // =-=-=-=- Edit ingame menu =-=-=-=-=-
            // move default columns to settings container
            _settingsContainer = new GameObject("Settings container");
            _settingsContainer.transform.SetParent(menuObject.transform, false);
            _settingsContainer.transform.localPosition = new Vector3(0f, 0f, 0f);
            _settingsContainer.transform.localScale = new Vector3(1f, 1f, 1f);
            GameObject valueColumn = Resources.FindObjectsOfTypeAll<GameObject>().FirstOrDefault(go => go.name == "Value Column");
            GameObject labelColumn = Resources.FindObjectsOfTypeAll<GameObject>().FirstOrDefault(go => go.name == "Label Column");
            valueColumn.transform.SetParent(_settingsContainer.transform, true);
            labelColumn.transform.SetParent(_settingsContainer.transform, true);

            // create mod settings container
            _modSettingsContainer = new GameObject("Mod settings container");
            _modSettingsContainer.transform.SetParent(menuObject.transform, false);
            _modSettingsContainer.transform.localPosition = new Vector3(0f, 0f, 0f);
            _modSettingsContainer.transform.localScale = new Vector3(1f, 1f, 1f);
            _modSettingsContainer.SetActive(false);

            if (modLoaderPanelPrefab == null) {
                Logger.LogWarning("mod loader panel prefab not found");
            } else {
                // add to menu
                modLoaderPanel = CreateModMenu();
                modLoaderPanel.transform.SetParent(_modSettingsContainer.transform, false);

                // add mods
                foreach (var mod in _mods) {
                    AddModToModMenu(mod.Value);
                }
            }

            //GameObject testButton = CreateButton("BepInEx Config Manager", modSettingsContainer.transform, new Vector2(0f, 0f));
            //testButton.GetComponent<Button>().onClick.AddListener(() => configurationManager.DisplayingWindow = !configurationManager.DisplayingWindow);


            // add mods button and move apply button
            //sm = Resources.FindObjectsOfTypeAll<SettingsManager>().FirstOrDefault();
            if (GOIConstants.settingsManager == null) {
                Debug.LogWarning("SettingsManager not found in scene");
            } else {
                GameObject applyButton = GOIConstants.settingsManager.applyResolutionButton.gameObject;
                RectTransform rect = applyButton.GetComponent<RectTransform>();
                rect.anchoredPosition = new Vector2(rect.anchoredPosition.x + 150f, rect.anchoredPosition.y);

                Transform buttonRowTransform = applyButton.transform.parent;

                GameObject modsButton = CreateButton("Mods", buttonRowTransform, new Vector2(rect.anchoredPosition.x - 300f, rect.anchoredPosition.y));
                modsButton.GetComponent<Button>().onClick.AddListener(ToggleModsContainer);
            }

            // enable menu
            _settingsContainer.SetActive(true);
            GOIConstants.canvas.transform.Find("InGame Menu").Find("Panel").gameObject.SetActive(true);

            Debug.Log("goiconstants state:");
            Debug.Log("player is null: " + (GOIConstants.player == null ? "true" : "false"));
            Debug.Log("playerCtrl is null: " + (GOIConstants.playerCtrl == null ? "true" : "false"));
            Debug.Log("mainCamera is null: " + (GOIConstants.mainCamera == null ? "true" : "false"));
            Debug.Log("cameraCtrl is null: " + (GOIConstants.cameraCtrl == null ? "true" : "false"));
            Debug.Log("loader is null: " + (GOIConstants.loader == null ? "true" : "false"));
            Debug.Log("canvas is null: " + (GOIConstants.canvas == null ? "true" : "false"));
            Debug.Log("settingsManager is null: " + (GOIConstants.settingsManager == null ? "true" : "false"));
        } else if (newState == GameState.MainMenu) {
            // disable pause menu timescale
            GOIConstants.ModifierSetEnabled<float>("timeScale", "pause", false);
            
            GOIConstants.loader = UnityEngine.Object.FindObjectOfType<Loader>();
            GOIConstants.canvas = GameObject.Find("Canvas");

            // add mod menu
            if (modLoaderPanelPrefab == null) {
                Logger.LogWarning("mod loader panel prefab not found");
            } else {
                // add to menu
                modLoaderPanel = CreateModMenu();
                modLoaderPanel.transform.SetParent(GOIConstants.canvas.transform, false);

                // add mods
                foreach (var mod in _mods) {
                    AddModToModMenu(mod.Value);
                }
                modLoaderPanel.SetActive(false);
            }

            // add mods button
            GameObject modsButtonObject = CreateMenuButton("Mods", GOIConstants.loader.menu.transform, 0);
            Button modsButton = modsButtonObject.GetComponent<Button>();
            modsButton.onClick.AddListener(ToggleModsContainer);
        }
    }
    public void FixedUpdate() {
        if (GOIConstants.gameState == GameState.InGame && (GOIConstants.player == null || GOIConstants.playerCtrl == null)) {
            PlayerControl player = UnityEngine.Object.FindObjectOfType<PlayerControl>();
            if (player == null) {
                Logger.LogWarning("PlayerControl not found in scene");
            } else {
                GOIConstants.playerCtrl = player;
                GOIConstants.player = player.gameObject;
            }
        }
    }
    /*private IEnumerator enableMenu() {
        while (GOIConstants.canvas == null) {
            yield return null;
        }

        settingsContainer.SetActive(true);
        GOIConstants.canvas.transform.Find("InGame Menu").Find("Panel").gameObject.SetActive(true);
    }*/
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode) {
        if (scene.name == "Mian") {
            ChangeGameState(GameState.InGame, scene);
            Logger.LogInfo("GAME STATE CHANGE mian");
        } else if (scene.name == "Loader") {
            ChangeGameState(GameState.MainMenu, scene);
        } else if (scene.name == "Reward Loader") {
            ChangeGameState(GameState.RewardMenu, scene);
        }
    }

    private void ToggleModsContainer() {
        if (GOIConstants.gameState == GameState.InGame) {
            if (_settingsContainer.activeSelf) { // show mods
                _settingsContainer.SetActive(false);
                _modSettingsContainer.SetActive(true);
            } else { // show normal settings
                _settingsContainer.SetActive(true);
                _modSettingsContainer.SetActive(false);
            }
        } else {
            if (!modLoaderPanel.activeSelf) { // show mods
                modLoaderPanel.SetActive(true);
            } else { // hide mods
                modLoaderPanel.SetActive(false);
            }
        }
    }


    // goi menu functions
    private GameObject CreateButton(string text, Transform parent, Vector2 anchorPos) {
        // create button object
        GameObject newButton = UnityEngine.Object.Instantiate(GOIConstants.settingsManager.applyResolutionButton.gameObject, parent, false);
        newButton.SetActive(true);
        newButton.name = text + "Button";

        // remove apply button event
        Button newButtonObject = newButton.GetComponent<Button>();
        newButtonObject.onClick = new Button.ButtonClickedEvent();

        // set text
        GameObject textObject = newButton.transform.Find("Text").gameObject;
        Localize localize = textObject.GetComponent<Localize>();
        if (localize != null) UnityEngine.Object.Destroy(localize);
        textObject.GetComponent<TextMeshProUGUI>().SetText(text);

        // set pos
        RectTransform buttonRect = newButton.GetComponent<RectTransform>();
        buttonRect.anchoredPosition = anchorPos;

        return newButton;
    }
    private GameObject CreateMenuButton(string text, Transform parent, int siblingIndex=-1) { // maybe make a separate plugin
        // create button
        GameObject menuButtonObject = UnityEngine.Object.Instantiate(GOIConstants.loader.menu.Find("Credits").gameObject, parent, false);
        if (siblingIndex != -1) menuButtonObject.transform.SetSiblingIndex(siblingIndex);

        // remove click event
        Button menuButton = menuButtonObject.GetComponent<Button>();
        menuButton.onClick = new Button.ButtonClickedEvent();

        // set text
        GameObject textObject = menuButton.transform.Find("Text").gameObject;
        TextMeshProUGUI textComponent = textObject.GetComponent<TextMeshProUGUI>();
        Localize localize = textObject.GetComponent<Localize>();
        if (localize != null) UnityEngine.Object.Destroy(localize);
        textComponent.SetText(text);

        return menuButtonObject;
    }
    public void ReplaceFontsInObject(GameObject o) {
        if (_nowayFont == null) Logger.LogWarning("Menu font is null");
        var textComponents = o.GetComponentsInChildren<TextMeshProUGUI>(true);

        foreach (var tmp in textComponents) {
            tmp.font = _nowayFont;
            tmp.fontMaterial = _nowayFont.material;
            tmp.UpdateFontAsset();
        }
    }
    private GameObject CreateModMenu() {
        // copy panel
        GameObject modLoaderPanel = Instantiate(modLoaderPanelPrefab);
        // move
        RectTransform rect = modLoaderPanel.GetComponent<RectTransform>();
        rect.anchoredPosition = new Vector2(-140f, 50f);
        // replace fonts
        ReplaceFontsInObject(modLoaderPanel);
        return modLoaderPanel;
    }
    private void AddModToModMenu(FPMod mod) {
        // add to left panel
        var pluginAttr = mod.GetType().GetCustomAttribute<BepInPlugin>();
        Transform modRows = modLoaderPanel.transform.Find("Panel").Find("Mod List").Find("Viewport").Find("Content").Find("Mod List Panel").Find("Rows");
        GameObject modRowObject = Instantiate(templateModRowPrefab);
        ReplaceFontsInObject(modRowObject);
        TextMeshProUGUI modRowText = modRowObject.transform.Find("Left").Find("Noway-Regular").GetComponent<TextMeshProUGUI>();
        modRowText.text = pluginAttr.Name;
        modRowObject.name = pluginAttr.GUID;
        // set toggle to enable and disable mod
        Toggle modRowToggle = modRowObject.transform.Find("Right").Find("TemplateCheckbox").GetComponent<Toggle>();
        modRowToggle.isOn = _enabledModConfigs[mod.Info.Metadata.GUID].Value;
        modRowToggle.onValueChanged.AddListener((bool isOn) => {
            if (isOn) EnableMod(mod);
            else DisableMod(mod);
        });
        // add mod row with checkbox
        modRowObject.transform.SetParent(modRows, false);

        // generate settings object
        GameObject configEditor = mod.GetConfigEditor();
        if (configEditor != null) {
            configEditor.name = pluginAttr.GUID;
            configEditor.transform.SetParent(modLoaderPanel.transform.Find("Panel").Find("Mod Settings"), false);
            configEditor.SetActive(false);
        } else { // config editor not provided, generate from BepInEx config
            GameObject settingsPanel = Instantiate(templateSettingsContainerPrefab);
            settingsPanel.name = pluginAttr.GUID;


            // add settings to right panel
            var fields = mod.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Where(f =>
                f.FieldType == typeof(ConfigEntry<bool>) ||
                f.FieldType == typeof(ConfigEntry<float>) ||
                f.FieldType == typeof(ConfigEntry<string>) ||
                f.FieldType == typeof(ConfigEntry<int>));
            foreach (var field in fields) {
                var fieldValue = field.GetValue(mod);
                if (fieldValue == null) continue;

                // create setting row
                GameObject settingRow = Instantiate(templateSettingRowPrefab);
                // set text
                TextMeshProUGUI labelText = settingRow.transform.Find("Left").Find("Noway-Regular").GetComponent<TextMeshProUGUI>();

                if (field.FieldType == typeof(ConfigEntry<bool>)) { // bool - checkbox
                    var configEntry = fieldValue as ConfigEntry<bool>;
                    labelText.text = configEntry.Definition.Key;

                    GameObject checkbox = Instantiate(templateCheckboxPrefab);
                    checkbox.transform.SetParent(settingRow.transform.Find("Right"), false);
                    Toggle toggle = checkbox.GetComponent<Toggle>();

                    toggle.isOn = configEntry.Value;
                    toggle.onValueChanged.AddListener((bool value) => {
                        configEntry.Value = value;
                        mod.Config.Save();
                    });
                } else if (field.FieldType == typeof(ConfigEntry<float>)) { // float - text box
                    var configEntry = fieldValue as ConfigEntry<float>;
                    labelText.text = configEntry.Definition.Key;


                    GameObject inputObject = Instantiate(templateInputFieldPrefab);
                    inputObject.transform.SetParent(settingRow.transform.Find("Right"), false);
                    TMP_InputField inputField = inputObject.GetComponent<TMP_InputField>();
                    Image inputFieldImage = inputObject.GetComponent<Image>();

                    inputField.text = configEntry.Value.ToString();
                    inputField.onValueChanged.AddListener((string value) => {
                        float parsedValue = 0f;
                        if (float.TryParse(value, out parsedValue)) {
                            configEntry.Value = parsedValue;
                            mod.Config.Save();
                            inputFieldImage.color = new Color(1f, 1f, 1f, 1f);
                        } else {
                            inputFieldImage.color = new Color(1f, 0.6f, 0.6f, 1f);
                        }
                    });
                } else if (field.FieldType == typeof(ConfigEntry<string>)) { // string - text box
                    var configEntry = fieldValue as ConfigEntry<string>;
                    labelText.text = configEntry.Definition.Key;

                    GameObject inputObject = Instantiate(templateInputFieldPrefab);
                    inputObject.transform.SetParent(settingRow.transform.Find("Right"), false);
                    TMP_InputField inputField = inputObject.GetComponent<TMP_InputField>();
                    Image inputFieldImage = inputObject.GetComponent<Image>();

                    inputField.text = configEntry.Value;
                    inputField.onValueChanged.AddListener((string value) => {
                        configEntry.Value = value;
                        mod.Config.Save();
                    });
                } else if (field.FieldType == typeof(ConfigEntry<int>)) { // int - text box
                    var configEntry = fieldValue as ConfigEntry<int>;
                    labelText.text = configEntry.Definition.Key;

                    GameObject inputObject = Instantiate(templateInputFieldPrefab);
                    inputObject.transform.SetParent(settingRow.transform.Find("Right"), false);
                    TMP_InputField inputField = inputObject.GetComponent<TMP_InputField>();
                    Image inputFieldImage = inputObject.GetComponent<Image>();

                    inputField.text = configEntry.Value.ToString();
                    inputField.onValueChanged.AddListener((string value) => {
                        int parsedValue = 0;
                        if (int.TryParse(value, out parsedValue)) {
                            configEntry.Value = parsedValue;
                            mod.Config.Save();
                            inputFieldImage.color = new Color(1f, 1f, 1f, 1f);
                        } else {
                            inputFieldImage.color = new Color(1f, 0.6f, 0.6f, 1f);
                        }
                    });
                }
                ReplaceFontsInObject(settingRow);

                settingRow.transform.SetParent(settingsPanel.transform.Find("Viewport").Find("Content"), false);
            }
            // add settings to right panel
            settingsPanel.transform.SetParent(modLoaderPanel.transform.Find("Panel").Find("Mod Settings"), false);
            settingsPanel.SetActive(false);
        }
        
        // add click event on mod row (searches for object with GUID name)
        Button rowButton = modRowObject.transform.Find("Left").GetComponent<Button>();
        rowButton.onClick.AddListener(() => {
            Transform modSettingsContainer = modLoaderPanel.transform.Find("Panel").Find("Mod Settings");
            foreach (Transform child in modSettingsContainer) {
                child.gameObject.SetActive(child.name == pluginAttr.GUID);
            }
        });
    }
}




// dont do spline if no spline
[HarmonyPatch(typeof(PoseControl), "Awake")]
public static class PoseControlAwakePatch {
    private static readonly AccessTools.FieldRef<PoseControl, Transform> lookOverrideRef = AccessTools.FieldRefAccess<PoseControl, Transform>("lookOverride");
    private static readonly AccessTools.FieldRef<PoseControl, Transform[]> interestingItemsRef = AccessTools.FieldRefAccess<PoseControl, Transform[]>("interestingItems");
    private static readonly AccessTools.FieldRef<PoseControl, Vector3[]> controlPointsRef = AccessTools.FieldRefAccess<PoseControl, Vector3[]>("controlPoints");

    private static MethodInfo lateUpdateMethod = typeof(PoseControl).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    [HarmonyPrefix]
    public static bool Prefix(PoseControl __instance) {
        var t = Traverse.Create(__instance);

        lookOverrideRef(__instance) = null;
        t.Field("timeSinceOverride").SetValue(0f);
        t.Field("lookVel").SetValue(Vector3.zero);
        interestingItemsRef(__instance) = new Transform[__instance.interestingItemParent.childCount];
        for (int i = 0; i < __instance.interestingItemParent.childCount; i++) {
            interestingItemsRef(__instance)[i] = __instance.interestingItemParent.GetChild(i);
        }
        __instance.handBlend = 1f;
        t.Field("leftHandReset").SetValue(new Vector3(0f, 0.522f, 0f));
        t.Field("rightHandReset").SetValue(new Vector3(0f, 0.522f, 0f));
        t.Field("leftGripCenterReset").SetValue(new Vector3(0f, 0.182f, 0.09f));
        t.Field("rightGripCenterReset").SetValue(new Vector3(0f, 0.171f, 0.063f));
        t.Field("leftForearmLength").SetValue(__instance.leftHand.localPosition.magnitude);
        t.Field("rightForearmLength").SetValue(__instance.rightHand.localPosition.magnitude);
        if (__instance.spline != null) {
            controlPointsRef(__instance) = new Vector3[__instance.spline.ControlPointCount];
            for (int j = 0; j < __instance.spline.ControlPointCount; j++) {
                controlPointsRef(__instance)[j] = __instance.spline.ControlPoints[j].position;
            }
        }
        lateUpdateMethod.Invoke(__instance, null);
        lateUpdateMethod.Invoke(__instance, null);

        return false;
    }
}
// return 0 for GetNearestSplineZ if no spline
[HarmonyPatch(typeof(PoseControl), "GetNearestSplineZ")]
public static class SplineZPatch {
    private static readonly AccessTools.FieldRef<PoseControl, Vector3[]> controlPointsRef = AccessTools.FieldRefAccess<PoseControl, Vector3[]>("controlPoints");

    [HarmonyPrefix]
    public static bool Prefix(PoseControl __instance, ref float __result) {
        if (__instance.spline == null || controlPointsRef(__instance).Length <= 0) {
            __result = 0f;
            return false;
        } else {
            return true;
        }
    }
};
[HarmonyPatch(typeof(FogControl), "Update")]
public static class FogControlUpdatePatch {
    [HarmonyPrefix]
    public static bool Prefix(FogControl __instance) {
        if (__instance.colorSets.Length == 0) {
            return false;
        } else {
            return true;
        }
    }
}

// mian fix
[HarmonyPatch(typeof(SettingsManager), "Start")]
public static class SettingsManagerStartPatch {
    // access private fields
    private static readonly AccessTools.FieldRef<SettingsManager, string[]> qualityNamesRef = AccessTools.FieldRefAccess<SettingsManager, string[]>("qualityNames");
    private static MethodInfo SetPostFXQualityMethod = typeof(SettingsManager).GetMethod("SetPostFXQuality", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    private static MethodInfo LoadResolutionMethod = typeof(SettingsManager).GetMethod("LoadResolution", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    [HarmonyPrefix]
    public static bool Prefix(SettingsManager __instance) {
        var t = Traverse.Create(__instance);

        // set pot goldness
        int wins = PlayerPrefs.GetInt("NumWins");
        PlayerPrefs.SetInt("StoredStatsSinceLastPlay", 0);
        //if (Application.isEditor) wins = 0;
        if (GOIConstants.gameState == GameState.InGame && (bool)__instance.potMat) {
            float goldness = (float)Mathf.Min(wins, 50) / 50f;
            goldness *= goldness;
            __instance.potMat.SetFloat("_Goldness", goldness);
        }
        // enable menu objects
        if (GOIConstants.gameState == GameState.InGame) {
            __instance.menu.SetActive(value: true);
        } else {
            foreach (Transform item in __instance.menu.transform) {
                item.gameObject.SetActive(value: true);
            }
        }
        PlayerPrefs.SetInt("NativeWidth", Screen.currentResolution.width);
        PlayerPrefs.SetInt("NativeHeight", Screen.currentResolution.height);
        PlayerPrefs.SetInt("NativeRefresh", Screen.currentResolution.refreshRate);
        __instance.canMenu = true;
        Debug.Log("detected system language as :" + Application.systemLanguage);
        t.Field("currentLanguageNum").SetValue(-1);
        int langIndex = PlayerPrefs.GetInt("Language", -1);
        if (langIndex < 0) {
            switch (Application.systemLanguage) {
                case SystemLanguage.English:
                    Debug.Log("successfully detected english");
                    langIndex = 0;
                    break;
                case SystemLanguage.Russian:
                    langIndex = 1;
                    break;
                case SystemLanguage.Japanese:
                    langIndex = 2;
                    Debug.Log("Detected language as Japanese, weirdly");
                    break;
                case SystemLanguage.ChineseSimplified:
                    langIndex = 3;
                    break;
                case SystemLanguage.ChineseTraditional:
                    langIndex = 3;
                    break;
                case SystemLanguage.Chinese:
                    langIndex = 3;
                    break;
                case SystemLanguage.Korean:
                    langIndex = 4;
                    break;
                default:
                    langIndex = 0;
                    break;
            }
        }
        __instance.SetLanguage(langIndex);
        __instance.OnLanguageChanged(langIndex);
        if (__instance.languageToggles[0] != null) {
            for (int i = 0; i < __instance.languageToggles.Length; i++) {
                if (i != langIndex) {
                    __instance.languageToggles[i].isOn = false;
                }
            }
            __instance.languageToggles[langIndex].isOn = true;
        }
        if (__instance.motionBlurToggle == null) {
            return false;
        }
        if (PlayerPrefs.HasKey("MotionBlur")) {
            __instance.motionBlurToggle.isOn = PlayerPrefs.GetInt("MotionBlur") == 1;
            __instance.ToggleMotionBlur(__instance.motionBlurToggle.isOn);
        } else {
            PlayerPrefs.SetInt("MotionBlur", 1);
            __instance.motionBlurToggle.isOn = true;
            __instance.ToggleMotionBlur(__instance.motionBlurToggle.isOn);
        }
        if (PlayerPrefs.HasKey("CursorOn")) {
            __instance.cursorToggle.isOn = PlayerPrefs.GetInt("CursorOn") == 1;
            __instance.ToggleCursor();
        } else {
            PlayerPrefs.SetInt("CursorOn", 1);
            __instance.cursorToggle.isOn = true;
            __instance.ToggleCursor();
        }
        if (PlayerPrefs.HasKey("Trackpad")) {
            bool isOn = PlayerPrefs.GetInt("Trackpad") == 1;
            __instance.trackpadToggle.isOn = isOn;
        } else {
            PlayerPrefs.SetInt("Trackpad", 0);
            __instance.trackpadToggle.isOn = false;
        }
        if (PlayerPrefs.HasKey("MouseSensitivity")) {
            float sensitivity = PlayerPrefs.GetFloat("MouseSensitivity");
            sensitivity = Mathf.Clamp(sensitivity, 0.1f, 2.4f);
            __instance.SetMouseSensitivity(sensitivity);
            __instance.mouseSensitivitySlider.value = sensitivity;
            t.Field("mouseSensitivity").SetValue(sensitivity);
        } else {
            float sensitivity = 1f;
            if (__instance.trackpadToggle != null && !__instance.trackpadToggle.isOn) {
                sensitivity = 1.5f;
            }
            PlayerPrefs.SetFloat("MouseSensitivity", sensitivity);
            __instance.SetMouseSensitivity(sensitivity);
            __instance.mouseSensitivitySlider.value = sensitivity;
            t.Field("mouseSensitivity").SetValue(sensitivity);
        }
        if (PlayerPrefs.HasKey("SubtitlesOn")) {
            __instance.subtitleToggle.isOn = PlayerPrefs.GetInt("SubtitlesOn") == 1;
            __instance.ToggleSubtitles();
        } else {
            PlayerPrefs.SetInt("SubtitlesOn", 1);
            __instance.subtitleToggle.isOn = true;
            __instance.ToggleSubtitles();
        }
        if (PlayerPrefs.HasKey("SFXVolume")) {
            __instance.mixer.SetFloat("SFXVol", PlayerPrefs.GetFloat("SFXVolume"));
            __instance.mixer.SetFloat("AmbienceVol", PlayerPrefs.GetFloat("SFXVolume"));
            __instance.SFXVolumeSlider.value = PlayerPrefs.GetFloat("SFXVolume");
        } else {
            PlayerPrefs.SetFloat("SFXVolume", __instance.SFXVolumeSlider.value);
        }
        if (PlayerPrefs.HasKey("MusicVolume")) {
            __instance.mixer.SetFloat("MusicVol", PlayerPrefs.GetFloat("MusicVolume") * 0.9f);
            __instance.MusicVolumeSlider.value = PlayerPrefs.GetFloat("MusicVolume");
        } else {
            PlayerPrefs.SetFloat("MusicVolume", __instance.MusicVolumeSlider.value);
        }
        if (PlayerPrefs.HasKey("VoiceVolume")) {
            __instance.mixer.SetFloat("VoiceVol", PlayerPrefs.GetFloat("VoiceVolume") * 0.8f);
            __instance.VOVolumeSlider.value = PlayerPrefs.GetFloat("VoiceVolume");
        } else {
            PlayerPrefs.SetFloat("VoiceVolume", __instance.VOVolumeSlider.value);
        }
        if (__instance.resolutionDropdown != null) {
            LoadResolutionMethod.Invoke(__instance, null);
        }
        if (PlayerPrefs.HasKey("Quality")) {
            int quality = PlayerPrefs.GetInt("Quality");

            QualitySettings.SetQualityLevel(quality);
            __instance.currentQualityText.text = qualityNamesRef(__instance)[quality];
            SetFogQuality[] array = UnityEngine.Object.FindObjectsOfType<SetFogQuality>();
            for (int j = 0; j < array.Length; j++) {
                array[j].SetQuality(quality);
            }
            SetPostFXQualityMethod.Invoke(__instance, new object[] { quality });
        } else {
            QualitySettings.SetQualityLevel(4);
            PlayerPrefs.SetInt("Quality", 4);
            __instance.currentQualityText.text = qualityNamesRef(__instance)[4];
            SetPostFXQualityMethod.Invoke(__instance, new object[] { 4 });
        }
        if (PlayerPrefs.HasKey("Vsync")) {
            QualitySettings.vSyncCount = PlayerPrefs.GetInt("Vsync");
        }
        if (GOIConstants.gameState == GameState.InGame) {
            __instance.menu.SetActive(value: false);
        } else {
            foreach (Transform menuItem in __instance.menu.transform) {
                menuItem.gameObject.SetActive(value: false);
            }
        }
        PlayerPrefs.Save();

        return false;
    }
}

// check if water isnt null when fixedupdate camera
[HarmonyPatch(typeof(CameraControl), "FixedUpdate")]
public static class CameraControlFixedUpdatePatch {
    // access private fields
    private static readonly AccessTools.FieldRef<CameraControl, Vector3> velRef			 = AccessTools.FieldRefAccess<CameraControl, Vector3>("vel");
    private static readonly AccessTools.FieldRef<CameraControl, Vector3> targetRef		  = AccessTools.FieldRefAccess<CameraControl, Vector3>("target");
    private static readonly AccessTools.FieldRef<CameraControl, float> waterLevelRef		= AccessTools.FieldRefAccess<CameraControl, float>("waterLevel");
    private static readonly AccessTools.FieldRef<CameraControl, bool> loadFinishedRef	   = AccessTools.FieldRefAccess<CameraControl, bool>("loadFinished");
    private static readonly AccessTools.FieldRef<CameraControl, Camera> mainCamRef		  = AccessTools.FieldRefAccess<CameraControl, Camera>("mainCam");
    private static readonly AccessTools.FieldRef<CameraControl, Vector3> lookaheadPosRef	= AccessTools.FieldRefAccess<CameraControl, Vector3>("lookaheadPos");
    private static readonly AccessTools.FieldRef<CameraControl, float> lastTFRef			= AccessTools.FieldRefAccess<CameraControl, float>("lastTF");

    [HarmonyPrefix]
    public static bool Prefix(CameraControl __instance) {
        if (loadFinishedRef(__instance) && Application.isPlaying) {
            if (__instance.player == null) {
                __instance.player = GOIConstants.player;
            }

            Vector3 vector = Vector3.zero;

            if (__instance.spline != null && __instance.progressMeter != null) {
                float lastTF = lastTFRef(__instance);
                lastTF = Mathf.Lerp(lastTF, __instance.progressMeter.currentTF, 0.3f);
                lastTFRef(__instance) = lastTF;

                // Sample spline positions
                Vector3 v1 = __instance.spline.Interpolate(lastTF);
                Vector3 v2 = __instance.spline.Interpolate(lastTF + 0.01f);
                Vector3 v3 = __instance.spline.Interpolate(lastTF + 0.02f);
                Vector3 v4 = __instance.spline.Interpolate(lastTF + 0.03f);

                Vector3 avg = 0.3333f * (v2 + v3 + v4);

                Vector3 lookaheadPos = lookaheadPosRef(__instance);
                lookaheadPos = Vector3.Lerp(lookaheadPos, avg, 0.3f);
                lookaheadPosRef(__instance) = lookaheadPos;

                vector = lookaheadPos - __instance.player.transform.position;
                vector.z = 0f;
            }

            Vector3 target = __instance.player.transform.position + vector.normalized * 2f;

            Camera mainCam = mainCamRef(__instance);
            float waterLevel = waterLevelRef(__instance);

            if (__instance.water != null && mainCam != null) {
                target.y = Mathf.Max(waterLevel + mainCam.orthographicSize - 2f, target.y);
            }

            target.z = -20f;

            Vector3 jitter = new Vector3(
                0.001f * Mathf.Sin(Time.time),
                                         0.001f * Mathf.Sin(Time.time),
                                         0f
            );
            target += jitter;

            Vector3 vel = velRef(__instance);
            Vector3 delta = target - __instance.transform.position;
            vel += 60f * delta * Time.fixedDeltaTime - 0.12f * vel;

            __instance.transform.position += vel * Time.fixedDeltaTime;

            // Save updated fields
            velRef(__instance) = vel;
            targetRef(__instance) = target;
        }

        return false;
    }
}
// fix else statement in togglemenu
[HarmonyPatch(typeof(SettingsManager), "ToggleMenu")]
public static class SettingsManagerToggleMenuPatch {
    // access private fields
    private static readonly AccessTools.FieldRef<SettingsManager, bool> canMenuRef					= AccessTools.FieldRefAccess<SettingsManager, bool>("canMenu");
    private static readonly AccessTools.FieldRef<SettingsManager, GameObject> menuRef				 = AccessTools.FieldRefAccess<SettingsManager, GameObject>("menu");
    private static readonly AccessTools.FieldRef<SettingsManager, TMP_Dropdown> resolutionDropdownRef = AccessTools.FieldRefAccess<SettingsManager, TMP_Dropdown>("resolutionDropdown");
    private static readonly AccessTools.FieldRef<SettingsManager, GameObject> narratorRef			 = AccessTools.FieldRefAccess<SettingsManager, GameObject>("narrator");
    private static readonly AccessTools.FieldRef<SettingsManager, GameObject> playerRef			   = AccessTools.FieldRefAccess<SettingsManager, GameObject>("player");

    [HarmonyPrefix]
    public static bool Prefix(SettingsManager __instance) {
        if (canMenuRef(__instance)) {
            menuRef(__instance).SetActive(!menuRef(__instance).activeSelf);
            Cursor.lockState = ((!menuRef(__instance).activeSelf) ? CursorLockMode.Locked : CursorLockMode.None);
            Cursor.visible = menuRef(__instance).activeSelf;
            //Time.timeScale = (menuRef(__instance).activeSelf ? 0f : 1f);
            GOIConstants.ModifierSetEnabled<float>("timeScale", "pause", menuRef(__instance).activeSelf);
            resolutionDropdownRef(__instance).Hide();
            if (narratorRef(__instance) != null) {
                if (menuRef(__instance).activeSelf) {
                    narratorRef(__instance).SendMessage("Pause");
                } else {
                    narratorRef(__instance).SendMessage("UnPause");
                }
            }
            if (playerRef(__instance) != null) {
                if (menuRef(__instance).activeSelf) {
                    playerRef(__instance).SendMessage("Pause");
                } else {
                    playerRef(__instance).SendMessage("UnPause");
                }
            }
        }

        return false;
    }
}
