using System;
using System.Collections.Generic;
using CelesteStudio.Util;
using Eto.Forms;
using StudioCommunication;

#if REWRITE

namespace CelesteStudio.Communication;

public sealed class CommunicationWrapper {
    public bool Connected => server.Connected;

    public StudioState State { get; private set; }
    
    public event Action? ConnectionChanged;
    public event Action<StudioState, StudioState>? StateUpdated;
    public event Action<Dictionary<int, string>>? LinesUpdated;
    
    private readonly StudioCommunicationServer server;
    private Dictionary<HotkeyID, List<WinFormsKeys>> bindings = [];
    
    public CommunicationWrapper() {
        server = new StudioCommunicationServer(OnConnectionChanged, OnStateChanged, OnLinesChanged, OnBindingsChanged);
    }
    
    private void OnConnectionChanged() {
        Application.Instance.AsyncInvoke(() => ConnectionChanged?.Invoke());
    }
    private void OnStateChanged(StudioState newState) {
        var prevState = State;
        State = newState;
        Application.Instance.AsyncInvoke(() => StateUpdated?.Invoke(prevState, newState));
    }
    private void OnLinesChanged(Dictionary<int, string> updateLines) {
        Application.Instance.AsyncInvoke(() => LinesUpdated?.Invoke(updateLines));
    }
    private void OnBindingsChanged(Dictionary<HotkeyID, List<WinFormsKeys>> newBindings) {
        bindings = newBindings;
        foreach (var pair in bindings) {
            Console.WriteLine(pair.ToString());
        }
    }
    
    public void ForceReconnect() {
        // stub
    }
    public void SendPath(string path) {
        if (Connected) {
            server.SendPath(path);
        }
    }
    public bool SendKeyEvent(Keys key, Keys modifiers, bool released) {
        var winFormsKey = key.ToWinForms();
        
        foreach (HotkeyID hotkey in bindings.Keys) {
            var bindingKeys = bindings[hotkey];
            if (bindingKeys.Count == 0) continue;
            
            // Require the key without any modifiers (or the modifier being the same as the key)
            if (bindingKeys.Count == 1) {
                if ((bindingKeys[0] == winFormsKey) &&
                    ((modifiers == Keys.None) ||
                     (modifiers == Keys.Shift && key is Keys.Shift or Keys.LeftShift or Keys.RightShift) ||
                     (modifiers == Keys.Control && key is Keys.Control or Keys.LeftControl or Keys.RightControl) ||
                     (modifiers == Keys.Alt && key is Keys.Alt or Keys.LeftAlt or Keys.RightAlt)))
                {
                    if (Connected) {
                        server.SendHotkey(hotkey, released);
                    }
                    return true;
                }
                
                continue;
            }
            
            // Binding has > 1 keys
            foreach (var bind in bindingKeys) {
                if (bind == winFormsKey)
                    continue;
                
                if (bind is WinFormsKeys.Shift or WinFormsKeys.LShiftKey or WinFormsKeys.RShiftKey && modifiers.HasFlag(Keys.Shift))
                    continue;
                if (bind is WinFormsKeys.Control or WinFormsKeys.LControlKey or WinFormsKeys.RControlKey && modifiers.HasFlag(Keys.Control))
                    continue;
                if (bind is WinFormsKeys.Menu or WinFormsKeys.LMenu or WinFormsKeys.RMenu && modifiers.HasFlag(Keys.Alt))
                    continue;
            
                // If only labeled for-loops would exist...
                goto NextIter;
            }
            
            if (Connected) {
                server.SendHotkey(hotkey, released);
            }
            return true;
            
            NextIter:; // Yes, that ";" is required..
        }
        
        return false;
    }
    
    #region Data
    
    public int CurrentLine => Connected ? State.CurrentLine : -1;
    public string CurrentLineSuffix => Connected ? State.CurrentLineSuffix : string.Empty;
    public int CurrentFrameInTas => Connected ? State.CurrentFrameInTas : -1;
    public int TotalFrames => Connected ? State.TotalFrames : -1;
    public int SaveStateLine => Connected ? State.SaveStateLine : -1;
    public States TasStates => Connected ? (States) State.tasStates : States.None;
    public string GameInfo => Connected ? State.GameInfo : string.Empty;
    public string LevelName => Connected ? State.LevelName : string.Empty;
    public string ChapterTime => Connected ? State.ChapterTime : string.Empty;
    
