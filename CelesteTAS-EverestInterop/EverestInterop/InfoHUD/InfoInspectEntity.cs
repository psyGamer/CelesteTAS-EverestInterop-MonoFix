using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Celeste;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using TAS.EverestInterop.Hitboxes;
using TAS.Utils;

namespace TAS.EverestInterop.InfoHUD {
    public enum InspectEntityTypes {
        Position,
        DeclaredOnly,
        All
    }

    public static class InfoInspectEntity {
        private static readonly Regex NewLineRegex = new(@"\r\n?|\n", RegexOptions.Compiled);
        private static readonly Dictionary<string, IEnumerable<MemberInfo>> CachedMemberInfos = new();

        private static readonly List<WeakReference> RequireInspectEntities = new();
        private static readonly HashSet<UniqueEntityId> RequireInspectUniqueEntityIds = new();
        private static readonly HashSet<Entity> InspectingEntities = new();
        private static AreaKey requireInspectAreaKey;

        private static ILHook origLoadLevelHook;
        private static ILHook loadCustomEntityHook;

        private static CelesteTasModuleSettings Settings => CelesteTasModule.Settings;

        public static void Load() {
            On.Monocle.EntityList.DebugRender += EntityListOnDebugRender;
            On.Celeste.Level.Begin += LevelOnBegin;
            On.Celeste.Level.End += LevelOnEnd;
            On.Celeste.Level.LoadLevel += LevelOnLoadLevel;
            origLoadLevelHook = new ILHook(typeof(Level).GetMethod("orig_LoadLevel"), ModOrigLoadLevel);
            loadCustomEntityHook = new ILHook(typeof(Level).GetMethod("LoadCustomEntity"), ModLoadCustomEntity);
        }

        public static void Unload() {
            On.Monocle.EntityList.DebugRender -= EntityListOnDebugRender;
            On.Celeste.Level.Begin -= LevelOnBegin;
            On.Celeste.Level.End -= LevelOnEnd;
            On.Celeste.Level.LoadLevel -= LevelOnLoadLevel;
            origLoadLevelHook?.Dispose();
            loadCustomEntityHook?.Dispose();
            origLoadLevelHook = null;
            loadCustomEntityHook = null;
        }

        public static void HandleMouseData(MouseState mouseState, MouseState lastMouseData) {
            if (!Engine.Instance.IsActive) {
                return;
            }

            if (mouseState.RightButton == ButtonState.Pressed && lastMouseData.RightButton == ButtonState.Released) {
                ClearInspectEntities();
            }

            if (mouseState.LeftButton == ButtonState.Pressed && lastMouseData.LeftButton == ButtonState.Released &&
                FindClickedEntity(mouseState) is { } entity) {
                InspectingEntity(entity);
                PrintAllSimpleValues(entity);
            }
        }

        public static Entity FindClickedEntity(MouseState mouseState) {
            if (Engine.Scene is Level level) {
                Vector2 mousePosition = new(mouseState.X, mouseState.Y);
                Camera camera = level.Camera;
                int viewScale =
                    (int) Math.Round(Engine.Instance.GraphicsDevice.PresentationParameters.BackBufferWidth / (float) camera.Viewport.Width);
                Vector2 mouseWorldPosition = camera.ScreenToCamera((mousePosition / viewScale).Floor());
                if (Settings.CenterCamera && level.GetPlayer() is { } player) {
                    Vector2 offset = player.Position - new Vector2(camera.Viewport.Width / 2f, camera.Viewport.Height / 2f) - camera.Position;
                    mouseWorldPosition += offset;
                }

                Entity tempEntity = new() {Position = mouseWorldPosition, Collider = new Hitbox(1, 1)};
                Entity clickedEntity = level.Entities.Where(entity =>
                        (!Settings.InfoIgnoreTriggerWhenClickEntity || entity is not Trigger)
                        && entity.GetType() != typeof(Entity)
                        && entity is not RespawnTargetTrigger
                        && entity is not LookoutBlocker
                        && entity is not Killbox
                        && entity is not Water
                        && entity is not WaterFall
                        && entity is not BigWaterfall
                        && entity is not PlaybackBillboard
                        && entity is not ParticleSystem)
                    .FirstOrDefault(entity => entity.CollideCheck(tempEntity));
                return clickedEntity;
            } else {
                return null;
            }
        }

