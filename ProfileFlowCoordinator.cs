using System;
using System.Reflection;
using HarmonyLib;
using HMUI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using VRUIControls;
using Zenject;

namespace ProfilesMod
{
    internal sealed class ProfileFlowCoordinator : FlowCoordinator
    {
        private const string FlowObjectName = "ProfilesMod.ProfileFlowCoordinator";
        private static ProfileFlowCoordinator _instance;

        private FlowCoordinator _mainFlowCoordinator;
        private DiContainer _sceneContainer;
        private Button _soloButton;
        private TMP_FontAsset _font;
        private PlayerSelectionViewController _playerSelectionViewController;
        private EnterTextViewController _enterTextViewController;
        private VRTextEntryController _nativeTextEntryController;

        internal static void Open(Button soloButton, TMP_FontAsset font)
        {
            if (soloButton == null)
                throw new ArgumentNullException(nameof(soloButton));
            if (font == null)
                throw new ArgumentNullException(nameof(font));

            var mainFlowCoordinator = FindMainFlowCoordinator();
            if (mainFlowCoordinator.childFlowCoordinator != null)
                throw new InvalidOperationException("The main menu already has an active sub-flow.");

            if (_instance == null || _instance.gameObject == null)
                _instance = CreateInstance(mainFlowCoordinator);

            _instance._mainFlowCoordinator = mainFlowCoordinator;
            _instance._soloButton = soloButton;
            _instance._font = font;
            mainFlowCoordinator.PresentFlowCoordinator(_instance);
        }