    public string GetConsoleCommand(bool simple) {
        return server.RequestGameData(GameDataType.ConsoleCommand, simple).Result ?? string.Empty;
    }
    public string GetModURL() {
        return server.RequestGameData(GameDataType.ModUrl).Result ?? string.Empty;
    }
    public string GetModInfo() {
        return server.RequestGameData(GameDataType.ModInfo).Result ?? string.Empty;
    }
    public string GetExactGameInfo() {
        return server.RequestGameData(GameDataType.ExactGameInfo).Result ?? string.Empty;
    }
    
    #endregion
    
    #region Actions
    
    public void CopyCustomInfoTemplateToClipboard() {
        // stub
    }
    public void SetCustomInfoTemplateFromClipboard() {
        // stub
    }
    public void ClearCustomInfoTemplate() {
        // stub
    }
    public void ClearWatchEntityInfo() {
        // stub
    }
    
    public void RecordTAS(string fileName) {
        // stub
    }
    
    #endregion
    
    #region Settings
    
    private const int DefaultDecimals = 2;
    private const int DefaultFastForwardSpeed = 10;
    private const float DefaultSlowForwardSpeed = 0.1f;
    
    private void SetSetting(string settingName, object? value) {
        if (Connected) {
            server.SendSetting(settingName, value);
        }
    }

    private bool GetBool(string settingName) {
        if (server.RequestGameData(GameDataType.SettingValue, settingName).Result is { } settingValue &&
            bool.TryParse(settingValue, out bool value))
        {
            return value;
        }
        
        return false;
    }
    private int GetInt(string settingName, int defaultValue) {
        if (server.RequestGameData(GameDataType.SettingValue, settingName).Result is { } settingValue &&
            int.TryParse(settingValue, out int value)) 
        {
            return value;
        }
        
        return defaultValue;
    }
    private float GetFloat(string settingName, float defaultValue) {
        if (server.RequestGameData(GameDataType.SettingValue, settingName).Result is { } settingValue &&
            float.TryParse(settingValue, out float value))
        {
            return value;
        }
        
        return defaultValue;
    }
    
    public bool GetHitboxes() => GetBool("ShowHitboxes");
    public bool GetTriggerHitboxes() => GetBool("ShowTriggerHitboxes");
    public bool GetUnloadedRoomsHitboxes() => GetBool("ShowUnloadedRoomsHitboxes");
    public bool GetCameraHitboxes() => GetBool("ShowCameraHitboxes");
    public bool GetSimplifiedHitboxes() => GetBool("SimplifiedHitboxes");
    public bool GetActualCollideHitboxes() => GetBool("ShowActualCollideHitboxes");
    public bool GetSimplifiedGraphics() => GetBool("SimplifiedGraphics");
    public bool GetGameplay() => GetBool("ShowGameplay");
    public bool GetCenterCamera() => GetBool("CenterCamera");
    public bool GetCenterCameraHorizontallyOnly() => GetBool("CenterCameraHorizontallyOnly");
    public bool GetInfoHud() => GetBool("InfoHud");
    public bool GetInfoTasInput() => GetBool("InfoTasInput");
    public bool GetInfoGame() => GetBool("InfoGame");
    public bool GetInfoWatchEntity() => GetBool("InfoWatchEntity");
    public bool GetInfoCustom() => GetBool("InfoCustom");
    public bool GetInfoSubpixelIndicator() => GetBool("InfoSubpixelIndicator");
    public bool GetSpeedUnit() => GetBool("SpeedUnit");
    
    public void ToggleHitboxes() => SetSetting("ShowHitboxes", null);
    public void ToggleTriggerHitboxes() => SetSetting("ShowTriggerHitboxes", null);
    public void ToggleUnloadedRoomsHitboxes() => SetSetting("ShowUnloadedRoomsHitboxes", null);
    public void ToggleCameraHitboxes() => SetSetting("ShowCameraHitboxes", null);
    public void ToggleSimplifiedHitboxes() => SetSetting("SimplifiedHitboxes", null);
    public void ToggleActualCollideHitboxes() => SetSetting("ShowActualCollideHitboxes", null);
    public void ToggleSimplifiedGraphics() => SetSetting("SimplifiedGraphics", null);
    public void ToggleGameplay() => SetSetting("ShowGameplay", null);
    public void ToggleCenterCamera() => SetSetting("CenterCamera", null);
    public void ToggleCenterCameraHorizontallyOnly() => SetSetting("CenterCameraHorizontallyOnly", null);
    public void ToggleInfoHud() => SetSetting("InfoHud", null);
    public void ToggleInfoTasInput() => SetSetting("InfoTasInput", null);
    public void ToggleInfoGame() => SetSetting("InfoGame", null);
    public void ToggleInfoWatchEntity() => SetSetting("InfoWatchEntity", null);
    public void ToggleInfoCustom() => SetSetting("InfoCustom", null);
    public void ToggleInfoSubpixelIndicator() => SetSetting("InfoSubpixelIndicator", null);
    public void ToggleSpeedUnit() => SetSetting("SpeedUnit", null);
    
