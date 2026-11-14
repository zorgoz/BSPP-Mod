using System;
using System.Reflection;
using HarmonyLib;
using IPA;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using IPALogger = IPA.Logging.Logger;

namespace TestMod
{
    [Plugin(RuntimeOptions.SingleStartInit)]
    internal class Plugin
    {
        private readonly Harmony _harmony;

        internal static IPALogger Log { get; private set; }

        [Init]
        public Plugin(IPALogger logger)
        {
            Log = logger;
            _harmony = new Harmony("TestMod");
            _harmony.PatchAll(typeof(Plugin).Assembly);
            Log.Info("TestMod initialized.");
        }

        [OnStart]
        public void OnStart()
        {
            Log.Info("TestMod started.");
        }

        [OnExit]
        public void OnExit()
        {
            _harmony.UnpatchSelf();
            Log.Info("TestMod shutting down.");
        }
    }

    [HarmonyPatch]
    internal static class MainMenuButtonPatch
    {
        private const string ButtonObjectName = "TestMod.MultiuserButton";

        private static MethodBase TargetMethod()
        {
            var controllerType = AccessTools.TypeByName("MainMenuViewController");
            if (controllerType == null)
                throw new TypeLoadException("Could not find MainMenuViewController.");

            var method = AccessTools.Method(controllerType, "DidActivate",
                new[] { typeof(bool), typeof(bool), typeof(bool) });
            if (method == null)
                throw new MissingMethodException(controllerType.FullName, "DidActivate");

            return method;
        }

        private static void Postfix(object __instance, bool firstActivation)
        {
            if (!firstActivation)
                return;

            var controllerType = __instance.GetType();
            var soloButton = GetButton(controllerType, __instance, "_soloButton");
            var multiplayerButton = GetButton(controllerType, __instance, "_multiplayerButton");
            var campaignButton = GetButton(controllerType, __instance, "_campaignButton");
            var partyButton = GetButton(controllerType, __instance, "_partyButton");

            var parent = soloButton.transform.parent;
            if (parent == null ||
                multiplayerButton.transform.parent != parent ||
                campaignButton.transform.parent != parent ||
                partyButton.transform.parent != parent)
                throw new InvalidOperationException("Main menu buttons do not share a parent.");

            if (parent.Find(ButtonObjectName) != null)
                return;

            var buttonObject = UnityEngine.Object.Instantiate(soloButton.gameObject, parent, false);
            buttonObject.name = ButtonObjectName;

            var button = buttonObject.GetComponent<Button>();
            if (button == null)
                throw new MissingComponentException("The cloned main menu object has no Button component.");

            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(() => Plugin.Log.Info("Multiuser button clicked."));
            var colors = button.colors;
            colors.normalColor = new Color32(205, 241, 255, 255);
            colors.highlightedColor = new Color32(82, 222, 255, 255);
            colors.pressedColor = new Color32(48, 177, 211, 255);
            colors.selectedColor = new Color32(140, 232, 255, 255);
            button.colors = colors;

            var label = buttonObject.GetComponentInChildren<TMP_Text>(true);
            if (label == null)
                throw new MissingComponentException("The cloned main menu button has no TMP_Text label.");
            label.richText = true;
            label.text = "<color=#55E7FF>\u25CF</color>  Multiuser";
            AddAccent(buttonObject);

            buttonObject.transform.SetSiblingIndex(partyButton.transform.GetSiblingIndex() + 1);
            var layout = parent.GetComponent<LayoutGroup>();
            if (layout != null)
            {
                var parentRect = parent as RectTransform;
                if (parentRect != null)
                    LayoutRebuilder.MarkLayoutForRebuild(parentRect);
            }
            else
            {
                var partyRect = partyButton.transform as RectTransform;
                var campaignRect = campaignButton.transform as RectTransform;
                var newRect = buttonObject.transform as RectTransform;
                if (partyRect == null || campaignRect == null || newRect == null)
                    throw new InvalidOperationException("Main menu buttons are missing RectTransform components.");

                var step = partyRect.anchoredPosition - campaignRect.anchoredPosition;
                if (step.sqrMagnitude < 1f)
                    step = Vector2.down * (partyRect.rect.height + 8f);
                newRect.anchoredPosition = partyRect.anchoredPosition + step;
            }

            Plugin.Log.Info("Added the Multiuser button to the main menu.");
        }

        private static void AddAccent(GameObject buttonObject)
        {
            var accent = new GameObject(ButtonObjectName + ".Accent",
                typeof(RectTransform), typeof(Image));
            var accentRect = (RectTransform)accent.transform;
            accentRect.SetParent(buttonObject.transform, false);
            accentRect.anchorMin = new Vector2(0f, 0.12f);
            accentRect.anchorMax = new Vector2(0f, 0.88f);
            accentRect.pivot = new Vector2(0f, 0.5f);
            accentRect.anchoredPosition = new Vector2(6f, 0f);
            accentRect.sizeDelta = new Vector2(4f, 0f);

            var image = accent.GetComponent<Image>();
            image.color = new Color32(85, 231, 255, 255);
            image.raycastTarget = false;
        }

        private static Button GetButton(Type controllerType, object instance, string fieldName)
        {
            var field = AccessTools.Field(controllerType, fieldName);
            if (field == null)
                throw new MissingFieldException(controllerType.FullName, fieldName);

            var button = field.GetValue(instance) as Button;
            if (button == null)
                throw new InvalidOperationException($"Main menu field {fieldName} is not set to a Button.");

            return button;
        }
    }
}
