using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace CustomSosigReplacer
{
    [Serializable]
    internal sealed class ModelAddon
    {
        public int schemaVersion = 0;
        public string id = null;
        public string displayName = null;
        public string menuName = null;
        public string impactEffect = null;
        public string bloodMask = null;
        public string bloodNoise = null;
        public string mesh = null;
        public float heightMeters = 1.72f;
        public float armorHitboxScale = 1f;
        public AddonMaterial[] materials = null;
        public AddonAnimation[] animations = null;
        public BoneAlias[] boneAliases = null;
        public AddonSurface surface = null;
        public EyeDefinition eyes = null;
        [NonSerialized] public string DirectoryPath = null;

        public string AssetPath(string relative)
        {
            if (string.IsNullOrEmpty(relative) || Path.IsPathRooted(relative)) throw new IOException("Invalid addon asset path: " + relative);
            string root = Path.GetFullPath(DirectoryPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string path = Path.GetFullPath(Path.Combine(root, relative));
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new IOException("Addon asset escapes its package: " + relative);
            if (!File.Exists(path)) throw new FileNotFoundException("Addon asset missing: " + relative, path);
            return path;
        }

        public string AnimationPath(string legacyName)
        {
            string role = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(legacyName));
            if (role.StartsWith("mixamo_")) role = role.Substring(7);
            else if (role.StartsWith("lpsp_")) role = role.Substring(5);
            foreach (var animation in animations)
                if (animation.role == role) return AssetPath(animation.file);
            throw new IOException("Addon '" + id + "' is missing animation role '" + role + "'.");
        }

        public string CanonicalBone(string source)
        {
            if (boneAliases != null)
                foreach (var alias in boneAliases)
                    if (alias.source == source) return "ValveBiped.Bip01_" + alias.role;
            return source;
        }

        public void Validate()
        {
            if (schemaVersion != 1 || string.IsNullOrEmpty(id) || string.IsNullOrEmpty(displayName)) throw new IOException("Invalid addon schema/id/name.");
            if (heightMeters < 0.5f || heightMeters > 3f) throw new IOException("Addon height must be 0.5-3m.");
            // JsonUtility can deserialize absent numeric fields as zero; old
            // addon manifests must retain their previous, unscaled hitboxes.
            if (armorHitboxScale == 0f) armorHitboxScale = 1f;
            if (armorHitboxScale < 0.25f || armorHitboxScale > 1.5f) throw new IOException("Addon armorHitboxScale must be between 0.25 and 1.5.");
            if (!string.IsNullOrEmpty(impactEffect) && !string.Equals(impactEffect, "native", StringComparison.OrdinalIgnoreCase) && !string.Equals(impactEffect, "meat", StringComparison.OrdinalIgnoreCase))
                throw new IOException("Addon impactEffect must be 'native' or 'meat'.");
            if (!string.IsNullOrEmpty(bloodMask)) AssetPath(bloodMask);
            if (!string.IsNullOrEmpty(bloodNoise)) AssetPath(bloodNoise);
            AssetPath(mesh);
            if (materials == null || materials.Length == 0 || animations == null) throw new IOException("Addon materials/animations missing.");
            if (surface != null && (surface.smoothness < 0f || surface.smoothness > 1f || surface.metallic < 0f || surface.metallic > 1f))
                throw new IOException("Addon surface smoothness/metallic must be between 0 and 1.");
            foreach (var material in materials) { AssetPath(material.albedo); if (!string.IsNullOrEmpty(material.normal)) AssetPath(material.normal); }
            var roles = new HashSet<string>();
            foreach (var animation in animations) {
                if (string.IsNullOrEmpty(animation.role) || !roles.Add(animation.role)) throw new IOException("Duplicate/invalid animation role.");
                AssetPath(animation.file);
            }
            if (eyes != null && eyes.enabled && (eyes.materialIndex < 0 || eyes.materialIndex >= materials.Length ||
                eyes.centers == null || eyes.centers.Length == 0 || eyes.radiusU <= 0f || eyes.radiusV <= 0f)) throw new IOException("Invalid addon eye definition.");
            if (eyes != null && eyes.centers != null)
                foreach (var center in eyes.centers)
                    if (center.u - eyes.radiusU < 0f || center.u + eyes.radiusU > 1f || center.v - eyes.radiusV < 0f || center.v + eyes.radiusV > 1f) throw new IOException("Eye regions must fit the normalized UV tile.");
        }

        public void ValidateSkeleton(RuntimeSkinnedAsset asset)
        {
            var names = new HashSet<string>();
            foreach (var bone in asset.Bones)
                if (!names.Add(bone.Name)) throw new IOException("Duplicate canonical skeleton bone: " + bone.Name);
            foreach (string role in new[] { "Pelvis", "Spine", "Spine1", "Spine2", "Neck1", "Head1", "L_Clavicle", "L_UpperArm", "L_Forearm", "L_Hand", "R_Clavicle", "R_UpperArm", "R_Forearm", "R_Hand", "L_Thigh", "L_Calf", "L_Foot", "R_Thigh", "R_Calf", "R_Foot" })
                if (!names.Contains("ValveBiped.Bip01_" + role)) throw new IOException("Humanoid bone role missing: " + role + ". Add boneAliases or fix the skeleton.");
            foreach (var primitive in asset.Meshes)
                if (primitive.MaterialIndex < 0 || primitive.MaterialIndex >= materials.Length) throw new IOException("Model material index is absent from addon manifest.");
        }

        public static Dictionary<string, ModelAddon> Discover(string pluginRoot)
        {
            var addons = new Dictionary<string, ModelAddon>(StringComparer.OrdinalIgnoreCase);
            var duplicates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string manifest in Directory.GetFiles(pluginRoot, "customsosig-model.json", SearchOption.AllDirectories))
            {
                try
                {
                    ModelAddon addon = JsonUtility.FromJson<ModelAddon>(File.ReadAllText(manifest));
                    if (addon == null) throw new IOException("Empty manifest.");
                    addon.DirectoryPath = Path.GetDirectoryName(manifest);
                    addon.Validate();
                    if (addons.ContainsKey(addon.id)) { duplicates.Add(addon.id); Plugin.Log.LogError("Duplicate model addon id: " + addon.id); continue; }
                    addons.Add(addon.id, addon);
                    Plugin.Log.LogInfo("Discovered model addon: " + addon.id + " (" + addon.displayName + ").");
                }
                catch (Exception error) { Plugin.Log.LogWarning("Rejected addon " + manifest + ": " + error.Message); }
            }
            foreach (string duplicate in duplicates) addons.Remove(duplicate);
            return addons;
        }

        public static ModelAddon Find(string pluginRoot, string selectedId)
        {
            ModelAddon selected;
            if (!Discover(pluginRoot).TryGetValue(selectedId, out selected))
                throw new IOException("Selected model addon '" + selectedId + "' is unavailable or duplicated. Install it or change Models.ActiveModel; vanilla Sosigs will be preserved.");
            return selected;
        }
    }

    [Serializable] internal sealed class AddonMaterial { public string name = null; public string albedo = null; public string normal = null; }
    [Serializable] internal sealed class AddonSurface { public float smoothness = 0.5f; public float metallic = 0f; }
    [Serializable] internal sealed class AddonAnimation { public string role = null; public string file = null; }
    [Serializable] internal sealed class BoneAlias { public string source = null; public string role = null; }
    [Serializable] internal sealed class EyeDefinition
    {
        public bool enabled = false;
        public int materialIndex = 0;
        public float radiusU = 0;
        public float radiusV = 0;
        public float darkThreshold = 0.42f;
        public EyeCenter[] centers = null;
    }
    [Serializable] internal sealed class EyeCenter { public float u = 0; public float v = 0; }
}
