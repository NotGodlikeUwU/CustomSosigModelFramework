using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

// Authoring convenience only. The game creates its armor colliders from the
// addon manifest; BoxColliders added to the FBX preview are not exported.
public sealed class ArmorHitboxScaleWindow : EditorWindow
{
    private const string Number = @"-?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?";
    private static readonly Regex ScaleField = new Regex("\"armorHitboxScale\"\\s*:\\s*" + Number);
    private static readonly Regex HeightField = new Regex("\"heightMeters\"\\s*:\\s*" + Number);

    private string _manifestPath = "";
    private float _scale = 1f;

    [Serializable]
    private sealed class ManifestProbe
    {
        public string id;
        public string mesh;
    }

    [MenuItem("Custom Sosigs/Armor Hitbox Scale")]
    private static void Open()
    {
        GetWindow<ArmorHitboxScaleWindow>("Armor Hitbox Scale");
    }

    private void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "Open an addon's customsosig-model.json, change the global armor collider size, " +
            "then rebuild and reinstall the addon. This also changes the blue Armor Hitbox overlay. " +
            "Ragdoll/body colliders are not changed.", MessageType.Info);

        if (GUILayout.Button("Open addon manifest..."))
        {
            string selected = EditorUtility.OpenFilePanel("Select customsosig-model.json",
                Path.GetDirectoryName(Application.dataPath), "json");
            if (!string.IsNullOrEmpty(selected)) LoadManifest(selected);
        }

        EditorGUILayout.SelectableLabel(_manifestPath, GUILayout.Height(EditorGUIUtility.singleLineHeight));
        if (string.IsNullOrEmpty(_manifestPath)) return;

        _scale = EditorGUILayout.Slider("Armor hitbox scale", _scale, 0.25f, 1.5f);
        EditorGUILayout.LabelField("1.0 = original size; 0.667 = 1.5x smaller; 0.38 = 2.63x smaller.",
            EditorStyles.wordWrappedMiniLabel);
        if (GUILayout.Button("Save scale to manifest")) SaveManifest();
    }

    private void LoadManifest(string path)
    {
        try
        {
            if (!string.Equals(Path.GetFileName(path), "customsosig-model.json", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Select an addon's customsosig-model.json file.");
            string json = File.ReadAllText(path);
            ManifestProbe probe = JsonUtility.FromJson<ManifestProbe>(json);
            if (probe == null || string.IsNullOrEmpty(probe.id) || string.IsNullOrEmpty(probe.mesh))
                throw new InvalidDataException("The selected JSON is not a model addon manifest.");
            Match match = ScaleField.Match(json);
            _scale = match.Success
                ? float.Parse(match.Value.Substring(match.Value.IndexOf(':') + 1), CultureInfo.InvariantCulture)
                : 1f;
            _manifestPath = path;
        }
        catch (Exception exception)
        {
            EditorUtility.DisplayDialog("Cannot open manifest", exception.Message, "OK");
        }
    }

    private void SaveManifest()
    {
        try
        {
            string json = File.ReadAllText(_manifestPath);
            string value = _scale.ToString("0.###", CultureInfo.InvariantCulture);
            Match match = ScaleField.Match(json);
            if (match.Success)
            {
                string replacement = match.Value.Substring(0, match.Value.IndexOf(':') + 1) + " " + value;
                json = json.Substring(0, match.Index) + replacement + json.Substring(match.Index + match.Length);
            }
            else
            {
                Match height = HeightField.Match(json);
                if (!height.Success) throw new InvalidDataException("Missing heightMeters; cannot insert armorHitboxScale.");
                int insertion = height.Index + height.Length;
                while (insertion < json.Length && char.IsWhiteSpace(json[insertion])) insertion++;
                if (insertion < json.Length && json[insertion] == ',')
                    json = json.Insert(insertion + 1, "\n  \"armorHitboxScale\": " + value + ",");
                else
                    json = json.Insert(insertion, ",\n  \"armorHitboxScale\": " + value);
            }
            File.WriteAllText(_manifestPath, json, new UTF8Encoding(false));
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("Armor scale saved",
                "Rebuild/install the addon and restart H3VR to test the new armor colliders.", "OK");
        }
        catch (Exception exception)
        {
            EditorUtility.DisplayDialog("Cannot save manifest", exception.Message, "OK");
        }
    }
}
