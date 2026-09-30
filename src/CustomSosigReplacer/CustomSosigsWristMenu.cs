using System;
using System.Collections.Generic;
using FistVR;
using HarmonyLib;
using UnityEngine;

namespace CustomSosigReplacer
{
    [HarmonyPatch(typeof(FVRWristMenu2), "Awake")]
    internal static class CustomSosigsWristMenuPatch
    {
        private static void Postfix(FVRWristMenu2 __instance)
        {
            if (__instance == null || __instance.Sections == null || __instance.Sections.Count == 0 ||
                __instance.BaseButton == null || Plugin.Instance == null) return;

            GameObject sectionObject = null;
            try
            {
                sectionObject = new GameObject("Custom Sosigs", typeof(RectTransform));
                RectTransform reference = __instance.Sections[0].GetComponent<RectTransform>();
                RectTransform rect = sectionObject.GetComponent<RectTransform>();
                rect.SetParent(__instance.Sections[0].transform.parent, false);
                if (reference != null)
                {
                    rect.anchorMin = reference.anchorMin;
                    rect.anchorMax = reference.anchorMax;
                    rect.pivot = reference.pivot;
                    rect.anchoredPosition = reference.anchoredPosition;
                    rect.sizeDelta = reference.sizeDelta;
                    rect.localRotation = reference.localRotation;
                    rect.localScale = reference.localScale;
                }

                CustomSosigsSection section = sectionObject.AddComponent<CustomSosigsSection>();
                section.Menu = __instance;
                section.ButtonText = "Custom Sosigs";
                section.Build(__instance.BaseButton);
                sectionObject.SetActive(false);
                __instance.Sections.Add(section);
                __instance.RegenerateButtons();
                Plugin.Log.LogInfo("Registered Custom Sosigs Wrist Menu section with " + section.ModelCount + " installed model choices.");
            }
            catch (Exception error)
            {
                if (sectionObject != null) UnityEngine.Object.Destroy(sectionObject);
                Plugin.Log.LogError("Could not register Custom Sosigs Wrist Menu section: " + error);
            }
        }
    }

    internal sealed class CustomSosigsSection : FVRWristMenuSection
    {
        private const int ChoiceColumns = 3;
        private const float ChoiceColumnSpacing = 115f;
        private const float ChoiceRowSpacing = 60f;
        private const float FirstChoiceRowY = 90f;
        private readonly List<Choice> _choices = new List<Choice>();
        private UnityEngine.UI.Text _armorToggleLabel;
        internal int ModelCount { get { return Math.Max(0, _choices.Count - 2); } }

        internal void Build(GameObject template)
        {
            AddChoice(template, Plugin.VanillaMode, "Sosig");
            AddChoice(template, Plugin.RandomMode, "Random");
            foreach (Plugin.RuntimeModel model in Plugin.InstalledModels)
            {
                string title = string.IsNullOrEmpty(model.Addon.menuName) ? model.Addon.displayName : model.Addon.menuName;
                AddChoice(template, model.Addon.id, title);
            }
            AddArmorToggle(template);
            RefreshSelection();
        }

        private void AddArmorToggle(GameObject template)
        {
            GameObject item = Instantiate(template, transform, false);
            item.name = "Armor Hitbox Visibility Toggle";
            FVRWristMenuSectionButton sectionButton = item.GetComponent<FVRWristMenuSectionButton>();
            _armorToggleLabel = sectionButton != null ? sectionButton.ButtonText : item.GetComponent<UnityEngine.UI.Text>();
            UnityEngine.UI.Button button = item.GetComponent<UnityEngine.UI.Button>();
            RectTransform rect = item.GetComponent<RectTransform>();
            if (_armorToggleLabel == null || button == null || rect == null)
            {
                Destroy(item);
                throw new InvalidOperationException("H3VR wrist button template cannot create the armor hitbox toggle.");
            }
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            // The H3VR template has fixed-size child graphics; changing only
            // the root sizeDelta leaves the visible button at its old size.
            rect.sizeDelta = new Vector2(108f, 55f);
            rect.localScale *= 0.6f;
            int choiceRows = (_choices.Count + ChoiceColumns - 1) / ChoiceColumns;
            rect.anchoredPosition = new Vector2(0f, FirstChoiceRowY - choiceRows * ChoiceRowSpacing);
            _armorToggleLabel.fontSize = 17;
            UnityEngine.UI.Image background = item.GetComponent<UnityEngine.UI.Image>();
            if (background != null) background.color = new Color(0.08f, 0.22f, 0.35f, 0.9f);
            button.onClick = new UnityEngine.UI.Button.ButtonClickedEvent();
            button.onClick.AddListener(() => { Plugin.ToggleArmorHitboxes(); RefreshSelection(); });
            item.SetActive(true);
        }

