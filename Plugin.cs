using System;
using System.IO;
using System.Reflection;
using HarmonyLib;
using IPA;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using IPALogger = IPA.Logging.Logger;

namespace ProfilesMod
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
            try
            {
                ProfileDatabase.Initialize();
                Log.Info("Profiles database is ready.");
            }
            catch (Exception exception)
            {
                Log.Error("Profiles database initialization failed. The Players button will be disabled.");
                Log.Error(exception.ToString());
            }

            _harmony = new Harmony("ProfilesMod");
            _harmony.PatchAll(typeof(Plugin).Assembly);
            Log.Info("Profiles initialized.");
        }

        [OnStart]
        public void OnStart()
        {
            Log.Info("Profiles started.");
        }

        [OnExit]
        public void OnExit()
        {
            _harmony.UnpatchSelf();
            Log.Info("Profiles shutting down.");
        }
    }

    [HarmonyPatch]
    internal static class MainMenuButtonPatch
    {
        private const string ButtonObjectName = "ProfilesMod.PlayersButton";
        private static Sprite _playersButtonSprite;
        private static Sprite _playersHighlightSprite;
        private static Sprite _playersOverlaySprite;

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

            LoadButtonSprites();

            var buttonObject = UnityEngine.Object.Instantiate(soloButton.gameObject, parent, false);
            buttonObject.name = ButtonObjectName;
            DisableButtonLocalization(buttonObject);

            var button = buttonObject.GetComponent<Button>();
            if (button == null)
                throw new MissingComponentException("The cloned main menu object has no Button component.");

            button.onClick = new Button.ButtonClickedEvent();
            button.interactable = ProfileDatabase.IsAvailable;
            button.onClick.AddListener(() =>
            {
                if (!ProfileDatabase.IsAvailable)
                    return;

                Plugin.Log.Info("Players button clicked.");
            });
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = Color.white;
            colors.pressedColor = Color.white;
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(0.55f, 0.55f, 0.55f, 1f);
            button.colors = colors;
            if (!ProfileDatabase.IsAvailable)
                SetDatabaseUnavailableHint(buttonObject, ProfileDatabase.InitializationFailure);

            ApplyButtonSprites(buttonObject);

            SetButtonCaption(buttonObject);

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

            Plugin.Log.Info(ProfileDatabase.IsAvailable
                ? "Added the Players button to the main menu."
                : "Added the Players button in a disabled state because the profiles database is unavailable.");
        }

        private static void LoadButtonSprites()
        {
            if (_playersButtonSprite != null)
                return;

            var buttonSprite = LoadSprite("ProfilesMod.Resources.PlayersButton.png");
            var highlightSprite = LoadSprite("ProfilesMod.Resources.PlayersHighlightButton.png");
            var overlaySprite = LoadSprite("ProfilesMod.Resources.PlayersButtonOverlay.png");

            _playersButtonSprite = buttonSprite;
            _playersHighlightSprite = highlightSprite;
            _playersOverlaySprite = overlaySprite;
        }

        private static Sprite LoadSprite(string resourceName)
        {
            using (var stream = typeof(Plugin).Assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                    throw new FileNotFoundException("Embedded button image was not found.", resourceName);

                using (var buffer = new MemoryStream())
                {
                    stream.CopyTo(buffer);
                    var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (!LoadPng(texture, buffer.ToArray()))
                    {
                        UnityEngine.Object.Destroy(texture);
                        throw new InvalidDataException("Could not decode embedded button image: " + resourceName);
                    }

                    texture.name = resourceName;
                    texture.filterMode = FilterMode.Bilinear;
                    texture.wrapMode = TextureWrapMode.Clamp;
                    return Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
                        new Vector2(0.5f, 0.5f), 100f);
                }
            }
        }

        private static bool LoadPng(Texture2D texture, byte[] pngData)
        {
            var imageConversionAssembly = Assembly.Load("UnityEngine.ImageConversionModule");
            var imageConversionType = imageConversionAssembly.GetType("UnityEngine.ImageConversion", true);
            var loadImage = imageConversionType.GetMethod("LoadImage", BindingFlags.Public | BindingFlags.Static,
                null, new[] { typeof(Texture2D), typeof(byte[]), typeof(bool) }, null);
            if (loadImage == null)
                throw new MissingMethodException(imageConversionType.FullName, "LoadImage");

            return (bool)loadImage.Invoke(null, new object[] { texture, pngData, true });
        }

        private static void ApplyButtonSprites(GameObject buttonObject)
        {
            var spriteSwapType = AccessTools.TypeByName("HMUI.ButtonSpriteSwap");
            if (spriteSwapType == null)
                throw new TypeLoadException("Could not find HMUI.ButtonSpriteSwap.");

            var spriteSwapComponents = buttonObject.GetComponentsInChildren(spriteSwapType, true);
            if (spriteSwapComponents.Length == 0)
                throw new MissingComponentException("The cloned main menu button has no ButtonSpriteSwap component.");

            var imagesField = AccessTools.Field(spriteSwapType, "_images");
            if (imagesField == null)
                throw new MissingFieldException(spriteSwapType.FullName, "_images");

            var primaryImages = imagesField.GetValue(spriteSwapComponents[0]) as Image[];
            if (primaryImages == null || primaryImages.Length == 0)
                throw new InvalidOperationException("The cloned button's sprite swap has no target images.");

            Image overlayImage = null;
            var allImages = buttonObject.GetComponentsInChildren<Image>(true);
            foreach (var image in allImages)
            {
                if (image.sprite != null && image.sprite.name == "SoloButtonOverlay")
                {
                    overlayImage = image;
                    break;
                }
            }

            if (overlayImage == null)
                throw new InvalidOperationException("Could not find the Solo button's overlay image to replace.");

            foreach (var image in primaryImages)
            {
                image.sprite = _playersButtonSprite;
                image.preserveAspect = true;
            }

            overlayImage.sprite = _playersOverlaySprite;
            overlayImage.preserveAspect = true;
            overlayImage.raycastTarget = false;

            SetSpriteField(spriteSwapType, spriteSwapComponents, "_normalStateSprite", _playersButtonSprite);
            SetSpriteField(spriteSwapType, spriteSwapComponents, "_highlightStateSprite", _playersHighlightSprite);
            SetSpriteField(spriteSwapType, spriteSwapComponents, "_pressedStateSprite", _playersHighlightSprite);
            SetSpriteField(spriteSwapType, spriteSwapComponents, "_disabledStateSprite", _playersButtonSprite);

            foreach (var spriteSwapComponent in spriteSwapComponents)
                imagesField.SetValue(spriteSwapComponent, primaryImages);
        }

        private static void SetButtonCaption(GameObject buttonObject)
        {
            var tmpTexts = buttonObject.GetComponentsInChildren<TMP_Text>(true);
            var legacyTexts = buttonObject.GetComponentsInChildren<UnityEngine.UI.Text>(true);
            if (tmpTexts.Length == 0 && legacyTexts.Length == 0)
                throw new MissingComponentException("The cloned main menu button has no text component.");

            foreach (var text in tmpTexts)
            {
                text.richText = false;
                text.text = "Players";
            }

            foreach (var text in legacyTexts)
                text.text = "Players";
        }

        private static void DisableButtonLocalization(GameObject buttonObject)
        {
            DisableLocalizedComponents(buttonObject, "BGLib.Polyglot.LocalizedTextMeshProUGUI");
            DisableLocalizedComponents(buttonObject, "BGLib.Polyglot.LocalizedTextMeshPro");
        }

        private static void SetDatabaseUnavailableHint(GameObject buttonObject, string failureReason)
        {
            var hoverHintType = AccessTools.TypeByName("HMUI.HoverHint");
            if (hoverHintType == null)
                return;

            var hoverHint = buttonObject.GetComponent(hoverHintType);
            if (hoverHint == null)
                hoverHint = buttonObject.AddComponent(hoverHintType);

            var hint = "Players is unavailable because the profiles database could not be opened for writing.";
            if (!string.IsNullOrEmpty(failureReason))
                hint += " " + failureReason;
            var textProperty = hoverHintType.GetProperty("text",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (textProperty != null && textProperty.CanWrite && textProperty.PropertyType == typeof(string))
            {
                textProperty.SetValue(hoverHint, hint, null);
                return;
            }

            var textField = AccessTools.Field(hoverHintType, "_text") ?? AccessTools.Field(hoverHintType, "text");
            if (textField != null && textField.FieldType == typeof(string))
            {
                textField.SetValue(hoverHint, hint);
                return;
            }

            Plugin.Log.Warn("HMUI.HoverHint is present but exposes no supported text member; database failure is logged.");
        }

        private static void DisableLocalizedComponents(GameObject buttonObject, string typeName)
        {
            var localizedType = AccessTools.TypeByName(typeName);
            if (localizedType == null)
                throw new TypeLoadException("Could not find " + typeName + ".");

            foreach (var component in buttonObject.GetComponentsInChildren(localizedType, true))
            {
                var behaviour = component as Behaviour;
                if (behaviour == null)
                    throw new InvalidOperationException(typeName + " is not a Behaviour component.");

                behaviour.enabled = false;
            }
        }

        private static void SetSpriteField(Type spriteSwapType, Component[] components, string fieldName, Sprite sprite)
        {
            var field = AccessTools.Field(spriteSwapType, fieldName);
            if (field == null)
                throw new MissingFieldException(spriteSwapType.FullName, fieldName);

            foreach (var component in components)
                field.SetValue(component, sprite);
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
