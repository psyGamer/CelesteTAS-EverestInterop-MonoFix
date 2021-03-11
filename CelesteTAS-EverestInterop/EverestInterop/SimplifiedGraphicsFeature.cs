﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Xml;
using Celeste;
using Celeste.Mod;
using Microsoft.Xna.Framework;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using TAS.Utils;

namespace TAS.EverestInterop {
    public static class SimplifiedGraphicsFeature {
        private static readonly List<string> SolidDecals = new List<string> {
            "3-resort/bridgecolumn",
            "3-resort/bridgecolumntop",
            "3-resort/brokenelevator",
            "3-resort/roofcenter",
            "3-resort/roofcenter_b",
            "3-resort/roofcenter_c",
            "3-resort/roofcenter_d",
            "3-resort/roofedge",
            "3-resort/roofedge_b",
            "3-resort/roofedge_c",
            "3-resort/roofedge_d",
            "4-cliffside/bridge_a",
        };

        private static readonly FieldInfo SpinnerColorField = typeof(CrystalStaticSpinner).GetFieldInfo("color");
        private static readonly FieldInfo DecalInfoCustomProperties = typeof(DecalRegistry.DecalInfo).GetFieldInfo("CustomProperties");
        private static readonly List<ILHook> IlHooks = new List<ILHook>();

        private static bool lastSimplifiedGraphics = Settings.SimplifiedGraphics;
        private static CelesteTasModuleSettings Settings => CelesteTasModule.Settings;

        public static TextMenu.Item CreateSimplifiedGraphicsOption() {
            return new TextMenuExt.SubMenu("Simplified Graphics".ToDialogText(), false).Apply(subMenu => {
                subMenu.Add(new TextMenu.OnOff("Enabled".ToDialogText(), Settings.SimplifiedGraphics).Change(value =>
                    Settings.SimplifiedGraphics = value));
                subMenu.Add(
                    new TextMenuExt.EnumerableSlider<bool>("Gameplay".ToDialogText(), Menu.CreateDefaultHideOptions(), Settings.HideGameplay).Change(
                        value =>
                            Settings.HideGameplay = value));
                subMenu.Add(
                    new TextMenuExt.EnumerableSlider<int?>("Lighting".ToDialogText(), Menu.CreateSliderOptions(10, 0, i => $"{i * 10}%"),
                        Settings.SimplifiedLighting).Change(value =>
                        Settings.SimplifiedLighting = value));
                subMenu.Add(
                    new TextMenuExt.EnumerableSlider<int?>("Bloom Base".ToDialogText(),
                        Menu.CreateSliderOptions(0, 10, i => (i / 10f).ToString(CultureInfo.InvariantCulture)), Settings.SimplifiedBloomBase).Change(
                        value =>
                            Settings.SimplifiedBloomBase = value));
                subMenu.Add(
                    new TextMenuExt.EnumerableSlider<int?>("Bloom Strength".ToDialogText(), Menu.CreateSliderOptions(1, 10),
                            Settings.SimplifiedBloomStrength)
                        .Change(
                            value => Settings.SimplifiedBloomStrength = value));
                subMenu.Add(
                    new TextMenuExt.EnumerableSlider<SpinnerColor>("Spinner Color".ToDialogText(), SpinnerColor.All,
                        Settings.SimplifiedSpinnerColor).Change(value =>
                        Settings.SimplifiedSpinnerColor = value));
                subMenu.Add(
                    new TextMenuExt.EnumerableSlider<bool>("Dust Sprite Edge".ToDialogText(), Menu.CreateDefaultHideOptions(),
                        Settings.SimplifiedDustSpriteEdge).Change(value =>
                        Settings.SimplifiedDustSpriteEdge = value));
                subMenu.Add(
                    new TextMenuExt.EnumerableSlider<bool>("Spotlight Wipe".ToDialogText(), Menu.CreateDefaultHideOptions(),
                        Settings.SimplifiedSpotlightWipe).Change(value =>
                        Settings.SimplifiedSpotlightWipe = value));
                subMenu.Add(
                    new TextMenuExt.EnumerableSlider<bool>("Color Grade".ToDialogText(), Menu.CreateDefaultHideOptions(),
                        Settings.SimplifiedColorGrade).Change(value =>
                        Settings.SimplifiedColorGrade = value));
                subMenu.Add(
                    new TextMenuExt.EnumerableSlider<bool>("Backdrop".ToDialogText(), Menu.CreateDefaultHideOptions(), Settings.SimplifiedBackdrop)
                        .Change(value =>
                            Settings.SimplifiedBackdrop = value));
                subMenu.Add(
                    new TextMenuExt.EnumerableSlider<bool>("Decal".ToDialogText(), Menu.CreateDefaultHideOptions(), Settings.SimplifiedDecal).Change(
                        value =>
                            Settings.SimplifiedDecal = value));
                subMenu.Add(
                    new TextMenuExt.EnumerableSlider<bool>("Particle".ToDialogText(), Menu.CreateDefaultHideOptions(), Settings.SimplifiedParticle)
                        .Change(value =>
                            Settings.SimplifiedParticle = value));
                subMenu.Add(
                    new TextMenuExt.EnumerableSlider<bool>("Distort".ToDialogText(), Menu.CreateDefaultHideOptions(), Settings.SimplifiedDistort)
                        .Change(value =>
                            Settings.SimplifiedDistort = value));
                subMenu.Add(
                    new TextMenuExt.EnumerableSlider<bool>("Mini Text Box".ToDialogText(), Menu.CreateDefaultHideOptions(),
                            Settings.SimplifiedMiniTextbox)
                        .Change(value =>
                            Settings.SimplifiedMiniTextbox = value));
                subMenu.Add(
                    new TextMenuExt.EnumerableSlider<bool>("Dream Block".ToDialogText(), Menu.CreateSimplifyOptions(), Settings.SimplifiedDreamBlock)
                        .Change(value =>
                            Settings.SimplifiedDreamBlock = value));
                subMenu.Add(
                    new TextMenuExt.EnumerableSlider<bool>("Lava".ToDialogText(), Menu.CreateSimplifyOptions(), Settings.SimplifiedLava).Change(
                        value =>
                            Settings.SimplifiedLava = value));
                subMenu.Add(
                    new TextMenuExt.EnumerableSlider<bool>("Lightning".ToDialogText(), Menu.CreateSimplifyOptions(), Settings.SimplifiedLightning)
                        .Change(value =>
                            Settings.SimplifiedLightning = value));
            });
        }