        protected override void DidActivate(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
        {
            showBackButton = true;
            SetTitle("Players");

            if (_playerSelectionViewController == null)
                _playerSelectionViewController = CreatePlayerSelectionViewController();

            if (addedToHierarchy)
                ProvideInitialViewControllers(_playerSelectionViewController);
        }

        internal static T ReadDependency<T>(object instance, Type owner, string fieldName) where T : class
        {
            if (instance == null)
                return null;

            var field = owner.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
            {
                Plugin.Log.Error("Native text-entry field is missing: " + owner.Name + "." + fieldName);
                return null;
            }

            return field.GetValue(instance) as T;
        }

        protected override void BackButtonWasPressed(ViewController topViewController)
        {
            if (topViewController == _enterTextViewController)
            {
                DismissViewController(topViewController);
                return;
            }

            if (topViewController == _playerSelectionViewController)
                _mainFlowCoordinator.DismissFlowCoordinator(this);
        }

        internal void BeginNameEntry()
        {
            try
            {
                if (_enterTextViewController == null)
                    CreateNativeTextEntryViewController();

                _enterTextViewController.Init("New player");
                PresentViewController(_enterTextViewController);
            }
            catch (Exception exception)
            {
                Plugin.Log.Error("Could not open Beat Saber's native player-name entry: " + exception);
                _playerSelectionViewController.SetMessage("Could not open the game's text-entry screen.");
            }
        }

        internal bool IsNameEntryAvailable()
        {
            return _enterTextViewController != null || FindConfiguredTextEntryTemplate() != null;
        }

        internal InputFieldView GetSearchInputTemplate(out UIKeyboardManager keyboardManager)
        {
            keyboardManager = null;
            var solo = ReadDependency<SoloFreePlayFlowCoordinator>(
                _mainFlowCoordinator, typeof(MainFlowCoordinator), "_soloFreePlayFlowCoordinator");
            var search = ReadDependency<LevelSearchViewController>(
                solo, typeof(LevelSelectionFlowCoordinator), "_levelSearchViewController");
            var input = ReadDependency<InputFieldView>(
                search, typeof(LevelSearchViewController), "_searchTextInputFieldView");
            keyboardManager = ReadDependency<UIKeyboardManager>(
                search, typeof(LevelSearchViewController), "_uiKeyboardManager");
            var selfContained = input != null && HasSelfContainedInputReferences(input);
            var externalListeners = input != null && HasExternalButtonListeners(input);
            if (input == null || !input.useGlobalKeyboard || keyboardManager == null ||
                !keyboardManager.isActiveAndEnabled || keyboardManager.keyboard == null ||
                !selfContained || externalListeners)
            {
                Plugin.Log.Error("The native song-search input or global keyboard is unavailable or not safe to clone.");
                keyboardManager = null;
                return null;
            }

            return input;
        }

        internal static InputFieldView GetPlaylistInputTemplate(out UIKeyboardManager keyboardManager)
        {
            keyboardManager = null;
            return _instance == null ? null : _instance.GetSearchInputTemplate(out keyboardManager);
        }

        internal static void ClosePlaylistKeyboard(InputFieldView input, UIKeyboardManager keyboardManager)
        {
            if (_instance != null)
                _instance.CloseKeyboardFor(input, keyboardManager);
        }

        internal void CloseKeyboardFor(InputFieldView input, UIKeyboardManager keyboardManager)
        {
            var selected = ReadDependency<InputFieldView>(
                keyboardManager, typeof(UIKeyboardManager), "_selectedInput");
            if (input != null && keyboardManager != null && selected == input)
                keyboardManager.CloseKeyboard();
        }

        private static bool HasSelfContainedInputReferences(InputFieldView input)
        {
            var root = input.transform;
            var text = ReadDependency<TMP_Text>(input, typeof(InputFieldView), "_textView");
            var caret = ReadDependency<ImageViewBase>(input, typeof(InputFieldView), "_blinkingCaret");
            var clearButton = ReadDependency<Button>(input, typeof(InputFieldView), "_clearSearchButton");
            var placeholder = ReadDependency<GameObject>(input, typeof(InputFieldView), "_placeholderText");
            var textGroup = ReadDependency<CanvasGroup>(input, typeof(InputFieldView), "_textViewCanvasGroup");
            return IsInHierarchy(root, text) && IsInHierarchy(root, caret) &&
                IsInHierarchy(root, clearButton) && placeholder != null &&
                (placeholder.transform == root || placeholder.transform.IsChildOf(root)) &&
                (textGroup == null || IsInHierarchy(root, textGroup)) &&
                (input.targetGraphic == null || IsInHierarchy(root, input.targetGraphic));
        }

        private static bool HasExternalButtonListeners(InputFieldView input)
        {
            var root = input.transform;
            foreach (var button in input.GetComponentsInChildren<Button>(true))
            {
                for (var index = 0; index < button.onClick.GetPersistentEventCount(); index++)
                {
                    var target = button.onClick.GetPersistentTarget(index);
                    var component = target as Component;
                    var targetObject = target as GameObject;
                    if ((component == null || !IsInHierarchy(root, component)) &&
                        (targetObject == null ||
                         (targetObject.transform != root && !targetObject.transform.IsChildOf(root))))
                        return true;
                }
            }

            return false;
        }

        internal void Play(string selectedPlayerId)
        {
            if (string.IsNullOrEmpty(selectedPlayerId))
                return;

            PlayerProfile selected = null;
            SoloFreePlayFlowCoordinator solo;
            SimpleDialogPromptViewController prompt;
            try
            {
                foreach (var player in ProfileDatabase.GetPlayers())
                {
                    if (player.Id == selectedPlayerId)
                    {
                        selected = player;
                        break;
                    }
                }
                if (selected == null)
                    throw new InvalidOperationException("The selected player no longer exists.");
                solo = ReadDependency<SoloFreePlayFlowCoordinator>(
                    _mainFlowCoordinator, typeof(MainFlowCoordinator), "_soloFreePlayFlowCoordinator");
                prompt = ReadDependency<SimpleDialogPromptViewController>(
                    _mainFlowCoordinator, typeof(MainFlowCoordinator), "_simpleDialogPromptViewController");
                if (solo == null || prompt == null)
                    throw new InvalidOperationException("The native Solo flow or tutorial prompt is unavailable.");
                ProfileSoloSession.Prepare(solo, _sceneContainer);
                ProfileDatabase.ClearPlaylistSelection(selectedPlayerId);
                ProfileDatabase.SelectPlayer(selectedPlayerId);
            }
            catch (Exception exception)
            {
                Plugin.Log.Error("Could not select player: " + exception);
                _playerSelectionViewController.SetMessage(exception.Message);
                return;
            }

            var soloButton = _soloButton;
            _mainFlowCoordinator.DismissFlowCoordinator(this, finishedCallback: () =>
            {
                try
                {
                    if (soloButton == null)
                        throw new InvalidOperationException("The Solo button is unavailable.");
                    ProfileSoloSession.Begin(selected, _mainFlowCoordinator, solo, prompt);
                    soloButton.onClick.Invoke();
                    ProfileSoloSession.CheckPendingTransition();
                }
                catch (Exception exception)
                {
                    ProfileSoloSession.Clear();
                    Plugin.Log.Error("Could not enter profile Solo: " + exception);
                }
            });
        }

        internal void Close()
        {
            if (_mainFlowCoordinator != null)
                _mainFlowCoordinator.DismissFlowCoordinator(this);
        }

        private static ProfileFlowCoordinator CreateInstance(FlowCoordinator mainFlowCoordinator)
        {
            var flowObject = new GameObject(FlowObjectName);
            flowObject.SetActive(false);
            try
            {
                var scene = mainFlowCoordinator.gameObject.scene;
                if (flowObject.scene != scene)
                    SceneManager.MoveGameObjectToScene(flowObject, scene);

                var sceneContext = FindSceneContext(mainFlowCoordinator);
                var container = sceneContext.Container;
                if (container == null || !container.HasBinding(typeof(BaseInputModule)))
                    throw new InvalidOperationException(
                        "The MainFlowCoordinator scene context does not bind BaseInputModule.");

                var flowCoordinator = container.InstantiateComponent<ProfileFlowCoordinator>(
                    flowObject, new object[0]);
                flowCoordinator._sceneContainer = container;
                flowObject.SetActive(true);
                return flowCoordinator;
            }
            catch
            {
                UnityEngine.Object.Destroy(flowObject);
                throw;
            }
        }

        private static SceneContext FindSceneContext(FlowCoordinator mainFlowCoordinator)
        {
            SceneContext match = null;
            var mainScene = mainFlowCoordinator.gameObject.scene;
            foreach (var sceneContext in Resources.FindObjectsOfTypeAll<SceneContext>())
            {
                if (sceneContext == null ||
                    !sceneContext.gameObject.activeInHierarchy ||
                    !sceneContext.HasResolved ||
                    sceneContext.gameObject.scene != mainScene)
                    continue;

                if (match != null)
                    throw new InvalidOperationException(
                        "Multiple resolved SceneContexts share the MainFlowCoordinator scene.");

                match = sceneContext;
            }

            if (match == null)
                throw new InvalidOperationException(
                    "No resolved SceneContext was found for the MainFlowCoordinator scene.");

            return match;
        }

        private static FlowCoordinator FindMainFlowCoordinator()
        {
            var mainFlowType = AccessTools.TypeByName("MainFlowCoordinator");
            if (mainFlowType == null)
                throw new TypeLoadException("Could not find Beat Saber's MainFlowCoordinator.");

            foreach (var candidate in Resources.FindObjectsOfTypeAll(mainFlowType))
            {
                var flowCoordinator = candidate as FlowCoordinator;
                if (flowCoordinator != null &&
                    flowCoordinator.isActivated &&
                    flowCoordinator.gameObject.activeInHierarchy)
                    return flowCoordinator;
            }

            throw new InvalidOperationException("No active MainFlowCoordinator is available.");
        }

        private PlayerSelectionViewController CreatePlayerSelectionViewController()
        {
            var viewObject = new GameObject(
                "ProfilesMod.PlayerSelectionViewController",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasGroup));
            viewObject.SetActive(false);
            viewObject.transform.SetParent(transform, false);

            var inputModule = _sceneContainer.Resolve<BaseInputModule>();
            var vrInputModuleType = AccessTools.TypeByName("VRUIControls.VRInputModule");
            if (vrInputModuleType != null && vrInputModuleType.IsInstanceOfType(inputModule))
            {
                _sceneContainer.InstantiateComponent<VRGraphicRaycaster>(
                    viewObject, new object[0]);
            }
            else
            {
                viewObject.AddComponent<GraphicRaycaster>();
            }

            var viewController = viewObject.AddComponent<PlayerSelectionViewController>();
            viewController.Initialize(this, _font);
            return viewController;
        }