    public int GetPositionDecimals() => GetInt("PositionDecimals", DefaultDecimals);
    public void SetPositionDecimals(int value) => SetSetting("PositionDecimals", value);

    public int GetSpeedDecimals() => GetInt("SpeedDecimals", DefaultDecimals);
    public void SetSpeedDecimals(int value) => SetSetting("SpeedDecimals", value);

    public int GetVelocityDecimals() => GetInt("VelocityDecimals", DefaultDecimals);
    public void SetVelocityDecimals(int value) => SetSetting("VelocityDecimals", value);
    
    public int GetAngleDecimals() => GetInt("AngleDecimals", DefaultDecimals);
    public void SetAngleDecimals(int value) => SetSetting("AngleDecimals", value);

    public int GetCustomInfoDecimals() => GetInt("CustomInfoDecimals", DefaultDecimals);
    public void SetCustomInfoDecimals(int value) => SetSetting("CustomInfoDecimals", value);

    public int GetSubpixelIndicatorDecimals() => GetInt("SubpixelIndicatorDecimals", DefaultDecimals);
    public void SetSubpixelIndicatorDecimals(int value) => SetSetting("SubpixelIndicatorDecimals", value);

    public int GetFastForwardSpeed() => GetInt("FastForwardSpeed", DefaultFastForwardSpeed);
    public void SetFastForwardSpeed(int value) => SetSetting("FastForwardSpeed", value);

    public float GetSlowForwardSpeed() => GetFloat("SlowForwardSpeed", DefaultSlowForwardSpeed);
    public void SetSlowForwardSpeed(float value) => SetSetting("SlowForwardSpeed", value);
    
    #endregion
}

#else

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using StudioCommunication;
using CelesteStudio.Communication;
using CelesteStudio.Util;
using Eto.Forms;

namespace CelesteStudio.Communication;

public class CommunicationWrapper {
    private Dictionary<HotkeyID, List<WinFormsKeys>> _bindings = new();
    public StudioState State { get; private set; }

    public StudioCommunicationServer Server { get; }

    public CommunicationWrapper() {
        Server = new StudioCommunicationServer();
        Server.BindingsUpdated += bindings => _bindings = bindings;
        Server.StateUpdated += (_, state) => State = state;
        Server.Run();
    }

    public void WriteWait() => Server.WriteWait();
    public void SendPath(string path) => Server.SendPath(path);
    
    // Prevent key-repeat events from being forwarded.
    // If there are too many, the communication will crash
    // TODO: Rewrite the studio communication, accounting for something like this
    private readonly HashSet<HotkeyID> pressedHotkeys = new();
    
    public bool SendKeyEvent(Keys key, Keys modifiers, bool released) {
        var winFormsKey = key.ToWinForms();
        
        foreach (HotkeyID hotkeyIDs in _bindings.Keys) {
            var bindingKeys = _bindings[hotkeyIDs];
            if (bindingKeys.Count == 0) continue;
            
            // Require the key without any modifiers (or the modifier being the same as the key)
            if (bindingKeys.Count == 1) {
                if ((bindingKeys[0] == winFormsKey) &&
                    ((modifiers == Keys.None) ||
                     (modifiers == Keys.Shift && key is Keys.Shift or Keys.LeftShift or Keys.RightShift) ||
                     (modifiers == Keys.Control && key is Keys.Control or Keys.LeftControl or Keys.RightControl) ||
                     (modifiers == Keys.Alt && key is Keys.Alt or Keys.LeftAlt or Keys.RightAlt)))
                {
                    if (!released && pressedHotkeys.Contains(hotkeyIDs)) {
                        return true;
                    }
                    
                    Server.SendHotkeyPressed(hotkeyIDs, released);
                    
                    if (released) {
                        pressedHotkeys.Remove(hotkeyIDs);
                    } else {
                        pressedHotkeys.Add(hotkeyIDs);
                    }

                    return true;
                }
                
                continue;
            }
            
            // Binding has > 1 keys
            foreach (var bind in bindingKeys) {
                if (bind == winFormsKey)
                    continue;
                
                if (bind is WinFormsKeys.Shift or WinFormsKeys.LShiftKey or WinFormsKeys.RShiftKey && modifiers.HasFlag(Keys.Shift))
                    continue;
                if (bind is WinFormsKeys.Control or WinFormsKeys.LControlKey or WinFormsKeys.RControlKey && modifiers.HasFlag(Keys.Control))
                    continue;
                if (bind is WinFormsKeys.Menu or WinFormsKeys.LMenu or WinFormsKeys.RMenu && modifiers.HasFlag(Keys.Alt))
                    continue;
            
                // If only labeled for-loops would exist...
                goto NextIter;
            }
            
            if (!released && pressedHotkeys.Contains(hotkeyIDs)) {
                return true;
            }
            
            Server.SendHotkeyPressed(hotkeyIDs, released);
            
            if (released) {
                pressedHotkeys.Remove(hotkeyIDs);
            } else {
                pressedHotkeys.Add(hotkeyIDs);
            }

            return true;
            
            NextIter:; // Yes, that ";" is required..
        }
        
        return false;
    }