        public static void Load() {
            // Optional: Various graphical simplifications to cut down on visual noise.
            On.Celeste.Level.Update += Level_Update;
            IL.Celeste.LightingRenderer.Render += LightingRenderer_Render;
            On.Celeste.ColorGrade.Set_MTexture_MTexture_float += ColorGradeOnSet_MTexture_MTexture_float;
            IL.Celeste.BloomRenderer.Apply += BloomRendererOnApply;
            On.Celeste.Decal.Render += Decal_Render;
            On.Monocle.Particle.Render += Particle_Render;
            On.Celeste.MiniTextbox.Render += MiniTextbox_Render;
            IL.Celeste.BackdropRenderer.Render += BackdropRenderer_Render;
            On.Celeste.CrystalStaticSpinner.CreateSprites += CrystalStaticSpinner_CreateSprites;
            IlHooks.Add(new ILHook(typeof(DustGraphic).GetNestedType("Eyeballs", BindingFlags.NonPublic).GetMethod("Render"), ModDustEyes));
            On.Celeste.DustStyles.Get_Session += DustStyles_Get_Session;
            On.Celeste.DreamBlock.Lerp += DreamBlock_Lerp;
            On.Celeste.LavaRect.Wave += LavaRect_Wave;
            On.Celeste.FloatingDebris.ctor_Vector2 += FloatingDebris_ctor;
            On.Celeste.MoonCreature.ctor_Vector2 += MoonCreature_ctor;
            IL.Celeste.LightningRenderer.Render += LightningRenderer_RenderIL;
            On.Celeste.LightningRenderer.Bolt.Render += Bolt_Render;
            On.Celeste.SummitCloud.Render += SummitCloudOnRender;
            On.Celeste.SpotlightWipe.Render += SpotlightWipeOnRender;
            On.Celeste.ReflectionTentacles.Render += ReflectionTentacles_Render;

            if (Type.GetType("FrostHelper.CustomSpinner, FrostTempleHelper") is Type customSpinnerType) {
                IlHooks.Add(new ILHook(customSpinnerType.GetConstructors()[0], ModCustomSpinnerColor));
            }

            if (Type.GetType("Celeste.Mod.MaxHelpingHand.Entities.RainbowSpinnerColorController, MaxHelpingHand") is Type rainbowSpinnerType) {
                IlHooks.Add(new ILHook(rainbowSpinnerType.GetConstructors()[0], ModRainbowSpinnerColor));
            }
        }

