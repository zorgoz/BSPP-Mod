using System;
using System.Collections;
using System.Collections.Generic;
using HMUI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ProfilesMod
{
    internal sealed class PlayerSelectionViewController : ViewController
    {
        private const float RowHeight = 3.8f;

        private readonly List<GameObject> _rows = new List<GameObject>();
        private ProfileFlowCoordinator _flow;
        private TMP_FontAsset _font;
        private TMP_Text _caption;
        private TMP_Text _message;
        private TMP_Text _nameDisplay;
        private Button _nameEntryButton;
        private InputFieldView _nameInput;
        private UIKeyboardManager _keyboardManager;
        private Button _addButton;
        private Button _deleteButton;
        private Button _playButton;
        private RectTransform _listContainer;
        private GameObject _listObject;
        private ScrollRect _scrollRect;
        private string _selectedPlayerId;
        private string _pendingName;

        internal void Initialize(ProfileFlowCoordinator flow, TMP_FontAsset font)
        {
            _flow = flow;
            _font = font;
        }

        protected override void DidActivate(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
        {
            ConfigureCanvasForScreen();
            if (firstActivation)
                BuildView();

            Refresh(restoreSelection: addedToHierarchy);
        }

        protected override void DidDeactivate(bool removedFromHierarchy, bool screenSystemDisabling)
        {
            _flow.CloseKeyboardFor(_nameInput, _keyboardManager);
        }

        internal void SetPendingName(string name)
        {
            _pendingName = name ?? string.Empty;
            if (_nameInput != null && _nameInput.text != _pendingName)
                _nameInput.SetText(_pendingName);
            if (_nameDisplay != null)
                _nameDisplay.text = string.IsNullOrEmpty(_pendingName) ? "Enter a name" : _pendingName;
            if (_addButton != null)
                _addButton.interactable = !string.IsNullOrWhiteSpace(_pendingName);
            SetMessage(string.Empty);
        }

        internal void SetMessage(string message)
        {
            if (_message != null)
                _message.text = message ?? string.Empty;
        }

        private void BuildView()
        {
            var root = rectTransform;
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;

            var panelObject = new GameObject("Panel", typeof(RectTransform));
            panelObject.transform.SetParent(transform, false);
            SetRect((RectTransform)panelObject.transform, Vector2.zero, new Vector2(144f, 74f));
            var panel = panelObject.transform;
            CreateText("Caption", panel, string.Empty, 5f, TextAlignmentOptions.Center,
                new Vector2(0f, 31f), new Vector2(130f, 6f), FontStyles.Bold, Color.white, out _caption);

            CreatePlayerList(panel);

            CreateText("NewPlayerLabel", panel, "New player:", 3.5f,
                TextAlignmentOptions.MidlineLeft, new Vector2(-51f, -18.5f),
                new Vector2(28f, 6f), FontStyles.Normal, Color.white, out _);
            var template = _flow.GetSearchInputTemplate(out _keyboardManager);
            if (template != null)
                _nameInput = CreateNameInput(panel, template);
            else
            {
                _nameEntryButton = CreateButton("NameEntryButton", panel, string.Empty,
                    new Vector2(0f, -18.5f), new Vector2(66f, 6f), out _nameDisplay);
                buttonBinder.AddBinding(_nameEntryButton, _flow.BeginNameEntry);
            }
            _addButton = CreateButton("AddButton", panel, "Add",
                new Vector2(54f, -18.5f), new Vector2(20f, 6f), out _);
            buttonBinder.AddBinding(_addButton, AddPlayer);

            CreateText("Message", panel, string.Empty, 3f, TextAlignmentOptions.Center,
                new Vector2(0f, -24f), new Vector2(130f, 4f), FontStyles.Normal,
                new Color(1f, 0.7f, 0.45f, 1f), out _message);

            _deleteButton = CreateButton("DeleteButton", panel, "Delete player",
                new Vector2(0f, -30f), new Vector2(36f, 6f), out _);
            buttonBinder.AddBinding(_deleteButton, DeletePlayer);
            var backButton = CreateButton("BackButton", panel, "Back",
                new Vector2(-48f, -30f), new Vector2(30f, 6f), out _);
            buttonBinder.AddBinding(backButton, () => _flow.Close());
            _playButton = CreateButton("PlayButton", panel, "Play",
                new Vector2(48f, -30f), new Vector2(30f, 6f), out _);
            buttonBinder.AddBinding(_playButton, Play);
            SetPendingName(null);
        }

        private InputFieldView CreateNameInput(Transform panel, InputFieldView template)
        {
            var staging = new GameObject("InputStaging");
            staging.SetActive(false);
            staging.transform.SetParent(panel, false);
            var inputObject = Instantiate(template.gameObject, staging.transform, false);
            inputObject.SetActive(false);
            inputObject.name = "NameInput";
            inputObject.transform.SetParent(panel, false);
            Destroy(staging);

            var input = inputObject.GetComponent<InputFieldView>();
            if (input == null)
                throw new MissingComponentException("The cloned song-search input is missing InputFieldView.");

            SetRect((RectTransform)inputObject.transform, new Vector2(0f, -18.5f), new Vector2(66f, 6f));
            var inputGraphic = inputObject.GetComponent<Graphic>();
            if (inputGraphic == null)
                throw new MissingComponentException("The cloned song-search input has no root raycast graphic.");

            var clearButton = ProfileFlowCoordinator.ReadDependency<Button>(
                input, typeof(InputFieldView), "_clearSearchButton");
            var placeholder = ProfileFlowCoordinator.ReadDependency<GameObject>(
                input, typeof(InputFieldView), "_placeholderText");
            foreach (var graphic in inputObject.GetComponentsInChildren<Graphic>(true))
            {
                if (graphic == inputGraphic)
                {
                    graphic.raycastTarget = true;
                    continue;
                }

                var isClearButtonGraphic = clearButton != null &&
                    graphic.transform.IsChildOf(clearButton.transform);
                if (!isClearButtonGraphic)
                    graphic.raycastTarget = false;

                if (placeholder != null &&
                    (graphic.transform == placeholder.transform || graphic.transform.IsChildOf(placeholder.transform)))
                    graphic.enabled = false;
                else if (!isClearButtonGraphic && graphic is Image image &&
                    ((graphic.name.IndexOf("search", StringComparison.OrdinalIgnoreCase) >= 0 &&
                      graphic.name.IndexOf("icon", StringComparison.OrdinalIgnoreCase) >= 0) ||
                     (image.sprite != null &&
                      image.sprite.name.IndexOf("search", StringComparison.OrdinalIgnoreCase) >= 0 &&
                      image.sprite.name.IndexOf("icon", StringComparison.OrdinalIgnoreCase) >= 0)))
                    graphic.enabled = false;
            }
            input.onValueChanged = new InputFieldView.InputFieldChanged();
            input.onValueChanged.AddListener(value => SetPendingName(value.text));
            inputObject.AddComponent<ProfileKeyboardPlacement>().Initialize(input, _keyboardManager, this);
            inputObject.SetActive(true);
            input.SetText(string.Empty);
            return input;
        }

        private void ConfigureCanvasForScreen()
        {
            var canvas = GetComponent<Canvas>();
            var screenCanvas = screen == null ? null : screen.GetComponentInParent<Canvas>();
            if (canvas == null || screenCanvas == null)
                throw new InvalidOperationException(
                    "The profile screen is missing its native ScreenSystem Canvas.");

            var rootCanvas = screenCanvas.rootCanvas;
            canvas.renderMode = rootCanvas.renderMode;
            canvas.worldCamera = rootCanvas.worldCamera;
            canvas.planeDistance = rootCanvas.planeDistance;
            canvas.pixelPerfect = rootCanvas.pixelPerfect;
            canvas.targetDisplay = rootCanvas.targetDisplay;
            canvas.additionalShaderChannels = rootCanvas.additionalShaderChannels;
            canvas.overrideSorting = false;
        }

        private void CreatePlayerList(Transform panel)
        {
            var list = new GameObject("PlayerList", typeof(RectTransform));
            list.SetActive(false);
            list.transform.SetParent(panel, false);
            var listRect = (RectTransform)list.transform;
            SetRect(listRect, new Vector2(0f, 7f), new Vector2(130f, RowHeight * 10f));

            var image = list.AddComponent<Image>();
            image.color = new Color(0.025f, 0.04f, 0.065f, 0.9f);
            var mask = list.AddComponent<Mask>();
            mask.showMaskGraphic = false;
            _scrollRect = list.AddComponent<ScrollRect>();

            var contentObject = new GameObject("Content", typeof(RectTransform));
            contentObject.transform.SetParent(list.transform, false);
            _listContainer = (RectTransform)contentObject.transform;
            _listContainer.anchorMin = new Vector2(0f, 1f);
            _listContainer.anchorMax = new Vector2(1f, 1f);
            _listContainer.pivot = new Vector2(0.5f, 1f);
            _listContainer.anchoredPosition = Vector2.zero;
            _listContainer.sizeDelta = Vector2.zero;

            _scrollRect.content = _listContainer;
            _scrollRect.viewport = listRect;
            _scrollRect.horizontal = false;
            _scrollRect.vertical = true;
            _scrollRect.scrollSensitivity = RowHeight;
            _listObject = list;
            list.SetActive(true);
        }

        private void Refresh(bool restoreSelection = false)
        {
            var players = ProfileDatabase.GetPlayers();
            var selectedStillExists = false;
            if (_selectedPlayerId != null)
            {
                foreach (var player in players)
                {
                    if (player.Id == _selectedPlayerId)
                    {
                        selectedStillExists = true;
                        break;
                    }

                }
            }

            if (!selectedStillExists)
            {
                _selectedPlayerId = null;
                foreach (var player in players)
                {
                    if (!restoreSelection || !player.IsSelected)
                        continue;

                    _selectedPlayerId = player.Id;
                    break;
                }
            }

            _caption.text = players.Length == 0 ? "Create player" : "Select or create player";
            _listObject.SetActive(players.Length != 0);

            foreach (var row in _rows)
                Destroy(row);
            _rows.Clear();
            _listContainer.sizeDelta = new Vector2(0f, players.Length * RowHeight);
            for (var index = 0; index < players.Length; index++)
                AddPlayerRow(players[index], index);

            var selected = FindSelected(players);
            _deleteButton.gameObject.SetActive(selected != null && !selected.HasRuns);
            _playButton.interactable = selected != null;
            var nameEntryAvailable = _nameInput != null || _flow.IsNameEntryAvailable();
            if (_nameEntryButton != null)
                _nameEntryButton.interactable = nameEntryAvailable;
            SetMessage(nameEntryAvailable ? string.Empty : "Player name entry unavailable: native keyboard not loaded.");
            _scrollRect.verticalNormalizedPosition = 1f;
        }

        private void AddPlayerRow(PlayerProfile player, int index)
        {
            var row = new GameObject("Player_" + index, typeof(RectTransform));
            row.SetActive(false);
            row.transform.SetParent(_listContainer, false);
            var rect = (RectTransform)row.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(0f, RowHeight);
            rect.anchoredPosition = new Vector2(0f, -index * RowHeight);

            row.AddComponent<Touchable>();
            var isSelected = player.Id == _selectedPlayerId;
            var button = row.AddComponent<Button>();
            SetButtonColors(button, isSelected ? new Color(0.25f, 0.85f, 1f, 1f) : Color.white,
                new Color(0.45f, 0.9f, 1f, 1f), new Color(0.25f, 0.7f, 0.9f, 1f));
            button.onClick.AddListener(() =>
            {
                _selectedPlayerId = player.Id;
                Refresh();
            });

            TMP_Text text;
            CreateText("Name", row.transform, player.Name, 3.4f, TextAlignmentOptions.Midline,
                Vector2.zero, Vector2.zero, isSelected ? FontStyles.Bold : FontStyles.Normal,
                Color.white, out text);
            var textRect = (RectTransform)text.transform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(2f, 0f);
            textRect.offsetMax = new Vector2(-2f, 0f);
            text.richText = false;
            text.fontSizeMin = 1.5f;
            text.fontSizeMax = 3.4f;
            text.enableAutoSizing = true;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;
            button.targetGraphic = text;
            row.SetActive(true);
            _rows.Add(row);
        }

        private void AddPlayer()
        {
            if (string.IsNullOrWhiteSpace(_pendingName))
            {
                SetMessage("Enter a player name first.");
                return;
            }

            try
            {
                var player = ProfileDatabase.CreatePlayer(_pendingName);
                _selectedPlayerId = player.Id;
                SetPendingName(null);
                Refresh();
            }
            catch (Exception exception)
            {
                Plugin.Log.Error("Could not create player: " + exception);
                SetMessage(exception.Message);
            }
        }

        private void DeletePlayer()
        {
            try
            {
                var selected = FindSelected(ProfileDatabase.GetPlayers());
                if (selected == null || selected.HasRuns)
                    return;

                ProfileDatabase.DeletePlayer(selected.Id);
                _selectedPlayerId = null;
                Refresh();
            }
            catch (Exception exception)
            {
                Plugin.Log.Error("Could not delete player: " + exception);
                SetMessage(exception.Message);
            }
        }

        private void Play()
        {
            if (_selectedPlayerId != null)
                _flow.Play(_selectedPlayerId);
        }

        private PlayerProfile FindSelected(IEnumerable<PlayerProfile> players)
        {
            foreach (var player in players)
            {
                if (player.Id == _selectedPlayerId)
                    return player;
            }

            return null;
        }

        private Button CreateButton(string name, Transform parent, string label, Vector2 position,
            Vector2 size, out TMP_Text text)
        {
            var buttonObject = new GameObject(name, typeof(RectTransform));
            buttonObject.SetActive(false);
            buttonObject.transform.SetParent(parent, false);
            SetRect((RectTransform)buttonObject.transform, position, size);
            buttonObject.AddComponent<Touchable>();
            var button = buttonObject.AddComponent<Button>();
            SetButtonColors(button, Color.white,
                new Color(0.45f, 0.9f, 1f, 1f), new Color(0.25f, 0.7f, 0.9f, 1f));

            CreateText("Label", buttonObject.transform, label, 3.5f, TextAlignmentOptions.Center,
                Vector2.zero, Vector2.zero, FontStyles.Bold, Color.white, out text);
            var textRect = (RectTransform)text.transform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(1f, 0f);
            textRect.offsetMax = new Vector2(-1f, 0f);
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;
            button.targetGraphic = text;
            buttonObject.SetActive(true);
            return button;
        }

        private void CreateText(string name, Transform parent, string value, float fontSize,
            TextAlignmentOptions alignment, Vector2 position, Vector2 size, FontStyles style,
            Color color, out TMP_Text text)
        {
            var textObject = new GameObject(name, typeof(RectTransform));
            textObject.SetActive(false);
            textObject.transform.SetParent(parent, false);
            SetRect((RectTransform)textObject.transform, position, size);
            text = textObject.AddComponent<TextMeshProUGUI>();
            text.font = _font;
            text.text = value;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.fontStyle = style;
            text.color = color;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            textObject.SetActive(true);
        }

        private static void SetButtonColors(Button button, Color normal, Color highlighted, Color pressed)
        {
            var colors = button.colors;
            colors.normalColor = normal;
            colors.highlightedColor = highlighted;
            colors.pressedColor = pressed;
            colors.selectedColor = highlighted;
            colors.disabledColor = new Color(normal.r * 0.55f, normal.g * 0.55f, normal.b * 0.55f, normal.a);
            button.colors = colors;
        }

        private static void SetRect(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }
    }

    internal sealed class ProfileKeyboardPlacement : MonoBehaviour, IPointerDownHandler
    {
        private InputFieldView _input;
        private UIKeyboardManager _manager;
        private PlayerSelectionViewController _view;

        internal void Initialize(InputFieldView input, UIKeyboardManager manager, PlayerSelectionViewController view)
        {
            _input = input;
            _manager = manager;
            _view = view;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            StartCoroutine(PositionKeyboard());
        }

        private IEnumerator PositionKeyboard()
        {
            yield return null;
            var selected = ProfileFlowCoordinator.ReadDependency<InputFieldView>(
                _manager, typeof(UIKeyboardManager), "_selectedInput");
            if (selected != _input)
                yield break;

            var container = ProfileFlowCoordinator.ReadDependency<RectTransform>(
                _manager, typeof(UIKeyboardManager), "_keyboardContainerTransform");
            if (container == null)
            {
                Plugin.Log.Error("Cannot position the profile keyboard: its container is missing.");
                _view.SetMessage("Could not position the game's keyboard.");
                yield break;
            }

            while (container != null && container.gameObject.activeInHierarchy &&
                container.GetComponent<PanelAnimation>() != null)
                yield return null;

            selected = ProfileFlowCoordinator.ReadDependency<InputFieldView>(
                _manager, typeof(UIKeyboardManager), "_selectedInput");
            if (selected != _input || container == null || !container.gameObject.activeInHierarchy)
                yield break;

            var screenRect = _view.rectTransform;
            var corners = new Vector3[4];
            container.GetWorldCorners(corners);
            var bottom = float.PositiveInfinity;
            var top = float.NegativeInfinity;
            foreach (var corner in corners)
            {
                var y = screenRect.InverseTransformPoint(corner).y;
                bottom = Mathf.Min(bottom, y);
                top = Mathf.Max(top, y);
            }

            ((RectTransform)_input.transform).GetWorldCorners(corners);
            var inputTop = float.NegativeInfinity;
            foreach (var corner in corners)
                inputTop = Mathf.Max(inputTop, screenRect.InverseTransformPoint(corner).y);

            var height = top - bottom;
            const float margin = 2f;
            var minBottom = screenRect.rect.yMin + margin;
            var maxBottom = screenRect.rect.yMax - height - margin;
            var targetBottom = maxBottom < minBottom
                ? screenRect.rect.center.y - height / 2f
                : Mathf.Clamp(inputTop + margin, minBottom, maxBottom);
            container.position += screenRect.TransformVector(new Vector3(0f, targetBottom - bottom, 0f));
        }
    }
}