        private static void ModOrigLoadLevel(ILContext il) {
            ILCursor cursor = new(il);

            // NPC
            if (cursor.TryGotoNext(MoveType.Before,
                ins => ins.OpCode == OpCodes.Call &&
                       ins.Operand.ToString() == "T System.Collections.Generic.List`1/Enumerator<Celeste.EntityData>::get_Current()",
                ins => ins.OpCode == OpCodes.Stloc_S
            )) {
                cursor.Index++;
                object entityDataOperand = cursor.Next.Operand;
                while (cursor.TryGotoNext(MoveType.Before,
                    i => i.OpCode == OpCodes.Newobj && i.Operand is MethodReference {HasParameters: true} m && m.Parameters.Count == 1 &&
                         m.Parameters[0].ParameterType.Name == "Vector2",
                    i => i.OpCode == OpCodes.Call && i.Operand.ToString() == "System.Void Monocle.Scene::Add(Monocle.Entity)")) {
                    cursor.Index++;
                    cursor.Emit(OpCodes.Dup).Emit(OpCodes.Ldloc_S, entityDataOperand);
                    cursor.EmitDelegate<Action<Entity, EntityData>>(CacheEntityData);
                }
            }

            // DashSwitch.Create and FallingBlock.Create
            cursor.Goto(0);
            if (cursor.TryGotoNext(MoveType.Before,
                ins => ins.OpCode == OpCodes.Call &&
                       ins.Operand.ToString() == "T System.Collections.Generic.List`1/Enumerator<Celeste.EntityData>::get_Current()",
                ins => ins.OpCode == OpCodes.Stloc_S
            )) {
                cursor.Index++;
                object entityDataOperand = cursor.Next.Operand;
                while (cursor.TryGotoNext(MoveType.Before,
                    i => i.OpCode == OpCodes.Call && i.Operand.ToString().Contains("::Create"),
                    i => i.OpCode == OpCodes.Call && i.Operand.ToString() == "System.Void Monocle.Scene::Add(Monocle.Entity)")) {
                    cursor.Index++;
                    cursor.Emit(OpCodes.Dup).Emit(OpCodes.Ldloc_S, entityDataOperand);
                    cursor.EmitDelegate<Action<Entity, EntityData>>(CacheEntityData);
                }
            }

            // General
            cursor.Goto(0);
            while (cursor.TryGotoNext(MoveType.After,
                i => (i.OpCode == OpCodes.Newobj) && i.Operand is MethodReference {HasParameters: true} m &&
                     m.Parameters.Any(parameter => parameter.ParameterType.Name == "EntityData"))) {
                if (cursor.TryFindPrev(out ILCursor[] results,
                    i => i.OpCode == OpCodes.Ldloc_S && i.Operand is VariableDefinition v && v.VariableType.Name == "EntityData")) {
                    cursor.Emit(OpCodes.Dup).Emit(OpCodes.Ldloc_S, results[0].Next.Operand);
                    cursor.EmitDelegate<Action<Entity, EntityData>>(CacheEntityData);
                }
            }
        }

        private static void ModLoadCustomEntity(ILContext il) {
            ILCursor cursor = new(il);

            if (cursor.TryGotoNext(MoveType.After, ins => ins.MatchCallvirt<Level.EntityLoader>("Invoke"))) {
                cursor.Emit(OpCodes.Dup).Emit(OpCodes.Ldarg_0);
                cursor.EmitDelegate<Action<Entity, EntityData>>(CacheEntityData);
            }

            cursor.Goto(0);
            while (cursor.TryGotoNext(MoveType.After,
                i => i.OpCode == OpCodes.Newobj && i.Operand.ToString().Contains("::.ctor(Celeste.EntityData"))) {
                cursor.Emit(OpCodes.Dup).Emit(OpCodes.Ldarg_0);
                cursor.EmitDelegate<Action<Entity, EntityData>>(CacheEntityData);
            }
        }

        private static void CacheEntityData(Entity entity, EntityData data) {
            entity.SaveEntityData(data);
        }