        public static void Unload() {
            On.Celeste.Level.Update -= Level_Update;
            IL.Celeste.LightingRenderer.Render -= LightingRenderer_Render;
            On.Celeste.ColorGrade.Set_MTexture_MTexture_float -= ColorGradeOnSet_MTexture_MTexture_float;
            IL.Celeste.BloomRenderer.Apply -= BloomRendererOnApply;
            On.Celeste.Decal.Render -= Decal_Render;
            On.Monocle.Particle.Render -= Particle_Render;
            On.Celeste.MiniTextbox.Render -= MiniTextbox_Render;
            IL.Celeste.BackdropRenderer.Render -= BackdropRenderer_Render;
            On.Celeste.CrystalStaticSpinner.CreateSprites -= CrystalStaticSpinner_CreateSprites;
            On.Celeste.DustStyles.Get_Session -= DustStyles_Get_Session;
            On.Celeste.DreamBlock.Lerp -= DreamBlock_Lerp;
            On.Celeste.LavaRect.Wave -= LavaRect_Wave;
            On.Celeste.FloatingDebris.ctor_Vector2 -= FloatingDebris_ctor;
            On.Celeste.MoonCreature.ctor_Vector2 -= MoonCreature_ctor;
            IL.Celeste.LightningRenderer.Render -= LightningRenderer_RenderIL;
            On.Celeste.LightningRenderer.Bolt.Render -= Bolt_Render;
            On.Celeste.SummitCloud.Render -= SummitCloudOnRender;
            On.Celeste.SpotlightWipe.Render -= SpotlightWipeOnRender;
            On.Celeste.ReflectionTentacles.Render -= ReflectionTentacles_Render;
            IlHooks.ForEach(hook => hook.Dispose());
        }

        private static void OnSimplifiedGraphicsChanged(bool simplifiedGraphics) {
            if (!(Engine.Scene is Level level)) {
                return;
            }

            if (simplifiedGraphics) {
                level.Entities.FindAll<FloatingDebris>().ForEach(debris => debris.Visible = false);
                level.Entities.FindAll<MoonCreature>().ForEach(creature => creature.Visible = false);
            } else {
                level.Entities.FindAll<FloatingDebris>().ForEach(debris => debris.Visible = true);
                level.Entities.FindAll<MoonCreature>().ForEach(creature => creature.Visible = true);
            }
        }

        private static void Level_Update(On.Celeste.Level.orig_Update orig, Level self) {
            orig(self);

            // Seems modified the Settings.SimplifiedGraphics property will mess key config.
            if (lastSimplifiedGraphics != Settings.SimplifiedGraphics) {
                OnSimplifiedGraphicsChanged(Settings.SimplifiedGraphics);
                lastSimplifiedGraphics = Settings.SimplifiedGraphics;
            }
        }

