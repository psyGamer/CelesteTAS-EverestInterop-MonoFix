﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Celeste;
using Monocle;
using TAS.EverestInterop.InfoHUD;
using TAS.Input;
using TAS.Input.Commands;
using TAS.Module;
using TAS.Utils;

namespace TAS;

public static class ExportGameInfo {
    private static StreamWriter streamWriter;
    private static IDictionary<string, Func<List<Entity>>> trackedEntities;
    private static bool exporting;

    // ReSharper disable once UnusedMember.Local
    // "StartExportGameInfo"
    // "StartExportGameInfo Path"
    // "StartExportGameInfo Path EntitiesToTrack"
    [TasCommand("StartExportGameInfo", AliasNames = new[] {"ExportGameInfo"}, CalcChecksum = false)]
    private static void StartExportCommand(string[] args) {
        string path = "dump.txt";
        if (args.Length > 0) {
            if (args[0].Contains(".")) {
                path = args[0];
                args = args.Skip(1).ToArray();
            }
        }

        BeginExport(path, args);
    }

    // ReSharper disable once UnusedMember.Local
    [DisableRun]
    [TasCommand("FinishExportGameInfo", AliasNames = new[] {"EndExportGameInfo"}, CalcChecksum = false)]
    private static void FinishExportCommand() {
        exporting = false;
        streamWriter?.Dispose();
        streamWriter = null;
    }

    private static void BeginExport(string path, string[] tracked) {
        streamWriter?.Dispose();

        exporting = true;
        if (Path.GetDirectoryName(path) is { } dir && dir.IsNotEmpty()) {
            Directory.CreateDirectory(dir);
        }

        streamWriter = new StreamWriter(path);
        streamWriter.WriteLine(string.Join("\t", "Line", "Inputs", "Frames", "Time", "Position", "Speed", "State", "Statuses", "Entities"));
        trackedEntities = new Dictionary<string, Func<List<Entity>>>();
        foreach (string typeName in tracked) {
            if (!InfoCustom.TryParseTypes(typeName, out List<Type> types)) {
                continue;
            }

            foreach (Type type in types) {
                if (type.IsSameOrSubclassOf(typeof(Entity)) && type.FullName != null) {
                    trackedEntities[type.FullName] = () => InfoCustom.FindEntities(type, string.Empty);
                }
            }
        }
    }

    public static void ExportInfo() {
        if (exporting && Manager.Controller.Current is { } currentInput) {
            Engine.Scene.OnEndOfFrame += () => ExportInfo(currentInput);
        }
    }

    private static void ExportInfo(InputFrame inputFrame) {
        InputController controller = Manager.Controller;
        string output;
        if (Engine.Scene is Level level) {
            Player player = level.Tracker.GetEntity<Player>();
            if (player == null) {
                return;
            }

            string time = GameInfo.GetChapterTime(level);
            string pos = player.ToSimplePositionString(GetDecimals(TasSettings.PositionDecimals, CelesteTasSettings.MaxDecimals));
            string speed = player.Speed.ToSimpleString(GetDecimals(TasSettings.SpeedDecimals, CelesteTasSettings.MaxDecimals));
            GameInfo.GetAdjustedLiftBoost(player, out string liftBoost);
            string statuses = $"{GameInfo.GetStatuses(level, player)}\t{liftBoost}";

            output = string.Join("\t",
                inputFrame.Line + 1, $"{controller.CurrentFrameInInput}/{inputFrame}", controller.CurrentFrameInTas, time, pos, speed,
                PlayerStates.GetStateName(player.StateMachine.State),
                statuses);

            foreach (string typeName in trackedEntities.Keys) {
                List<Entity> entities = trackedEntities[typeName].Invoke();
                if (entities == null) {
                    continue;
                }

                foreach (Entity entity in entities) {
                    output +=
                        $"\t{typeName}: {entity.ToSimplePositionString(GetDecimals(TasSettings.PositionDecimals, CelesteTasSettings.MaxDecimals))}";
                }
            }

            if (InfoCustom.GetInfo(GetDecimals(TasSettings.CustomInfoDecimals, CelesteTasSettings.MaxDecimals)) is { } customInfo &&
                customInfo.IsNotEmpty()) {
                output += $"\t{customInfo.ReplaceLineBreak(" ")}";
            }

            if (InfoWatchEntity.GetInfo("\t", true, GetDecimals(TasSettings.CustomInfoDecimals, CelesteTasSettings.MaxDecimals)) is { } watchInfo &&
                watchInfo.IsNotEmpty()) {
                output += $"\t{watchInfo}";
            }
        } else {
            string sceneName;
            if (Engine.Scene is Overworld overworld) {
                sceneName = $"Overworld {(overworld.Current ?? overworld.Next).GetType().Name}";
            } else {
                sceneName = Engine.Scene.GetType().Name;
            }

            output = string.Join("\t", inputFrame.Line + 1, $"{controller.CurrentFrameInInput}/{inputFrame}", controller.CurrentFrameInTas,
                sceneName);
        }

        streamWriter.WriteLine(output);
        streamWriter.Flush();
    }

    private static int GetDecimals(int current, int max) {
        return current == 0 ? current : max;
    }
}