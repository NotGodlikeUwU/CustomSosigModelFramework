using System;
using System.Collections.Generic;
using System.Collections;
using System.Globalization;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using FistVR;
using HarmonyLib;
using UnityEngine;

namespace CustomSosigReplacer
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "NotGodlike.h3vr.customsosigmodelframework";
        public const string PluginName = "CustomSosigModelFramework";
        public const string PluginVersion = "1.2.7";
        internal const string VanillaMode = "Sosig";
        internal const string RandomMode = "Random";
        internal static ModelAddon ActiveAddon;
        internal static Material EyeSurfaceMaterial;

        internal static ManualLogSource Log;
        internal static RuntimeSkinnedAsset SkinnedAsset;
        internal static AnimationSet Animations;
        internal static Material[] SharedMaterials;
        internal static Settings RuntimeSettings;
        internal static Plugin Instance;
        private static readonly Dictionary<string, RuntimeModel> Models = new Dictionary<string, RuntimeModel>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<RuntimeModel> RandomPool = new List<RuntimeModel>();
        private ConfigEntry<string> _selectedModel;
        private ConfigEntry<bool> _showArmorHitboxes;
        private Material _armorOverlayMaterial;
        private readonly Dictionary<MatDef, MatDef> _silentImpactMaterials = new Dictionary<MatDef, MatDef>();
        private GameObject _silentMustardBurst;
        private bool _armorOverlayShaderWarningLogged;
        private int _selectionRevision;

        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            RuntimeSettings = BindSettings();
            _selectedModel = Config.Bind("Models", "ActiveModel", "NotGodlike.metrocop", "Wrist Menu selection: Sosig, Random, or an installed model addon ID.");
            _showArmorHitboxes = Config.Bind("Visual", "ShowArmorHitboxes", false, "Show translucent blue protection zones on custom Sosig models. Toggle in Wrist Menu > Custom Sosigs.");

            if (!RuntimeSettings.Enabled)
            {
                Logger.LogInfo("CustomSosigModelFramework is disabled in config.");
                return;
            }

            try
            {
                Models.Clear();
                RandomPool.Clear();
                foreach (ModelAddon addon in ModelAddon.Discover(Paths.PluginPath).Values)
                {
                    try
                    {
                        RuntimeModel model = LoadModel(addon);
                        Models.Add(addon.id, model);
                        RandomPool.Add(model);
                    }
                    catch (Exception error) { Logger.LogError("Rejected model addon '" + addon.id + "' during loading: " + error); }
                }
                RandomPool.Sort((left, right) => string.Compare(left.Addon.id, right.Addon.id, StringComparison.OrdinalIgnoreCase));
                if (!IsValidSelection(_selectedModel.Value))
                {
                    Logger.LogWarning("Selected model '" + _selectedModel.Value + "' is unavailable; using vanilla Sosigs. The missing addon will not appear in Wrist Menu.");
                    _selectedModel.Value = VanillaMode;
                }
                _harmony = new Harmony(PluginGuid);
                _harmony.PatchAll(typeof(Plugin).Assembly);
                Logger.LogInfo("CustomSosigModelFramework loaded " + Models.Count + " model addons. Selected mode: " + _selectedModel.Value + ".");
            }
            catch (Exception exception)
            {
                Logger.LogError("Failed to initialize CustomSosigModelFramework: " + exception);
                enabled = false;
            }
        }

        private RuntimeModel LoadModel(ModelAddon addon)
        {
                ActiveAddon = addon;
                EyeSurfaceMaterial = null;
                string meshPath = ActiveAddon.AssetPath(ActiveAddon.mesh);
                SkinnedAsset = RuntimeSkinnedAsset.Load(meshPath, addon);
                ActiveAddon.ValidateSkeleton(SkinnedAsset);
                Animations = new AnimationSet(
                    RuntimeBoneClip.Load(ActiveAddon.AnimationPath("mixamo_walk.csanim.gz"), SkinnedAsset.Bones.Length),
                    RuntimeBoneClip.Load(ActiveAddon.AnimationPath("mixamo_backward.csanim.gz"), SkinnedAsset.Bones.Length),
                    RuntimeBoneClip.Load(ActiveAddon.AnimationPath("mixamo_strafe_left.csanim.gz"), SkinnedAsset.Bones.Length),
                    RuntimeBoneClip.Load(ActiveAddon.AnimationPath("mixamo_strafe_right.csanim.gz"), SkinnedAsset.Bones.Length),
                    RuntimeBoneClip.Load(ActiveAddon.AnimationPath("mixamo_strafe_left_fast.csanim.gz"), SkinnedAsset.Bones.Length),
                    RuntimeBoneClip.Load(ActiveAddon.AnimationPath("mixamo_strafe_right_fast.csanim.gz"), SkinnedAsset.Bones.Length),
                    RuntimeBoneClip.Load(ActiveAddon.AnimationPath("mixamo_hit.csanim.gz"), SkinnedAsset.Bones.Length),
                    RuntimeBoneClip.Load(ActiveAddon.AnimationPath("mixamo_death.csanim.gz"), SkinnedAsset.Bones.Length),
                    RuntimeBoneClip.Load(ActiveAddon.AnimationPath("mixamo_crouch_idle.csanim.gz"), SkinnedAsset.Bones.Length),
                    RuntimeBoneClip.Load(ActiveAddon.AnimationPath("mixamo_crouch_forward_left.csanim.gz"), SkinnedAsset.Bones.Length),
                    RuntimeBoneClip.Load(ActiveAddon.AnimationPath("mixamo_crouch_forward_right.csanim.gz"), SkinnedAsset.Bones.Length),
                    RuntimeBoneClip.Load(ActiveAddon.AnimationPath("mixamo_crouch_left.csanim.gz"), SkinnedAsset.Bones.Length),
                    RuntimeBoneClip.Load(ActiveAddon.AnimationPath("mixamo_crouch_backward.csanim.gz"), SkinnedAsset.Bones.Length),
                    RuntimeBoneClip.Load(ActiveAddon.AnimationPath("mixamo_stand_to_crouch.csanim.gz"), SkinnedAsset.Bones.Length),
                    RuntimeBoneClip.Load(ActiveAddon.AnimationPath("mixamo_crouch_to_stand.csanim.gz"), SkinnedAsset.Bones.Length),
                    RuntimeBoneClip.Load(ActiveAddon.AnimationPath("mixamo_falling.csanim.gz"), SkinnedAsset.Bones.Length),
                    RuntimeBoneClip.Load(ActiveAddon.AnimationPath("lpsp_relaxed_idle.csanim.gz"), SkinnedAsset.Bones.Length),
                    RuntimeBoneClip.Load(ActiveAddon.AnimationPath("lpsp_rifle_idle.csanim.gz"), SkinnedAsset.Bones.Length),
                    RuntimeBoneClip.Load(ActiveAddon.AnimationPath("lpsp_rifle_aim.csanim.gz"), SkinnedAsset.Bones.Length),
                    RuntimeBoneClip.Load(ActiveAddon.AnimationPath("lpsp_rifle_fire.csanim.gz"), SkinnedAsset.Bones.Length),
                    RuntimeBoneClip.Load(ActiveAddon.AnimationPath("lpsp_rifle_reload.csanim.gz"), SkinnedAsset.Bones.Length),
                    RuntimeBoneClip.Load(ActiveAddon.AnimationPath("lpsp_handgun_idle.csanim.gz"), SkinnedAsset.Bones.Length),
                    RuntimeBoneClip.Load(ActiveAddon.AnimationPath("lpsp_handgun_aim.csanim.gz"), SkinnedAsset.Bones.Length),
                    RuntimeBoneClip.Load(ActiveAddon.AnimationPath("lpsp_handgun_fire.csanim.gz"), SkinnedAsset.Bones.Length),
                    RuntimeBoneClip.Load(ActiveAddon.AnimationPath("lpsp_handgun_reload.csanim.gz"), SkinnedAsset.Bones.Length));
                SharedMaterials = CreateMaterials(RuntimeSettings.UseNormalMaps, RuntimeSettings.Metallic, RuntimeSettings.Smoothness);
                Animations.KnockedDown = RuntimeBoneClip.Load(ActiveAddon.AnimationPath("mixamo_knocked_down.csanim.gz"), SkinnedAsset.Bones.Length);
                foreach (string name in new[] { "rifle_jog", "handgun_jog", "jog_forward", "jog_backward", "jog_left_forward", "jog_right_forward", "jog_left_backward", "jog_right_backward", "breathing" })
                    Animations.CombatMotion.Add(name, RuntimeBoneClip.Load(ActiveAddon.AnimationPath("lpsp_" + name + ".csanim.gz"), SkinnedAsset.Bones.Length));

                BloodImpactVfx bloodVfx = null;
                if (string.Equals(addon.impactEffect, "meat", StringComparison.OrdinalIgnoreCase))
                {
                    bloodVfx = gameObject.AddComponent<BloodImpactVfx>();
                    bloodVfx.Initialize(string.IsNullOrEmpty(addon.bloodMask) ? null : addon.AssetPath(addon.bloodMask),
                        string.IsNullOrEmpty(addon.bloodNoise) ? null : addon.AssetPath(addon.bloodNoise));
                }
                ModelArmorEnvelope.Shape chestEnvelope = default(ModelArmorEnvelope.Shape);
                bool hasChestEnvelope = SkinnedAsset.ArmorEnvelopes != null && SkinnedAsset.ArmorEnvelopes.TryGetValue("Chest", out chestEnvelope);
                Logger.LogInfo("Loaded " + ActiveAddon.displayName + " rig with " + SkinnedAsset.Bones.Length + " bones, " +
                    SkinnedAsset.Meshes.Length + " mesh primitives, and " +
                    (SkinnedAsset.ArmorEnvelopes == null ? 0 : SkinnedAsset.ArmorEnvelopes.Count) +
                    " adaptive armor zones" + (hasChestEnvelope ? " (chest radius " + chestEnvelope.Radius.ToString("F3") + "m)." : "."));
                return new RuntimeModel(addon, SkinnedAsset, Animations, SharedMaterials, EyeSurfaceMaterial, bloodVfx);
        }

        private void OnDestroy()
        {
            if (_harmony != null)
            {
                _harmony.UnpatchSelf();
            }
            if (_armorOverlayMaterial != null) Destroy(_armorOverlayMaterial);
            foreach (MatDef material in _silentImpactMaterials.Values)
                if (material != null) Destroy(material);
            _silentImpactMaterials.Clear();
            if (Instance == this) Instance = null;
        }

        internal static bool HasModels { get { return Models.Count > 0; } }
        internal static string SelectedModel { get { return Instance != null && Instance._selectedModel != null ? Instance._selectedModel.Value : VanillaMode; } }
        internal static bool ShowArmorHitboxes { get { return Instance != null && Instance._showArmorHitboxes != null && Instance._showArmorHitboxes.Value; } }
        internal static IEnumerable<RuntimeModel> InstalledModels { get { return RandomPool; } }

        internal static MatDef GetSilentImpactMaterial(MatDef source)
        {
            if (Instance == null) return source;
            if (source == null) source = PM.DefaultMatDef;
            if (source == null) return null;
            MatDef material;
            if (Instance._silentImpactMaterials.TryGetValue(source, out material)) return material;
            // MatDef also defines penetration and damage behavior. Never mutate the
            // game's shared asset or replace its ballistic type just for VFX.
            material = UnityEngine.Object.Instantiate(source);
            material.name = source.name + " - Custom Sosig silent impact";
            material.ImpactEffectType = BallisticImpactEffectType.None;
            material.BulletHoleType = BulletHoleDecalType.None;
            Instance._silentImpactMaterials.Add(source, material);
            return material;
        }

        internal static void SpawnBloodImpact(string addonId, Vector3 point, Vector3 normal)
        {
            RuntimeModel model;
            if (addonId != null && Models.TryGetValue(addonId, out model) && model.BloodVfx != null)
                model.BloodVfx.Spawn(point, normal);
        }

        internal static GameObject SilentMustardBurst
        {
            get
            {
                if (Instance == null) return null;
                if (Instance._silentMustardBurst == null)
                {
                    GameObject prefab = new GameObject("Custom Sosig silent mustard burst template");
                    prefab.transform.SetParent(Instance.transform, false);
                    prefab.transform.localPosition = Vector3.zero;
                    prefab.AddComponent<SilentMustardBurst>();
                    Instance._silentMustardBurst = prefab;
                }
                return Instance._silentMustardBurst;
            }
        }

        internal static bool IsSilentMustardBurstTemplate(GameObject candidate)
        {
            return Instance != null && candidate == Instance._silentMustardBurst;
        }

        internal static Material GetArmorOverlayMaterial()
        {
            if (Instance == null) return null;
            if (Instance._armorOverlayMaterial != null) return Instance._armorOverlayMaterial;
            Shader shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null) shader = Shader.Find("Unlit/Transparent");
            if (shader == null)
            {
                if (!Instance._armorOverlayShaderWarningLogged)
                {
                    Instance._armorOverlayShaderWarningLogged = true;
                    Log.LogWarning("No transparent shader is available for armor hitbox overlays.");
                }
                return null;
            }
            Material material = new Material(shader);
            material.name = "Custom Sosig translucent blue armor hitboxes";
            material.color = new Color(0.03f, 0.38f, 1f, 0.28f);
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            material.SetInt("_ZWrite", 0);
            // The collision shells are inside the opaque character mesh.
            material.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
            material.renderQueue = 4000;
            Instance._armorOverlayMaterial = material;
            return material;
        }

        internal static void ToggleArmorHitboxes()
        {
            if (Instance == null || Instance._showArmorHitboxes == null) return;
            Instance._showArmorHitboxes.Value = !Instance._showArmorHitboxes.Value;
            Instance.Config.Save();
            foreach (SosigVisualProxy proxy in UnityEngine.Object.FindObjectsOfType<SosigVisualProxy>())
                if (proxy != null) proxy.RefreshArmorOverlays();
            Log.LogInfo("Armor hitbox visualization: " + (ShowArmorHitboxes ? "on" : "off") + ".");
        }

        private static bool IsValidSelection(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            return string.Equals(id, VanillaMode, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(id, RandomMode, StringComparison.OrdinalIgnoreCase) || Models.ContainsKey(id);
        }

        internal static void SelectModel(string id)
        {
            if (Instance == null || !IsValidSelection(id) || string.Equals(SelectedModel, id, StringComparison.OrdinalIgnoreCase)) return;
            Instance._selectedModel.Value = id;
            Instance.Config.Save();
            int revision = ++Instance._selectionRevision;
            Sosig[] sosigs = UnityEngine.Object.FindObjectsOfType<Sosig>();
            foreach (Sosig sosig in sosigs)
            {
                if (sosig == null) continue;
                SosigVisualProxy proxy = sosig.GetComponent<SosigVisualProxy>();
                if (proxy != null) { proxy.Detach(); UnityEngine.Object.Destroy(proxy); }
            }
            if (!string.Equals(id, VanillaMode, StringComparison.OrdinalIgnoreCase))
                Instance.StartCoroutine(Instance.AttachExistingNextFrame(sosigs, revision));
            Log.LogInfo("Wrist Menu selected " + id + "; updated " + sosigs.Length + " existing Sosigs.");
        }

        private IEnumerator AttachExistingNextFrame(Sosig[] sosigs, int revision)
        {
            yield return null;
            if (revision != _selectionRevision) yield break;
            foreach (Sosig sosig in sosigs)
                if (sosig != null) TryAttachProxy(sosig, "Wrist Menu");
        }

        internal static void TryAttachProxy(Sosig sosig, string spawnSource)
        {
            if (sosig == null || RuntimeSettings == null || !RuntimeSettings.Enabled || string.Equals(SelectedModel, VanillaMode, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            SosigVisualProxy proxy = null;
            try
            {
                if (sosig.GetComponent<SosigVisualProxy>() != null)
                {
                    return;
                }

                RuntimeModel model;
                if (string.Equals(SelectedModel, RandomMode, StringComparison.OrdinalIgnoreCase))
                {
                    if (RandomPool.Count == 0) return;
                    model = RandomPool[UnityEngine.Random.Range(0, RandomPool.Count)];
                }
                else if (!Models.TryGetValue(SelectedModel, out model)) return;
                proxy = sosig.gameObject.AddComponent<SosigVisualProxy>();
                proxy.Initialize(sosig, model.Asset, model.Animations, model.Materials, model.EyeMaterial,
                    model.Addon.displayName, model.Addon.heightMeters, model.Addon.armorHitboxScale, RuntimeSettings,
                    string.Equals(model.Addon.impactEffect, "meat", StringComparison.OrdinalIgnoreCase), model.Addon.id);
                Log.LogInfo("Attached custom visual proxy to Sosig '" + sosig.name + "' via " + spawnSource + ".");
            }
            catch (Exception exception)
            {
                if (proxy != null) { proxy.Detach(); UnityEngine.Object.Destroy(proxy); }
                Log.LogError("Could not attach a custom visual to Sosig '" + sosig.name + "' via " + spawnSource + ": " + exception);
            }
        }

        internal sealed class RuntimeModel
        {
            internal readonly ModelAddon Addon;
            internal readonly RuntimeSkinnedAsset Asset;
            internal readonly AnimationSet Animations;
            internal readonly Material[] Materials;
            internal readonly Material EyeMaterial;
            internal readonly BloodImpactVfx BloodVfx;

            internal RuntimeModel(ModelAddon addon, RuntimeSkinnedAsset asset, AnimationSet animations, Material[] materials, Material eyeMaterial, BloodImpactVfx bloodVfx)
            {
                Addon = addon; Asset = asset; Animations = animations; Materials = materials; EyeMaterial = eyeMaterial; BloodVfx = bloodVfx;
            }
        }

        private Settings BindSettings()
        {
            Settings settings = new Settings();
            settings.Enabled = Config.Bind("General", "Enabled", true, "Replace every newly spawned Sosig visual.").Value;
            settings.HideOriginal = Config.Bind("General", "HideOriginal", true, "Hide vanilla Sosig body and wearable renderers, but keep held weapons visible.").Value;
            settings.UseCustomHitboxes = Config.Bind("General", "UseCustomHitboxes", true, "Put COD Zombies-style damage hitboxes on the animated custom skeleton.").Value;
            settings.Height = Config.Bind("Visual", "HeightMeters", 1.72f, "Rendered custom model height in meters.").Value;
            settings.FeetBelowLowerLink = Config.Bind("Visual", "FeetBelowLowerLink", 0.43f, "Distance from the lower Sosig link center to the model feet.").Value;
            settings.YawDegrees = Config.Bind("Visual", "YawDegrees", 0f, "Extra rotation around the model's vertical axis.").Value;
            settings.ColorHex = Config.Bind("Visual", "Color", "#FFFFFF", "RGB or RGBA tint applied over the Model addon textures.").Value;
            settings.Metallic = Config.Bind("Visual", "Metallic", 0.2f, "Standard shader metallic value.").Value;
            settings.Smoothness = Config.Bind("Visual", "Smoothness", 0.72f, "Standard shader smoothness value.").Value;
            settings.UseNormalMaps = Config.Bind("Visual", "UseNormalMaps", true, "Load the supplied body and mask normal maps.").Value;
            settings.BlueEyeEmission = Config.Bind("Visual", "BlueEyeEmission", true, "Enable blue emissive Model addon eye lenses; bloom depends on the game camera.").Value;
            settings.EyeEmissionIntensity = Config.Bind("Visual", "EyeEmissionIntensity", 3f, "Blue lens emission intensity. Does not add scene lights.").Value;
            settings.FollowSharpness = Config.Bind("Motion", "FollowSharpness", 28f, "How quickly the proxy follows the Sosig links.").Value;
            settings.EnableProceduralMotion = Config.Bind("Motion", "EnableProceduralMotion", true, "Add subtle bob and sway while the Sosig moves.").Value;
            settings.BobMeters = Config.Bind("Motion", "BobMeters", 0f, "Optional procedural vertical bob. Keep at zero for a floor-locked model.").Value;
            settings.SwayDegrees = Config.Bind("Motion", "SwayDegrees", 1.8f, "Maximum procedural roll sway.").Value;
            settings.GaitDegrees = Config.Bind("Motion", "GaitDegrees", 24f, "Maximum procedural leg swing angle.").Value;
            settings.HandIkWeight = Config.Bind("MotionV2", "HandIkCorrectionWeight", 0.3f, "Small final hand-to-weapon correction applied after authored firearm animation.").Value;
            settings.JointLimitWeight = Config.Bind("MotionV2", "JointLimitCorrectionWeight", 0.15f, "Safety correction after authored animation and hand IK. Keep low so valid weapon poses are preserved.").Value;
            settings.DriveHeldWeaponsFromAnimation = Config.Bind("MotionV3", "DriveHeldWeaponsFromAnimation", true, "Align the custom model's visual hands to native Sosig gun targets. Native aiming and firing remain authoritative.").Value;
            settings.HandTargetFollowSharpness = Config.Bind("MotionV3", "HandTargetFollowSharpness", 22f, "Legacy compatibility setting. Native gun targets are no longer driven by authored animation.").Value;
            return settings;
        }

        private static Material[] CreateMaterials(bool useNormalMaps, float metallic, float smoothness)
        {
            Color tint = ParseColor(RuntimeSettings.ColorHex);
            if (ActiveAddon.surface != null)
            {
                metallic = ActiveAddon.surface.metallic;
                smoothness = ActiveAddon.surface.smoothness;
            }
            Material[] materials = new Material[ActiveAddon.materials.Length];
            for (int index = 0; index < materials.Length; index++)
            {
                var definition = ActiveAddon.materials[index];
                materials[index] = CreateMaterial(definition.name ?? ActiveAddon.displayName,
                    ActiveAddon.AssetPath(definition.albedo),
                    string.IsNullOrEmpty(definition.normal) ? string.Empty : ActiveAddon.AssetPath(definition.normal),
                    tint, useNormalMaps, metallic, smoothness);
            }
            bool emitEyes = RuntimeSettings.BlueEyeEmission && ActiveAddon.eyes != null && ActiveAddon.eyes.enabled;
            int eyeIndex = emitEyes ? ActiveAddon.eyes.materialIndex : 0;
            if (emitEyes) ConfigureEyeSurfaces((Texture2D)materials[eyeIndex].mainTexture);
            if (emitEyes && materials[eyeIndex].HasProperty("_EmissionMap"))
            {
                Texture2D eyes = CreateEyeEmissionMask((Texture2D)materials[eyeIndex].mainTexture);
                materials[eyeIndex].SetTexture("_EmissionMap", eyes);
                materials[eyeIndex].SetColor("_EmissionColor", new Color(0.015f, 0.32f, 1f) * Mathf.Max(0f, RuntimeSettings.EyeEmissionIntensity));
                materials[eyeIndex].EnableKeyword("_EMISSION");
                materials[eyeIndex].globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            return materials;
        }

        private static void ConfigureEyeSurfaces(Texture2D albedo)
        {
            Shader shader = Shader.Find("Unlit/Color");
            if (shader == null) { Log.LogWarning("Unlit/Color unavailable; eye surface fallback unavailable."); return; }
            EyeSurfaceMaterial = new Material(shader);
            EyeSurfaceMaterial.color = new Color(0.02f, 0.4f, 1f) * Mathf.Max(1f, RuntimeSettings.EyeEmissionIntensity);
            foreach (var definition in SkinnedAsset.Meshes)
            {
                if (definition.MaterialIndex != ActiveAddon.eyes.materialIndex) continue;
                Mesh mesh = definition.Mesh;
                Vector2[] uv = mesh.uv;
                int[] triangles = mesh.triangles;
                var eyes = new List<int>(); var face = new List<int>();
                for (int index = 0; index < triangles.Length; index += 3)
                {
                    Vector2 center = (uv[triangles[index]] + uv[triangles[index + 1]] + uv[triangles[index + 2]]) / 3f;
                    center.x = Mathf.Repeat(center.x, 1f);
                    center.y = Mathf.Repeat(center.y, 1f);
                    bool inRegion = false;
                    foreach (var region in ActiveAddon.eyes.centers)
                    {
                        float dx = (center.x - region.u) / ActiveAddon.eyes.radiusU;
                        float dy = (center.y - region.v) / ActiveAddon.eyes.radiusV;
                        if (dx * dx + dy * dy < 1f) inRegion = true;
                    }
                    Color pixel = albedo.GetPixelBilinear(center.x, center.y);
                    bool lens = inRegion && Mathf.Max(pixel.r, Mathf.Max(pixel.g, pixel.b)) < ActiveAddon.eyes.darkThreshold;
                    var destination = lens ? eyes : face;
                    destination.Add(triangles[index]); destination.Add(triangles[index + 1]); destination.Add(triangles[index + 2]);
                }
                if (eyes.Count == 0) { Log.LogWarning("No eye lens triangles selected."); continue; }
                mesh.subMeshCount = 2;
                mesh.SetTriangles(face.ToArray(), 0);
                mesh.SetTriangles(eyes.ToArray(), 1);
                Log.LogInfo("Unlit blue eye surfaces: " + eyes.Count / 3 + " lens triangles.");
            }
        }

        private static Texture2D CreateEyeEmissionMask(Texture2D albedo)
        {
            const int size = 512;
            Texture2D mask = new Texture2D(size, size, TextureFormat.RGB24, true, true);
            mask.name = "Model addon blue eye lens emission";
            mask.wrapMode = TextureWrapMode.Repeat;
            Color[] pixels = new Color[size * size];
            Vector2[] centers = new Vector2[ActiveAddon.eyes.centers.Length];
            for (int index = 0; index < centers.Length; index++) centers[index] = new Vector2(ActiveAddon.eyes.centers[index].u, ActiveAddon.eyes.centers[index].v);
            foreach (Vector2 center in centers)
            {
                float radiusU = ActiveAddon.eyes.radiusU;
                float radiusV = ActiveAddon.eyes.radiusV;
                for (int y = Mathf.FloorToInt((center.y - radiusV) * size); y <= Mathf.CeilToInt((center.y + radiusV) * size); y++)
                for (int x = Mathf.FloorToInt((center.x - radiusU) * size); x <= Mathf.CeilToInt((center.x + radiusU) * size); x++)
                {
                    float u = (x + 0.5f) / size; float v = (y + 0.5f) / size;
                    float dx = (u - center.x) / radiusU; float dy = (v - center.y) / radiusV;
                    if (dx * dx + dy * dy > 1f) continue;
                    Color source = albedo.GetPixelBilinear(u, v);
                    float darkness = Mathf.Max(source.r, Mathf.Max(source.g, source.b));
                    float intensity = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(ActiveAddon.eyes.darkThreshold - 0.1f, ActiveAddon.eyes.darkThreshold, darkness));
                    pixels[y * size + x] = Color.white * intensity;
                }
            }
            mask.SetPixels(pixels);
            mask.Apply(true, true);
            return mask;
        }

        private static Material CreateMaterial(string name, string albedoPath, string normalPath, Color tint, bool useNormalMaps, float metallic, float smoothness)
        {
            Shader shader = Shader.Find("Standard");
            if (shader == null)
            {
                shader = Shader.Find("Diffuse");
            }
            if (shader == null)
            {
                throw new InvalidOperationException("Neither Standard nor Diffuse shader is available.");
            }

            Material material = new Material(shader);
            material.name = "CustomSosigModelFramework - " + name;
            material.color = tint;
            material.mainTexture = LoadTexture(albedoPath, false);
            if (useNormalMaps && material.HasProperty("_BumpMap") && File.Exists(normalPath))
            {
                material.SetTexture("_BumpMap", LoadTexture(normalPath, true));
                material.EnableKeyword("_NORMALMAP");
            }
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", Mathf.Clamp01(metallic));
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", Mathf.Clamp01(smoothness));
            return material;
        }

        private static Texture2D LoadTexture(string path, bool linear)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("Model addon texture was not found.", path);
            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGB24, true, linear);
            if (!texture.LoadImage(File.ReadAllBytes(path))) throw new IOException("Unity failed to decode texture: " + path);
            if (linear)
            {
                // Desktop Standard shaders unpack tangent-space X from alpha
                // (DXT5nm convention). Raw PNG alpha=1 would bend every normal.
                Color[] pixels = texture.GetPixels();
                for (int index = 0; index < pixels.Length; index++) pixels[index].a = pixels[index].r;
                Texture2D packed = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, true, true);
                packed.SetPixels(pixels);
                packed.Apply(true, false);
                UnityEngine.Object.Destroy(texture);
                texture = packed;
            }
            texture.name = Path.GetFileNameWithoutExtension(path);
            texture.wrapMode = TextureWrapMode.Repeat;
            texture.anisoLevel = 4;
            return texture;
        }

        private static Color ParseColor(string text)
        {
            string value = (text ?? string.Empty).Trim().TrimStart('#');
            if (value.Length != 6 && value.Length != 8)
            {
                return new Color(0.91f, 0.09f, 0.17f, 1f);
            }

            uint packed;
            if (!uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out packed))
            {
                return new Color(0.91f, 0.09f, 0.17f, 1f);
            }

            if (value.Length == 6)
            {
                return new Color(((packed >> 16) & 255) / 255f, ((packed >> 8) & 255) / 255f, (packed & 255) / 255f, 1f);
            }

            return new Color(((packed >> 24) & 255) / 255f, ((packed >> 16) & 255) / 255f, ((packed >> 8) & 255) / 255f, (packed & 255) / 255f);
        }

        internal sealed class Settings
        {
            public bool Enabled;
            public bool HideOriginal;
            public bool UseCustomHitboxes;
            public float Height;
            public float FeetBelowLowerLink;
            public float YawDegrees;
            public string ColorHex;
            public float Metallic;
            public float Smoothness;
            public bool UseNormalMaps;
            public bool BlueEyeEmission;
            public float EyeEmissionIntensity;
            public float FollowSharpness;
            public bool EnableProceduralMotion;
            public float BobMeters;
            public float SwayDegrees;
            public float GaitDegrees;
            public float HandIkWeight;
            public float JointLimitWeight;
            public bool DriveHeldWeaponsFromAnimation;
            public float HandTargetFollowSharpness;
        }

        internal sealed class AnimationSet
        {
            public readonly Dictionary<string, RuntimeBoneClip> CombatMotion = new Dictionary<string, RuntimeBoneClip>();
            public readonly RuntimeBoneClip Walk;
            public readonly RuntimeBoneClip Backwards;
            public readonly RuntimeBoneClip StrafeLeft;
            public readonly RuntimeBoneClip StrafeRight;
            public readonly RuntimeBoneClip StrafeLeftFast;
            public readonly RuntimeBoneClip StrafeRightFast;
            public readonly RuntimeBoneClip Hit;
            public readonly RuntimeBoneClip Death;
            public readonly RuntimeBoneClip CrouchIdle;
            public readonly RuntimeBoneClip CrouchForwardLeft;
            public readonly RuntimeBoneClip CrouchForwardRight;
            public readonly RuntimeBoneClip CrouchLeft;
            public readonly RuntimeBoneClip CrouchBackward;
            public readonly RuntimeBoneClip StandToCrouch;
            public readonly RuntimeBoneClip CrouchToStand;
            public readonly RuntimeBoneClip Falling;
            public RuntimeBoneClip KnockedDown;
            public readonly RuntimeBoneClip RelaxedIdle;
            public readonly RuntimeBoneClip RifleIdle;
            public readonly RuntimeBoneClip RifleAim;
            public readonly RuntimeBoneClip RifleFire;
            public readonly RuntimeBoneClip RifleReload;
            public readonly RuntimeBoneClip HandgunIdle;
            public readonly RuntimeBoneClip HandgunAim;
            public readonly RuntimeBoneClip HandgunFire;
            public readonly RuntimeBoneClip HandgunReload;

            public AnimationSet(RuntimeBoneClip walk, RuntimeBoneClip backwards, RuntimeBoneClip strafeLeft, RuntimeBoneClip strafeRight, RuntimeBoneClip strafeLeftFast, RuntimeBoneClip strafeRightFast, RuntimeBoneClip hit, RuntimeBoneClip death, RuntimeBoneClip crouchIdle, RuntimeBoneClip crouchForwardLeft, RuntimeBoneClip crouchForwardRight, RuntimeBoneClip crouchLeft, RuntimeBoneClip crouchBackward, RuntimeBoneClip standToCrouch, RuntimeBoneClip crouchToStand, RuntimeBoneClip falling, RuntimeBoneClip relaxedIdle, RuntimeBoneClip rifleIdle, RuntimeBoneClip rifleAim, RuntimeBoneClip rifleFire, RuntimeBoneClip rifleReload, RuntimeBoneClip handgunIdle, RuntimeBoneClip handgunAim, RuntimeBoneClip handgunFire, RuntimeBoneClip handgunReload)
            {
                Walk = walk;
                Backwards = backwards;
                StrafeLeft = strafeLeft;
                StrafeRight = strafeRight;
                StrafeLeftFast = strafeLeftFast;
                StrafeRightFast = strafeRightFast;
                Hit = hit;
                Death = death;
                CrouchIdle = crouchIdle;
                CrouchForwardLeft = crouchForwardLeft;
                CrouchForwardRight = crouchForwardRight;
                CrouchLeft = crouchLeft;
                CrouchBackward = crouchBackward;
                StandToCrouch = standToCrouch;
                CrouchToStand = crouchToStand;
                Falling = falling;
                RelaxedIdle = relaxedIdle;
                RifleIdle = rifleIdle;
                RifleAim = rifleAim;
                RifleFire = rifleFire;
                RifleReload = rifleReload;
                HandgunIdle = handgunIdle;
                HandgunAim = handgunAim;
                HandgunFire = handgunFire;
                HandgunReload = handgunReload;
            }
        }
    }
}