    public void Play() {
    }
    
    public bool Connected => StudioCommunicationBase.Initialized;
    
    public int CurrentLine => Connected ? State.CurrentLine : -1;
    public string CurrentLineSuffix => Connected ? State.CurrentLineSuffix : string.Empty;
    public int CurrentFrameInTas => Connected ? State.CurrentFrameInTas : -1;
    public int TotalFrames => Connected ? State.TotalFrames : -1;
    public int SaveStateLine => Connected ? State.SaveStateLine : -1;
    public States TasStates => Connected ? (States) State.tasStates : States.None;
    public string GameInfo => Connected ? State.GameInfo : string.Empty;
    public string LevelName => Connected ? State.LevelName : string.Empty;
    public string ChapterTime => Connected ? State.ChapterTime : string.Empty;

    private bool GetToggle(string settingName) {
        if (Server.GetDataFromGame(GameDataType.SettingValue, settingName) is { } settingValue &&
            bool.TryParse(settingValue, out var value)) 
        {
            return value;
        }
        
        return false;
    }
    
    public bool GetHitboxes() => GetToggle("ShowHitboxes");
    public bool GetTriggerHitboxes() => GetToggle("ShowTriggerHitboxes");
    public bool GetUnloadedRoomsHitboxes() => GetToggle("ShowUnloadedRoomsHitboxes");
    public bool GetCameraHitboxes() => GetToggle("ShowCameraHitboxes");
    public bool GetSimplifiedHitboxes() => GetToggle("SimplifiedHitboxes");
    public bool GetActualCollideHitboxes() => GetToggle("ShowActualCollideHitboxes");
    public bool GetSimplifiedGraphics() => GetToggle("SimplifiedGraphics");
    public bool GetGameplay() => GetToggle("ShowGameplay");
    public bool GetCenterCamera() => GetToggle("CenterCamera");
    public bool GetCenterCameraHorizontallyOnly() => GetToggle("CenterCameraHorizontallyOnly");
    public bool GetInfoHud() => GetToggle("InfoHud");
    public bool GetInfoTasInput() => GetToggle("InfoTasInput");
    public bool GetInfoGame() => GetToggle("InfoGame");
    public bool GetInfoWatchEntity() => GetToggle("InfoWatchEntity");
    public bool GetInfoCustom() => GetToggle("InfoCustom");
    public bool GetInfoSubpixelIndicator() => GetToggle("InfoSubpixelIndicator");
    public bool GetSpeedUnit() => GetToggle("SpeedUnit");
    