        private void AddChoice(GameObject template, string id, string label)
        {
            GameObject item = Instantiate(template, transform, false);
            item.name = "Model Choice - " + id;
            FVRWristMenuSectionButton sectionButton = item.GetComponent<FVRWristMenuSectionButton>();
            UnityEngine.UI.Text text = sectionButton != null ? sectionButton.ButtonText : item.GetComponent<UnityEngine.UI.Text>();
            UnityEngine.UI.Button button = item.GetComponent<UnityEngine.UI.Button>();
            RectTransform rect = item.GetComponent<RectTransform>();
            if (text == null || button == null || rect == null)
            {
                Destroy(item);
                throw new InvalidOperationException("H3VR wrist button template is missing a Text, Button, or RectTransform.");
            }
            int index = _choices.Count;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(108f, 55f);
            rect.anchoredPosition = new Vector2((index % ChoiceColumns - 1) * ChoiceColumnSpacing,
                FirstChoiceRowY - (index / ChoiceColumns) * ChoiceRowSpacing);
            text.text = label;
            if (label.Length > 7)
            {
                RectTransform labelRect = text.rectTransform;
                // H3VR's ButtonText can live on the button root. Stretching
                // that RectTransform would also move the entire button; both
                // long-named addons would collapse onto the same position.
                if (labelRect != rect)
                {
                    labelRect.anchorMin = Vector2.zero;
                    labelRect.anchorMax = Vector2.one;
                    labelRect.offsetMin = new Vector2(3f, 0f);
                    labelRect.offsetMax = new Vector2(-3f, 0f);
                }
                text.fontSize = 14;
                text.resizeTextForBestFit = true;
                text.resizeTextMinSize = 10;
                text.resizeTextMaxSize = 14;
                text.horizontalOverflow = HorizontalWrapMode.Wrap;
                text.verticalOverflow = VerticalWrapMode.Truncate;
            }
            // The instantiated section-button prefab has a persistent click target.
            // Replace its UnityEvent so the original section navigation cannot run.
            button.onClick = new UnityEngine.UI.Button.ButtonClickedEvent();
            button.onClick.AddListener(() => { Plugin.SelectModel(id); RefreshSelection(); });
            item.SetActive(true);
            _choices.Add(new Choice(id, text));
        }

        public override void Enable() { RefreshSelection(); }

        private void RefreshSelection()
        {
            foreach (Choice choice in _choices)
                if (choice.Label != null)
                    choice.Label.color = string.Equals(choice.Id, Plugin.SelectedModel, StringComparison.OrdinalIgnoreCase)
                        ? new Color(0.2f, 0.85f, 1f, 1f) : Color.white;
            if (_armorToggleLabel != null)
            {
                _armorToggleLabel.text = "Armor Hitbox";
                _armorToggleLabel.color = Plugin.ShowArmorHitboxes
                    ? new Color(0.2f, 0.85f, 1f, 1f) : Color.white;
            }
        }

        private sealed class Choice
        {
            internal readonly string Id;
            internal readonly UnityEngine.UI.Text Label;
            internal Choice(string id, UnityEngine.UI.Text label) { Id = id; Label = label; }
        }
    }
}