        private void CreateNativeTextEntryViewController()
        {
            var template = FindConfiguredTextEntryTemplate();
            if (template == null)
                throw new InvalidOperationException("No fully configured EnterTextViewController is loaded.");

            var viewObject = UnityEngine.Object.Instantiate(template.gameObject);
            viewObject.SetActive(false);
            viewObject.name = "ProfilesMod.EnterTextViewController";

            var viewController = viewObject.GetComponent<EnterTextViewController>();
            if (viewController == null)
            {
                UnityEngine.Object.Destroy(viewObject);
                throw new MissingComponentException("The native text-entry template lost its EnterTextViewController.");
            }

            _nativeTextEntryController = GetTextEntryController(viewController);
            if (_nativeTextEntryController == null)
            {
                UnityEngine.Object.Destroy(viewObject);
                throw new InvalidOperationException("The native text-entry template has no VRTextEntryController.");
            }

            _enterTextViewController = viewController;
            viewController.didFinishEvent += HandleTextEntryFinished;
            _nativeTextEntryController.cancelButtonWasPressedEvent += HandleTextEntryCancelled;
        }

        private void HandleTextEntryFinished(EnterTextViewController viewController, string text)
        {
            if (viewController != _enterTextViewController)
                return;

            _playerSelectionViewController.SetPendingName(text);
            DismissViewController(viewController);
        }