    public void ToggleHitboxes() => Server.ToggleGameSetting("ShowHitboxes", null);
    public void ToggleTriggerHitboxes() => Server.ToggleGameSetting("ShowTriggerHitboxes", null);
    public void ToggleUnloadedRoomsHitboxes() => Server.ToggleGameSetting("ShowUnloadedRoomsHitboxes", null);
    public void ToggleCameraHitboxes() => Server.ToggleGameSetting("ShowCameraHitboxes", null);
    public void ToggleSimplifiedHitboxes() => Server.ToggleGameSetting("SimplifiedHitboxes", null);
    public void ToggleActualCollideHitboxes() => Server.ToggleGameSetting("ShowActualCollideHitboxes", null);
    public void ToggleSimplifiedGraphics() => Server.ToggleGameSetting("SimplifiedGraphics", null);
    public void ToggleGameplay() => Server.ToggleGameSetting("ShowGameplay", null);
    public void ToggleCenterCamera() => Server.ToggleGameSetting("CenterCamera", null);
    public void ToggleCenterCameraHorizontallyOnly() => Server.ToggleGameSetting("CenterCameraHorizontallyOnly", null);
    public void ToggleInfoHud() => Server.ToggleGameSetting("InfoHud", null);
    public void ToggleInfoTasInput() => Server.ToggleGameSetting("InfoTasInput", null);
    public void ToggleInfoGame() => Server.ToggleGameSetting("InfoGame", null);
    public void ToggleInfoWatchEntity() => Server.ToggleGameSetting("InfoWatchEntity", null);
    public void ToggleInfoCustom() => Server.ToggleGameSetting("InfoCustom", null);
    public void ToggleInfoSubpixelIndicator() => Server.ToggleGameSetting("InfoSubpixelIndicator", null);
    public void ToggleSpeedUnit() => Server.ToggleGameSetting("SpeedUnit", null);

    private const int DefaultDecimals = 2;
    private const int DefaultFastForwardSpeed = 10;
    private const float DefaultSlowForwardSpeed = 0.1f;
    
    private int GetDecimals(string settingName) {
        string decimals = DefaultDecimals.ToString();
        if (Server.GetDataFromGame(GameDataType.SettingValue, settingName) is { } settingValue) {
            decimals = settingValue;
        }

        bool success = int.TryParse(decimals, out int result);
        return success ? result : DefaultDecimals;
    }

    public int GetPositionDecimals() => GetDecimals("PositionDecimals");
    public void SetPositionDecimals(int value) => Server.ToggleGameSetting("PositionDecimals", value);

    public int GetSpeedDecimals() => GetDecimals("SpeedDecimals");
    public void SetSpeedDecimals(int value) => Server.ToggleGameSetting("SpeedDecimals", value);

    public int GetVelocityDecimals() => GetDecimals("VelocityDecimals");
    public void SetVelocityDecimals(int value) => Server.ToggleGameSetting("VelocityDecimals", value);
    
    public int GetAngleDecimals() => GetDecimals("AngleDecimals");
    public void SetAngleDecimals(int value) => Server.ToggleGameSetting("AngleDecimals", value);

    public int GetCustomInfoDecimals() => GetDecimals("CustomInfoDecimals");
    public void SetCustomInfoDecimals(int value) => Server.ToggleGameSetting("CustomInfoDecimals", value);

    public int GetSubpixelIndicatorDecimals() => GetDecimals("SubpixelIndicatorDecimals");
    public void SetSubpixelIndicatorDecimals(int value) => Server.ToggleGameSetting("SubpixelIndicatorDecimals", value);

    public int GetFastForwardSpeed() {
        string speed = DefaultFastForwardSpeed.ToString();
        if (Server.GetDataFromGame(GameDataType.SettingValue, "FastForwardSpeed") is { } settingValue) {
            speed = settingValue;
        }

        bool success = int.TryParse(speed, out int result);
        return success ? result : DefaultFastForwardSpeed;
    }
    public void SetFastForwardSpeed(int value) => Server.ToggleGameSetting("FastForwardSpeed", value);

    public float GetSlowForwardSpeed() {
        string speed = DefaultSlowForwardSpeed.ToString(CultureInfo.InvariantCulture);
        if (Server.GetDataFromGame(GameDataType.SettingValue, "SlowForwardSpeed") is { } settingValue) {
            speed = settingValue;
        }

        bool success = float.TryParse(speed, NumberStyles.None, CultureInfo.InvariantCulture, out float result);
        return success ? result : DefaultSlowForwardSpeed;
    }
    public void SetSlowForwardSpeed(float value) => Server.ToggleGameSetting("SlowForwardSpeed", value);
    
    public void CopyCustomInfoTemplateToClipboard() => Server.ToggleGameSetting("Copy Custom Info Template to Clipboard", null);
    public void SetCustomInfoTemplateFromClipboard() => Server.ToggleGameSetting("Set Custom Info Template From Clipboard", null);
    public void ClearCustomInfoTemplate() => Server.ToggleGameSetting("Clear Custom Info Template", null);
    public void ClearWatchEntityInfo() => Server.ToggleGameSetting("Clear Watch Entity Info", null);
}

#endif