        private static void InspectingEntity(Entity clickedEntity) {
            requireInspectAreaKey = clickedEntity.SceneAs<Level>().Session.Area;
            if (clickedEntity.LoadEntityData() is { } entityData) {
                UniqueEntityId uniqueEntityId = new(clickedEntity, entityData);
                if (RequireInspectUniqueEntityIds.Contains(uniqueEntityId)) {
                    RequireInspectUniqueEntityIds.Remove(uniqueEntityId);
                } else {
                    RequireInspectUniqueEntityIds.Add(uniqueEntityId);
                }
            } else {
                if (RequireInspectEntities.FirstOrDefault(reference => reference.Target == clickedEntity) is { } alreadyAdded) {
                    RequireInspectEntities.Remove(alreadyAdded);
                } else {
                    RequireInspectEntities.Add(new WeakReference(clickedEntity));
                }
            }

            GameInfo.Update();
        }

        private static void EntityListOnDebugRender(On.Monocle.EntityList.orig_DebugRender orig, EntityList self, Camera camera) {
            orig(self, camera);

            if (Settings.ShowHitboxes) {
                foreach (Entity entity in Engine.Scene.Entities) {
                    if (InspectingEntities.Contains(entity)) {
                        Draw.Point(entity.Position, HitboxColor.EntityColorInversely);
                    }
                }
            }
        }

        private static void LevelOnBegin(On.Celeste.Level.orig_Begin orig, Level self) {
            orig(self);

            if (self.Session.Area != requireInspectAreaKey) {
                ClearInspectEntities();
            }
        }

        private static void LevelOnEnd(On.Celeste.Level.orig_End orig, Level self) {
            orig(self);
            InspectingEntities.Clear();
        }

        private static void LevelOnLoadLevel(On.Celeste.Level.orig_LoadLevel orig, Level self, Player.IntroTypes playerIntro, bool isFromLoader) {
            orig(self, playerIntro, isFromLoader);

            RequireInspectEntities.ToList().ForEach(reference => {
                if (reference.Target is Entity {Scene: null}) {
                    RequireInspectEntities.Remove(reference);
                }
            });
        }

        private static void ClearInspectEntities() {
            RequireInspectEntities.Clear();
            RequireInspectUniqueEntityIds.Clear();
            InspectingEntities.Clear();
            GameInfo.Update();
        }

        public static string GetInspectingEntitiesInfo(string separator = "\n") {
            InspectingEntities.Clear();
            string inspectingInfo = string.Empty;
            if (Engine.Scene is not Level level) {
                return string.Empty;
            }

            if (RequireInspectEntities.IsNotEmpty()) {
                inspectingInfo = string.Join(separator, RequireInspectEntities.Where(reference => reference.IsAlive).Select(
                    reference => {
                        Entity entity = (Entity) reference.Target;
                        InspectingEntities.Add(entity);
                        return GetEntityValues(entity, Settings.InfoInspectEntityType, separator);
                    }
                ));
            }

            if (RequireInspectUniqueEntityIds.IsNotEmpty()) {
                Dictionary<UniqueEntityId, Entity> matchEntities = GetMatchEntities(level);
                if (matchEntities.IsNotEmpty()) {
                    if (inspectingInfo.IsNotNullOrEmpty()) {
                        inspectingInfo += separator;
                    }

                    inspectingInfo += string.Join(separator, matchEntities.Select(pair => {
                        Entity entity = matchEntities[pair.Key];
                        InspectingEntities.Add(entity);
                        return GetEntityValues(entity, Settings.InfoInspectEntityType, separator);
                    }));
                }
            }

            return inspectingInfo;
        }

        private static void PrintAllSimpleValues(Entity entity) {
            ("Info of Clicked Entity:\n" + GetEntityValues(entity, InspectEntityTypes.All)).Log(true);
        }

        private static string GetEntityValues(Entity entity, InspectEntityTypes inspectEntityType, string separator = "\n") {
            Type type = entity.GetType();
            string entityId = "";
            if (entity.LoadEntityData() is { } entityData) {
                entityId = $"[{entityData.ToEntityId().ToString()}]";
            }

            if (inspectEntityType == InspectEntityTypes.Position) {
                return GetPositionInfo(entity, entityId);
            }

            List<string> values = GetAllSimpleFields(type, inspectEntityType == InspectEntityTypes.DeclaredOnly).Select(info => {
                object value = info switch {
                    FieldInfo fieldInfo => fieldInfo.GetValue(entity),
                    PropertyInfo propertyInfo => propertyInfo.GetValue(entity),
                    _ => null
                };

                if (value is float floatValue) {
                    if (info.Name.EndsWith("Timer")) {
                        value = $"{(int) 60f / Engine.TimeRateB * floatValue:F0}";
                    } else {
                        value = Settings.RoundCustomInfo ? $"{floatValue:F2}" : $"{floatValue:F12}";
                    }
                } else if (value is Vector2 vector2) {
                    value = vector2.ToSimpleString(Settings.RoundCustomInfo);
                }

                if (separator == "\t" && value != null) {
                    value = NewLineRegex.Replace(value.ToString(), "");
                }

                return $"{type.Name}{entityId}.{info.Name}: {value}";
            }).ToList();

            values.Insert(0, GetPositionInfo(entity, entityId));

            return string.Join(separator, values);
        }