        private void HandleTextEntryCancelled()
        {
            if (_enterTextViewController != null && topViewController == _enterTextViewController)
                DismissViewController(_enterTextViewController);
        }

        private static EnterTextViewController FindConfiguredTextEntryTemplate()
        {
            var textEntryField = typeof(EnterTextViewController).GetField(
                "_textEntryController", BindingFlags.Instance | BindingFlags.NonPublic);
            var titleField = typeof(EnterTextViewController).GetField(
                "_titleText", BindingFlags.Instance | BindingFlags.NonPublic);
            var okButtonField = typeof(EnterTextViewController).GetField(
                "_okButton", BindingFlags.Instance | BindingFlags.NonPublic);
            var keyboardField = typeof(VRTextEntryController).GetField(
                "_uiKeyboard", BindingFlags.Instance | BindingFlags.NonPublic);
            var textField = typeof(VRTextEntryController).GetField(
                "_text", BindingFlags.Instance | BindingFlags.NonPublic);
            var cursorField = typeof(VRTextEntryController).GetField(
                "_cursorText", BindingFlags.Instance | BindingFlags.NonPublic);
            if (textEntryField == null || titleField == null || okButtonField == null ||
                keyboardField == null || textField == null || cursorField == null)
                throw new MissingFieldException("Beat Saber's native text-entry layout changed.");

            foreach (var candidate in Resources.FindObjectsOfTypeAll<EnterTextViewController>())
            {
                if (candidate == null)
                    continue;

                var textEntry = textEntryField.GetValue(candidate) as VRTextEntryController;
                var titleText = titleField.GetValue(candidate) as TMP_Text;
                var okButton = okButtonField.GetValue(candidate) as Button;
                var keyboard = textEntry == null ? null : keyboardField.GetValue(textEntry) as Component;
                var text = textEntry == null ? null : textField.GetValue(textEntry) as TMP_Text;
                var cursor = textEntry == null ? null : cursorField.GetValue(textEntry) as TMP_Text;
                if (IsInHierarchy(candidate.transform, textEntry) &&
                    IsInHierarchy(candidate.transform, titleText) &&
                    IsInHierarchy(candidate.transform, okButton) &&
                    IsInHierarchy(candidate.transform, keyboard) &&
                    IsInHierarchy(candidate.transform, text) &&
                    IsInHierarchy(candidate.transform, cursor))
                    return candidate;
            }

            return null;
        }

        private static bool IsInHierarchy(Transform root, Component component)
        {
            return component != null &&
                (component.transform == root || component.transform.IsChildOf(root));
        }

        private static VRTextEntryController GetTextEntryController(EnterTextViewController viewController)
        {
            var field = typeof(EnterTextViewController).GetField(
                "_textEntryController", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
                throw new MissingFieldException(typeof(EnterTextViewController).FullName, "_textEntryController");

            return field.GetValue(viewController) as VRTextEntryController;
        }
    }
}
