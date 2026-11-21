using System;
using HarmonyLib;
using HMUI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProfilesMod
{
    internal sealed class ProfilePlaylistDialog : IDisposable
    {
        private readonly ProfileLeaderboardViewController _owner;
        private readonly ModalView _modal;
        private readonly RectTransform _screen;
        private readonly GameObject _editor;
        private readonly GameObject _confirmation;
        private readonly InputFieldView _input;
        private readonly UIKeyboardManager _keyboard;
        private readonly Button _delete;
        private readonly TMP_Text _message;
        private readonly Sprite _backgroundSprite;
        private PlayerPlaylist _playlist;
        private string _playerId;
        private GameplaySetupViewController _leftView;
        private bool _leftWasInteractable;
        private bool _rightWasInteractable;
        private bool _viewsDisabled;

        private ProfilePlaylistDialog(ProfileLeaderboardViewController owner, ModalView modal,
            RectTransform screen, GameObject editor, GameObject confirmation, InputFieldView input,
            UIKeyboardManager keyboard, Button delete, TMP_Text message, Sprite backgroundSprite)
        {
            _owner = owner;
            _modal = modal;
            _screen = screen;
            _editor = editor;
            _confirmation = confirmation;
            _input = input;
            _keyboard = keyboard;
            _delete = delete;
            _message = message;
            _backgroundSprite = backgroundSprite;
        }

        internal static ProfilePlaylistDialog Create(ProfileLeaderboardViewController owner)
        {
            var solo = ProfileSoloSession.ActiveSolo;
            var container = ProfileSoloSession.Container;
            var navigation = ProfileFlowCoordinator.ReadDependency<LevelSelectionNavigationController>(
                solo, typeof(LevelSelectionFlowCoordinator), "levelSelectionNavigationController");
            var setup = ProfileFlowCoordinator.ReadDependency<GameplaySetupViewController>(
                solo, typeof(SinglePlayerLevelSelectionFlowCoordinator), "_gameplaySetupViewController");
            var colors = ProfileFlowCoordinator.ReadDependency<ColorsOverrideSettingsPanelController>(
                setup, typeof(GameplaySetupViewController), "_colorsOverrideSettingsPanelController");
            var template = colors == null ? null : colors.editColorSchemeModalView;
            var inputTemplate = ProfileFlowCoordinator.GetPlaylistInputTemplate(out var keyboard);
            if (navigation == null || navigation.screen == null || container == null ||
                template == null || inputTemplate == null || keyboard == null)
                throw new InvalidOperationException("The native modal, screen or global keyboard is unavailable.");
            var present = ProfileFlowCoordinator.ReadDependency<PanelAnimationSO>(
                template, typeof(ModalView), "_presentPanelAnimations");
            var dismiss = ProfileFlowCoordinator.ReadDependency<PanelAnimationSO>(
                template, typeof(ModalView), "_dismissPanelAnimation");
            if (present == null || dismiss == null)
                throw new InvalidOperationException("The native modal animations are unavailable.");

            var screen = navigation.screen.transform as RectTransform;
            var screenCanvas = navigation.screen.GetComponentInParent<Canvas>();
            if (screen == null || screenCanvas == null || owner.NativeFont == null ||
                owner.NativeTextMaterial == null || owner.NativeImageMaterial == null)
                throw new InvalidOperationException("The native screen or UI rendering materials are unavailable.");
            var root = new GameObject("ProfilesMod.PlaylistDialog", typeof(RectTransform), typeof(Canvas));
            root.SetActive(false);
            root.transform.SetParent(navigation.transform, false);
            Sprite backgroundSprite = null;
            try
            {
                var modal = container.InstantiateComponent<ModalView>(root, new object[0]);
                SetField(modal, "_presentPanelAnimations", present);
                SetField(modal, "_dismissPanelAnimation", dismiss);
                SetField(modal, "_animateParentCanvas", false);
                var curved = root.AddComponent<CurvedCanvasSettings>();
                curved.SetRadius(0f);
                curved.raycastTarget = false;
                curved.enabled = false;
                var canvas = root.GetComponent<Canvas>();
                var rootCanvas = screenCanvas.rootCanvas;
                canvas.renderMode = rootCanvas.renderMode;
                canvas.worldCamera = rootCanvas.worldCamera;
                canvas.planeDistance = rootCanvas.planeDistance;
                canvas.pixelPerfect = rootCanvas.pixelPerfect;
                canvas.targetDisplay = rootCanvas.targetDisplay;
                canvas.additionalShaderChannels = rootCanvas.additionalShaderChannels |
                    AdditionalCanvasShaderChannels.TexCoord2;
                canvas.overrideSorting = false;
                var rect = (RectTransform)root.transform;
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(76f, 38f);

                var backgroundObject = MakePanel("Background", root.transform);
                var background = backgroundObject.AddComponent<ImageView>();
                backgroundSprite = Sprite.Create(Texture2D.whiteTexture,
                    new Rect(0f, 0f, Texture2D.whiteTexture.width, Texture2D.whiteTexture.height),
                    new Vector2(0.5f, 0.5f));
                background.sprite = backgroundSprite;
                background.material = owner.NativeImageMaterial;
                background.color = new Color(0.06f, 0.07f, 0.10f, 0.98f);
                background.raycastTarget = true;
                var editor = MakePanel("Editor", root.transform);
                var confirmation = MakePanel("Confirmation", root.transform);
                var font = owner.NativeFont;
                var textMaterial = owner.NativeTextMaterial;
                MakeText(editor.transform, "Playlist", font, textMaterial, new Vector2(0f, 14f), new Vector2(70f, 5f), 4f);
                var nameLabel = MakeText(editor.transform, "Playlist name:", font, textMaterial,
                    new Vector2(0f, 9.5f), new Vector2(58f, 3f), 2.7f);
                nameLabel.alignment = TextAlignmentOptions.MidlineLeft;
                var fieldObject = new GameObject("NameFieldBackground", typeof(RectTransform), typeof(CanvasRenderer));
                fieldObject.transform.SetParent(editor.transform, false);
                var fieldRect = (RectTransform)fieldObject.transform;
                fieldRect.anchorMin = fieldRect.anchorMax = fieldRect.pivot = new Vector2(0.5f, 0.5f);
                fieldRect.anchoredPosition = new Vector2(0f, 4f);
                fieldRect.sizeDelta = new Vector2(60f, 7f);
                var fieldBackground = fieldObject.AddComponent<ImageView>();
                fieldBackground.sprite = backgroundSprite;
                fieldBackground.material = owner.NativeImageMaterial;
                fieldBackground.color = new Color(0.14f, 0.32f, 0.45f, 1f);
                fieldBackground.raycastTarget = false;
                var input = CloneInput(inputTemplate, editor.transform, keyboard, navigation);
                var message = MakeText(editor.transform, "", font, textMaterial, new Vector2(0f, -3f), new Vector2(70f, 4f), 2.4f);
                message.color = new Color(1f, 0.7f, 0.45f);
                var save = MakeButton(editor.transform, "Save", font, textMaterial, new Vector2(-23f, -12f));
                var delete = MakeButton(editor.transform, "Delete", font, textMaterial, new Vector2(0f, -12f));
                var cancel = MakeButton(editor.transform, "Cancel", font, textMaterial, new Vector2(23f, -12f));
                MakeText(confirmation.transform, "Delete playlist?", font, textMaterial,
                    new Vector2(0f, 6f), new Vector2(70f, 8f), 4f);
                var confirmDelete = MakeButton(confirmation.transform, "Delete", font, textMaterial, new Vector2(-13f, -8f));
                var confirmCancel = MakeButton(confirmation.transform, "Cancel", font, textMaterial, new Vector2(13f, -8f));
                var dialog = new ProfilePlaylistDialog(owner, modal, screen, editor, confirmation,
                    input, keyboard, delete, message, backgroundSprite);
                save.onClick.AddListener(dialog.Save);
                delete.onClick.AddListener(dialog.ConfirmDelete);
                cancel.onClick.AddListener(dialog.Hide);
                confirmDelete.onClick.AddListener(dialog.Delete);
                confirmCancel.onClick.AddListener(dialog.CancelDelete);
                modal.blockerClickedEvent += dialog.Hide;
                root.AddComponent<ProfilePlaylistDialogLifecycle>().OnDisabled = dialog.OnDisabled;
                root.AddComponent<ProfileDialogKeyboardGuard>().Initialize(input, keyboard);
                return dialog;
            }
            catch
            {
                UnityEngine.Object.Destroy(root);
                if (backgroundSprite != null)
                    UnityEngine.Object.Destroy(backgroundSprite);
                throw;
            }
        }

        internal void Show(PlayerPlaylist playlist)
        {
            var player = ProfileSoloSession.Player;
            if (player == null)
                throw new InvalidOperationException("The profile session is unavailable.");
            _playerId = player.Id;
            _playlist = playlist;
            _input.SetText(playlist == null ? string.Empty : playlist.Name);
            _message.text = string.Empty;
            _delete.gameObject.SetActive(playlist != null);
            _confirmation.SetActive(false);
            _editor.SetActive(true);
            _modal.Show(false);
            var rect = (RectTransform)_modal.transform;
            rect.localScale = Vector3.one;
            rect.position = _screen.TransformPoint(_screen.rect.center) - _screen.forward * 0.35f;
            rect.rotation = _screen.rotation;
            _leftView = ProfileFlowCoordinator.ReadDependency<GameplaySetupViewController>(
                ProfileSoloSession.ActiveSolo, typeof(SinglePlayerLevelSelectionFlowCoordinator),
                "_gameplaySetupViewController");
            _rightWasInteractable = _owner.enableUserInteractions;
            _leftWasInteractable = _leftView != null && _leftView.enableUserInteractions;
            _viewsDisabled = true;
            _owner.enableUserInteractions = false;
            if (_leftView != null)
                _leftView.enableUserInteractions = false;
        }

        private void Save()
        {
            try
            {
                RequireCurrentPlayer();
                if (_playlist == null)
                    ProfileDatabase.CreatePlaylist(_playerId, _input.text);
                else
                    ProfileDatabase.RenamePlaylist(_playerId, _playlist.Id, _input.text);
                Hide();
                _owner.PlaylistChanged(false);
            }
            catch (Exception exception)
            {
                Plugin.Log.Error("Could not save playlist: " + exception);
                _message.text = exception.Message;
            }
        }

        private void ConfirmDelete()
        {
            CloseKeyboard();
            _editor.SetActive(false);
            _confirmation.SetActive(true);
        }

        private void CancelDelete()
        {
            _confirmation.SetActive(false);
            _editor.SetActive(true);
        }

        private void Delete()
        {
            try
            {
                RequireCurrentPlayer();
                if (_playlist == null)
                    throw new InvalidOperationException("The playlist does not exist.");
                ProfileDatabase.DeletePlaylist(_playerId, _playlist.Id);
                Hide();
                _owner.PlaylistChanged(true);
            }
            catch (Exception exception)
            {
                Plugin.Log.Error("Could not delete playlist: " + exception);
                CancelDelete();
                _message.text = exception.Message;
            }
        }

        private void RequireCurrentPlayer()
        {
            if (ProfileSoloSession.Player == null || ProfileSoloSession.Player.Id != _playerId)
                throw new InvalidOperationException("The profile session has changed.");
        }

        private void Hide()
        {
            CloseKeyboard();
            _modal.Hide(false);
        }

        private void CloseKeyboard()
        {
            ProfileFlowCoordinator.ClosePlaylistKeyboard(_input, _keyboard);
        }

        private void OnDisabled()
        {
            CloseKeyboard();
            if (_viewsDisabled)
            {
                if (_owner != null)
                    _owner.enableUserInteractions = _rightWasInteractable;
                if (_leftView != null)
                    _leftView.enableUserInteractions = _leftWasInteractable;
                _viewsDisabled = false;
            }
        }

        public void Dispose()
        {
            if (_modal != null)
            {
                Hide();
                UnityEngine.Object.Destroy(_modal.gameObject);
            }
            if (_backgroundSprite != null)
                UnityEngine.Object.Destroy(_backgroundSprite);
        }

        private static InputFieldView CloneInput(InputFieldView template, Transform parent,
            UIKeyboardManager keyboard, ViewController navigation)
        {
            var staging = new GameObject("InputStaging");
            staging.SetActive(false);
            staging.transform.SetParent(parent, false);
            var inputObject = UnityEngine.Object.Instantiate(template.gameObject, staging.transform, false);
            inputObject.SetActive(false);
            inputObject.name = "PlaylistName";
            inputObject.transform.SetParent(parent, false);
            UnityEngine.Object.Destroy(staging);
            var rect = (RectTransform)inputObject.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0f, 4f);
            rect.sizeDelta = new Vector2(58f, 6f);
            var input = inputObject.GetComponent<InputFieldView>();
            var rootGraphic = inputObject.GetComponent<Graphic>();
            if (input == null || rootGraphic == null)
                throw new InvalidOperationException("The native input template is incomplete.");
            var clear = ProfileFlowCoordinator.ReadDependency<Button>(input, typeof(InputFieldView), "_clearSearchButton");
            var placeholder = ProfileFlowCoordinator.ReadDependency<GameObject>(input, typeof(InputFieldView), "_placeholderText");
            foreach (var graphic in inputObject.GetComponentsInChildren<Graphic>(true))
            {
                if (graphic == rootGraphic)
                    graphic.raycastTarget = true;
                else if (clear == null || !graphic.transform.IsChildOf(clear.transform))
                {
                    graphic.raycastTarget = false;
                    if ((placeholder != null && (graphic.transform == placeholder.transform ||
                         graphic.transform.IsChildOf(placeholder.transform))) ||
                        (graphic is Image image &&
                         ((graphic.name.IndexOf("search", StringComparison.OrdinalIgnoreCase) >= 0 &&
                           graphic.name.IndexOf("icon", StringComparison.OrdinalIgnoreCase) >= 0) ||
                          (image.sprite != null &&
                           image.sprite.name.IndexOf("search", StringComparison.OrdinalIgnoreCase) >= 0 &&
                           image.sprite.name.IndexOf("icon", StringComparison.OrdinalIgnoreCase) >= 0))))
                        graphic.enabled = false;
                }
            }
            input.onValueChanged = new InputFieldView.InputFieldChanged();
            inputObject.AddComponent<ProfileKeyboardPlacement>().Initialize(input, keyboard, navigation,
                error => Plugin.Log.Error(error));
            inputObject.SetActive(true);
            return input;
        }

        private static GameObject MakePanel(string name, Transform parent)
        {
            var panel = new GameObject(name, typeof(RectTransform));
            panel.transform.SetParent(parent, false);
            var rect = (RectTransform)panel.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return panel;
        }

        private static TMP_Text MakeText(Transform parent, string value, TMP_FontAsset font, Material material,
            Vector2 position, Vector2 size, float fontSize)
        {
            var obj = new GameObject("Text", typeof(RectTransform));
            obj.SetActive(false);
            obj.transform.SetParent(parent, false);
            var rect = (RectTransform)obj.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            var text = obj.AddComponent<CurvedTextMeshPro>();
            text.font = font;
            text.fontSharedMaterial = material;
            text.text = value;
            text.color = Color.white;
            text.fontSize = fontSize;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            obj.SetActive(true);
            return text;
        }

        private static Button MakeButton(Transform parent, string label, TMP_FontAsset font,
            Material material, Vector2 position)
        {
            var obj = new GameObject(label, typeof(RectTransform));
            obj.transform.SetParent(parent, false);
            var rect = (RectTransform)obj.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(22f, 6f);
            obj.AddComponent<Touchable>();
            var button = obj.AddComponent<Button>();
            button.targetGraphic = MakeText(obj.transform, label, font, material, Vector2.zero, rect.sizeDelta, 3.2f);
            return button;
        }

        private static void SetField(object instance, string name, object value)
        {
            var field = AccessTools.Field(typeof(ModalView), name);
            if (field == null)
                throw new MissingFieldException(typeof(ModalView).FullName, name);
            field.SetValue(instance, value);
        }
    }

    internal sealed class ProfilePlaylistDialogLifecycle : MonoBehaviour
    {
        internal Action OnDisabled;
        private void OnDisable() => OnDisabled?.Invoke();
    }
}