        private static string GetPositionInfo(Entity entity, string entityId) {
            return $"{entity.GetType().Name}{entityId}: {entity.ToSimplePositionString(Settings.RoundCustomInfo)}";
        }

        private static IEnumerable<MemberInfo> GetAllSimpleFields(Type type, bool declaredOnly = false) {
            BindingFlags bindingFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            if (declaredOnly) {
                bindingFlags |= BindingFlags.DeclaredOnly;
            }

            string key = type.FullName + "-" + bindingFlags;

            if (CachedMemberInfos.ContainsKey(key)) {
                return CachedMemberInfos[key];
            } else {
                List<MemberInfo> memberInfos = type
                    .GetFields(bindingFlags).Where(info => {
                        Type t = info.FieldType;
                        return (t.IsPrimitive || t.IsEnum || t == typeof(Vector2)) && !info.Name.EndsWith("k__BackingField");
                    }).Cast<MemberInfo>().ToList();
                List<MemberInfo> propertyInfos = type
                    .GetProperties(bindingFlags).Where(
                        info => {
                            Type t = info.PropertyType;
                            return t.IsPrimitive || t.IsEnum || t == typeof(Vector2);
                        }).Cast<MemberInfo>().ToList();
                memberInfos.AddRange(propertyInfos);

                List<MemberInfo> result = new();
                foreach (IGrouping<bool, MemberInfo> grouping in memberInfos.GroupBy(info => type == info.DeclaringType)) {
                    List<MemberInfo> infos = grouping.ToList();
                    infos.Sort((info1, info2) => string.Compare(info1.Name, info2.Name, StringComparison.InvariantCultureIgnoreCase));
                    if (grouping.Key) {
                        result.InsertRange(0, infos);
                    } else {
                        result.AddRange(infos);
                    }
                }

                CachedMemberInfos[key] = result;
                return result;
            }
        }

        private static Dictionary<UniqueEntityId, Entity> GetMatchEntities(Level level) {
            Dictionary<UniqueEntityId, Entity> result = new();
            List<Entity> possibleEntities = new();
            HashSet<Type> possibleTypes = new();

            string currentRoom = level.Session.Level;
            foreach (UniqueEntityId id in RequireInspectUniqueEntityIds.Where(id => id.GlobalOrPersistent || id.EntityId.Level == currentRoom)) {
                possibleTypes.Add(id.Type);
            }

            if (possibleTypes.IsEmpty()) {
                return result;
            }

            if (possibleTypes.All(type => level.Tracker.Entities.ContainsKey(type))) {
                foreach (Type type in possibleTypes) {
                    possibleEntities.AddRange(level.Tracker.Entities[type]);
                }
            } else {
                possibleEntities.AddRange(level.Entities.Where(entity => possibleTypes.Contains(entity.GetType())));
            }

            foreach (Entity entity in possibleEntities) {
                if (entity.LoadEntityData() is not { } entityData) {
                    continue;
                }

                UniqueEntityId uniqueEntityId = new(entity, entityData);
                if (RequireInspectUniqueEntityIds.Contains(uniqueEntityId) && !result.ContainsKey(uniqueEntityId)) {
                    result[uniqueEntityId] = entity;

                    if (result.Count == RequireInspectUniqueEntityIds.Count) {
                        return result;
                    }
                }
            }

            return result;
        }
    }

    internal record UniqueEntityId {
        public readonly Type Type;
        public readonly bool GlobalOrPersistent;
        public readonly EntityID EntityId;

        public UniqueEntityId(Entity entity, EntityData entityData) {
            Type = entity.GetType();
            GlobalOrPersistent = entity.TagCheck(Tags.Global) || entity.TagCheck(Tags.Persistent);
            EntityId = entityData.ToEntityId();
        }
    }
}