        private static void LightingRenderer_Render(ILContext il) {
            ILCursor ilCursor = new ILCursor(il);
            if (ilCursor.TryGotoNext(
                MoveType.After,
                ins => ins.MatchCall(typeof(MathHelper), "Clamp")
            )) {
                ilCursor.EmitDelegate<Func<float, float>>(alpha =>
                    Settings.SimplifiedGraphics && Settings.SimplifiedLighting != null ? (10 - Settings.SimplifiedLighting.Value) / 10f : alpha);
            }
        }

        private static void ColorGradeOnSet_MTexture_MTexture_float(On.Celeste.ColorGrade.orig_Set_MTexture_MTexture_float orig, MTexture fromTex,
            MTexture toTex, float p) {
            bool? origEnabled = null;
            if (Settings.SimplifiedGraphics && Settings.SimplifiedColorGrade) {
                origEnabled = ColorGrade.Enabled;
                ColorGrade.Enabled = false;
            }

            orig(fromTex, toTex, p);
            if (origEnabled.HasValue) {
                ColorGrade.Enabled = origEnabled.Value;
            }
        }

        private static void BloomRendererOnApply(ILContext il) {
            ILCursor ilCursor = new ILCursor(il);
            while (ilCursor.TryGotoNext(
                MoveType.After,
                ins => ins.OpCode == OpCodes.Ldarg_0,
                ins => ins.MatchLdfld<BloomRenderer>("Base")
            )) {
                ilCursor.EmitDelegate<Func<float, float>>(bloomValue =>
                    Settings.SimplifiedGraphics && Settings.SimplifiedBloomBase.HasValue ? Settings.SimplifiedBloomBase.Value / 10f : bloomValue);
            }

            while (ilCursor.TryGotoNext(
                MoveType.After,
                ins => ins.OpCode == OpCodes.Ldarg_0,
                ins => ins.MatchLdfld<BloomRenderer>("Strength")
            )) {
                ilCursor.EmitDelegate<Func<float, float>>(bloomValue =>
                    Settings.SimplifiedGraphics && Settings.SimplifiedBloomStrength.HasValue
                        ? Settings.SimplifiedBloomStrength.Value / 10f
                        : bloomValue);
            }
        }

        private static void Decal_Render(On.Celeste.Decal.orig_Render orig, Decal self) {
            if (Settings.SimplifiedGraphics && Settings.SimplifiedDecal) {
                string decalName = self.Name.ToLower().Replace("decals/", "");
                if (!SolidDecals.Contains(decalName)) {
                    if (!DecalRegistry.RegisteredDecals.ContainsKey(decalName)) {
                        return;
                    }

                    object customProperties = DecalInfoCustomProperties.GetValue(DecalRegistry.RegisteredDecals[decalName]);

                    switch (customProperties) {
                        case Dictionary<string, XmlAttributeCollection> dictionary when !dictionary.ContainsKey("solid"):
                        case List<KeyValuePair<string, XmlAttributeCollection>> list when list.All(pair => pair.Key != "solid"):
                            return;
                    }
                }
            }

            orig(self);
        }

        private static void Particle_Render(On.Monocle.Particle.orig_Render orig, ref Particle self) {
            if (Settings.SimplifiedGraphics && Settings.SimplifiedParticle) {
                return;
            }

            orig(ref self);
        }

        private static void MiniTextbox_Render(On.Celeste.MiniTextbox.orig_Render orig, MiniTextbox self) {
            if (Settings.SimplifiedGraphics && Settings.SimplifiedMiniTextbox) {
                return;
            }

            orig(self);
        }

        private static void BackdropRenderer_Render(ILContext il) {
            ILCursor c = new ILCursor(il);

            Instruction methodStart = c.Next;
            c.EmitDelegate<Func<bool>>(() => !Settings.SimplifiedGraphics || !Settings.SimplifiedBackdrop);
            c.Emit(OpCodes.Brtrue, methodStart);
            c.Emit(OpCodes.Ret);
            c.GotoNext(i => i.MatchLdloc(2));
            c.Emit(OpCodes.Ldloc_2);
            c.EmitDelegate<Action<Backdrop>>((backdrop => {
                if (Settings.Mod9DLighting && backdrop.Visible && Engine.Scene is Level level) {
                    bool hideBackdrop =
                        (level.Session.Level.StartsWith("g") || level.Session.Level.StartsWith("h"))
                        && level.Session.Level != "hh-08"
                        && backdrop.Name?.StartsWith("bgs/nameguysdsides") == true;
                    backdrop.Visible = !hideBackdrop;
                }
            }));
        }

