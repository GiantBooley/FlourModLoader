using System.Collections.Generic;
using UnityEngine;
using System;

namespace FlourModLoader;

public enum GameState {
    InGame, MainMenu, RewardMenu, Credits
};

public class LambdaModifier<T> {
    public Func<T, T> function;
    public int sortingIndex;
    public string name;
    public bool enabled;

    public LambdaModifier(Func<T, T> func, int sortingInd, string nam, bool enable = true) {
        function = func;
        sortingIndex = sortingInd;
        name = nam;
        enabled = enable;
    }
}
public class ModifiedConstant {

}
public class ModifiedConstant<T> : ModifiedConstant {
    public List<LambdaModifier<T>> modifiers = new List<LambdaModifier<T>>();
    private T _initialValue;
    public Action<T> onChanged;

    public bool dontUseCachedValue;
    private T _cachedValue;
    private bool _dirty;

    public void Update() { // for continuous value: call update in monobehaviour.update() or whenever dependency value changes
        _dirty = true;
        onChanged.Invoke(GetValue());
    }
    public T GetValue() {
        if (_dirty || dontUseCachedValue) {
            T value = _initialValue;
            foreach (LambdaModifier<T> modifier in modifiers) {
                if (modifier.enabled) value = modifier.function(value);
            }
            _cachedValue = value;
            return value;
        } else {
            return _cachedValue;
        }
    }
    public void AddModifier(LambdaModifier<T> modifier) {
        int indexToAdd = -1;

        for (int i = modifiers.Count - 1; i >= 0; i--) {
            if (modifiers[i].name == modifier.name) return;
            if (indexToAdd == -1 && modifiers[i].sortingIndex <= modifier.sortingIndex) {
                indexToAdd = i + 1;
            }
        }
        if (indexToAdd == -1) indexToAdd = 0;
        modifiers.Insert(indexToAdd, modifier);
        Update();
    }
    public void RemoveModifier(LambdaModifier<T> modifier) {
        modifiers.Remove(modifier);
        Update();
    }
    public void RemoveModifier(string name) {
        for (int i = 0; i < modifiers.Count; i++) {
            if (modifiers[i].name == name) {
                modifiers.RemoveAt(i);
                break;
            }
        }
        Update();
    }
    public void ModifierSetEnabled(LambdaModifier<T> modifier, bool enabled) {
        for (int i = 0; i < modifiers.Count; i++) {
            if (modifiers[i] == modifier) {
                modifiers[i].enabled = enabled;
                break;
            }
        }
        Update();
    }
    public void ModifierSetEnabled(string name, bool enabled) {
        for (int i = 0; i < modifiers.Count; i++) {
            if (modifiers[i].name == name) {
                modifiers[i].enabled = enabled;
                break;
            }
        }
        Update();
    }
    public ModifiedConstant(T initial, Action<T> onChangedAction) {
        _initialValue = initial;
        onChanged = onChangedAction;
        _dirty = true;
        dontUseCachedValue = false;
    }
}

public static class GOIConstants {
    public static float timer;
    public static GameState gameState;

    public static GameObject player;
    public static PlayerControl playerCtrl;
    public static GameObject mainCamera;
    public static CameraControl cameraCtrl;

    public static Loader loader;
    public static GameObject canvas;
    public static SettingsManager settingsManager;


    private static readonly Dictionary<string, ModifiedConstant> constants = new Dictionary<string, ModifiedConstant>();

    public static void CreateConstant<T>(string name, T initialValue, Action<T> onChangedAction) {
        if (!constants.ContainsKey(name)) {
            constants.Add(name, new ModifiedConstant<T>(initialValue, onChangedAction));
        }
    }
    public static void RemoveConstant(string name) {
        constants.Remove(name);
    }
    public static T GetConstant<T>(string name) {
        if (constants.TryGetValue(name, out ModifiedConstant mc)) {
            if (mc is ModifiedConstant<T> typedConstant) {
                return typedConstant.GetValue();
            }
        }

        Debug.LogWarning($"Constant \"{name}\" not found");
        return default(T);
    }

    public static void AddModifier<T>(string name, LambdaModifier<T> modifier) {
        if (constants.TryGetValue(name, out ModifiedConstant mc)) {
            if (mc is ModifiedConstant<T> typedConstant) {
                typedConstant.AddModifier(modifier);
                return;
            }
        }
        Debug.LogWarning($"Constant \"{name}\" not found");
    }
    public static void RemoveModifier<T>(string name, LambdaModifier<T> modifier) {
        if (constants.TryGetValue(name, out ModifiedConstant mc)) {
            if (mc is ModifiedConstant<T> typedConstant) {
                typedConstant.RemoveModifier(modifier);
                return;
            }
        }
        Debug.LogWarning($"Constant \"{name}\" not found");
    }
    public static void RemoveModifier<T>(string name, string modifierName) {
        if (constants.TryGetValue(name, out ModifiedConstant mc)) {
            if (mc is ModifiedConstant<T> typedConstant) {
                typedConstant.RemoveModifier(modifierName);
                return;
            }
        }
        Debug.LogWarning($"Constant \"{name}\" not found");
    }
    public static void ModifierSetEnabled<T>(string name, LambdaModifier<T> modifier, bool enabled) {
        if (constants.TryGetValue(name, out ModifiedConstant mc)) {
            if (mc is ModifiedConstant<T> typedConstant) {
                typedConstant.ModifierSetEnabled(modifier, enabled);
                return;
            }
        }
        Debug.LogWarning($"Constant \"{name}\" not found");
    }
    public static void ModifierSetEnabled<T>(string name, string modifierName, bool enabled) {
        if (constants.TryGetValue(name, out ModifiedConstant mc)) {
            if (mc is ModifiedConstant<T> typedConstant) {
                typedConstant.ModifierSetEnabled(modifierName, enabled);
                return;
            }
        }
        Debug.LogWarning($"Constant \"{name}\" not found");
    }
    public static void UpdateConstant<T>(string name) {
        if (constants.TryGetValue(name, out ModifiedConstant mc)) {
            if (mc is ModifiedConstant<T> typedConstant) {
                typedConstant.Update();
                return;
            }
        }
        Debug.LogWarning($"Constant \"{name}\" not found");
    }
}