        private static void CrystalStaticSpinner_CreateSprites(On.Celeste.CrystalStaticSpinner.orig_CreateSprites orig, CrystalStaticSpinner self) {
            if (Settings.SimplifiedGraphics && Settings.SimplifiedSpinnerColor.Name != (CrystalColor) (-1)) {
                SpinnerColorField.SetValue(self, Settings.SimplifiedSpinnerColor.Name);
            }

            orig(self);
        }

        private static void ModDustEyes(ILContext il) {
            ILCursor ilCursor = new ILCursor(il);
            Instruction start = ilCursor.Next;
            ilCursor.EmitDelegate<Func<bool>>(() => Settings.SimplifiedGraphics);
            ilCursor.Emit(OpCodes.Brfalse, start);
            ilCursor.Emit(OpCodes.Ret);
        }

        private static DustStyles.DustStyle DustStyles_Get_Session(On.Celeste.DustStyles.orig_Get_Session orig, Session session) {
            if (Settings.SimplifiedGraphics && Settings.SimplifiedDustSpriteEdge) {
                Color color = Color.Transparent;
                return new DustStyles.DustStyle {
                    EdgeColors = new[] {color.ToVector3(), color.ToVector3(), color.ToVector3()},
                    EyeColor = color,
                    EyeTextures = "danger/dustcreature/eyes"
                };
            }

            return orig(session);
        }

        private static float DreamBlock_Lerp(On.Celeste.DreamBlock.orig_Lerp orig, DreamBlock self, float a, float b, float percent) {
            if (Settings.SimplifiedGraphics && Settings.SimplifiedDreamBlock) {
                return 0f;
            }

            return orig(self, a, b, percent);
        }

        private static float LavaRect_Wave(On.Celeste.LavaRect.orig_Wave orig, LavaRect self, int step, float length) {
            if (Settings.SimplifiedGraphics && Settings.SimplifiedLava) {
                return 0f;
            }

            return orig(self, step, length);
        }

        private static void FloatingDebris_ctor(On.Celeste.FloatingDebris.orig_ctor_Vector2 orig, FloatingDebris self, Vector2 position) {
            orig(self, position);
            if (Settings.SimplifiedGraphics) {
                self.Add(new RemoveSelfComponent());
            }
        }

        private static void MoonCreature_ctor(On.Celeste.MoonCreature.orig_ctor_Vector2 orig, MoonCreature self, Vector2 position) {
            orig(self, position);
            if (Settings.SimplifiedGraphics) {
                self.Add(new RemoveSelfComponent());
            }
        }

        private static void LightningRenderer_RenderIL(ILContext il) {
            ILCursor c = new ILCursor(il);

            if (c.TryGotoNext(
                MoveType.After,
                ins => ins.OpCode == OpCodes.Ldarg_0,
                ins => ins.MatchLdfld<LightningRenderer>("DrawEdges")
            )) {
                c.EmitDelegate<Func<bool, bool>>(drawEdges => (!Settings.SimplifiedGraphics || !Settings.SimplifiedLightning) && drawEdges);
                c.Goto(0);
            }

            for (int j = 0; j < 2; j++) {
                c.GotoNext(i => i.MatchNewobj(out _));
            }

            c.GotoNext();
            Instruction cont = c.Next;

            c.EmitDelegate<Func<bool>>(() => Settings.SimplifiedGraphics && Settings.SimplifiedLightning);
            c.Emit(OpCodes.Brfalse, cont);
            c.Emit(OpCodes.Dup);
            c.Emit(OpCodes.Call, (typeof(Color).GetMethod("get_LightGoldenrodYellow")));
            c.Emit(OpCodes.Call, typeof(Draw).GetMethod("HollowRect", new[] {typeof(Rectangle), typeof(Color)}));
        }

        private static void Bolt_Render(On.Celeste.LightningRenderer.Bolt.orig_Render orig, object self) {
            if (Settings.SimplifiedGraphics && Settings.SimplifiedLightning) {
                return;
            }

            orig.Invoke(self);
        }

        private static void SummitCloudOnRender(On.Celeste.SummitCloud.orig_Render orig, SummitCloud self) {
            if (Settings.SimplifiedGraphics) {
                return;
            }

            orig(self);
        }

        // Hide screen wipe when beginning level
        private static void SpotlightWipeOnRender(On.Celeste.SpotlightWipe.orig_Render orig, SpotlightWipe self, Scene scene) {
            if (Settings.SimplifiedGraphics && Settings.SimplifiedSpotlightWipe) {
                return;
            }

            orig(self, scene);
        }

        private static void ReflectionTentacles_Render(On.Celeste.ReflectionTentacles.orig_Render orig, ReflectionTentacles self) {
            if (!Settings.SimplifiedGraphics) {
                orig(self);
            }
        }

        private static void ModCustomSpinnerColor(ILContext il) {
            ILCursor ilCursor = new ILCursor(il);
            if (ilCursor.TryGotoNext(
                i => i.OpCode == OpCodes.Ldarg_0,
                i => i.OpCode == OpCodes.Ldarg_S && i.Operand.ToString() == "tint",
                i => i.OpCode == OpCodes.Call && i.Operand.ToString() == "Microsoft.Xna.Framework.Color Monocle.Calc::HexToColor(System.String)",
                i => i.OpCode == OpCodes.Stfld && i.Operand.ToString() == "Microsoft.Xna.Framework.Color FrostHelper.CustomSpinner::Tint"
            )) {
                ilCursor.Index += 2;
                ilCursor.EmitDelegate<Func<string, string>>(color =>
                    Settings.SimplifiedGraphics && Settings.SimplifiedSpinnerColor.Value != null ? Settings.SimplifiedSpinnerColor.Value : color);
            }
        }

        private static void ModRainbowSpinnerColor(ILContext il) {
            ILCursor ilCursor = new ILCursor(il);
            if (Type.GetType("Celeste.Mod.MaxHelpingHand.Entities.RainbowSpinnerColorController, MaxHelpingHand") is Type rainbowSpinnerType &&
                ilCursor.TryGotoNext(
                    i => i.MatchLdstr("gradientSize")
                )) {
                ilCursor.Emit(OpCodes.Ldarg_0)
                    .Emit(OpCodes.Ldfld, rainbowSpinnerType.GetField("colors", BindingFlags.Instance | BindingFlags.NonPublic));
                ilCursor.EmitDelegate<Action<Color[]>>(colors => {
                    if (!Settings.SimplifiedGraphics || Settings.SimplifiedSpinnerColor.Value == null) {
                        return;
                    }

                    Color simpleColor = Calc.HexToColor(Settings.SimplifiedSpinnerColor.Value);
                    for (int i = 0; i < colors.Length; i++) {
                        colors[i] = simpleColor;
                    }
                });
            }
        }

        // ReSharper disable FieldCanBeMadeReadOnly.Global
        public struct SpinnerColor {
            public static readonly List<SpinnerColor> All = new List<SpinnerColor> {
                new SpinnerColor((CrystalColor) (-1), null),
                new SpinnerColor(CrystalColor.Blue, "#639BFF"),
                new SpinnerColor(CrystalColor.Red, "#FF4F4F"),
                new SpinnerColor(CrystalColor.Purple, "#FF4FEF"),
            };

            public CrystalColor Name;
            public string Value;

            private SpinnerColor(CrystalColor name, string value) {
                Name = name;
                Value = value;
            }

            public override string ToString() {
                string result = Name == (CrystalColor) (-1) ? "Default" : Name.ToString();
                return result.ToDialogText();
            }
        }
        // ReSharper restore FieldCanBeMadeReadOnly.Global
    }
}