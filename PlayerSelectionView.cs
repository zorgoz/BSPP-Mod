using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProfilesMod
{
    internal sealed class PlayerSelectionView : MonoBehaviour
    {
        private const string ViewObjectName = "ProfilesMod.PlayerSelectionView";
        private const float RowHeight = 58f;

        private readonly List<GameObject> _rows = new List<GameObject>();
        private Button _soloButton;
        private TMP_FontAsset _font;
        private TMP_InputField _nameInput;
        private TMP_Text _caption;
        private TMP_Text _message;
        private Button _deleteButton;
        private Button _playButton;
        private RectTransform _listContainer;
        private GameObject _listObject;
        private ScrollRect _scrollRect;
        private string _selectedPlayerId;

        internal static void Open(Button soloButton, TMP_Text sampleText)
        {
            var canvas = soloButton.GetComponentInParent<Canvas>();
            if (canvas == null)
                throw new InvalidOperationException("Could not find the main menu canvas.");

            var viewTransform = canvas.transform.Find(ViewObjectName);
            PlayerSelectionView view = null;
            if (viewTransform != null)
                view = viewTransform.GetComponent<PlayerSelectionView>();

            if (view == null)
            {
                var viewObject = new GameObject(ViewObjectName, typeof(RectTransform));
                viewObject.transform.SetParent(canvas.transform, false);
                var viewCanvas = viewObject.AddComponent<Canvas>();
                viewCanvas.overrideSorting = true;
                viewCanvas.sortingLayerID = canvas.sortingLayerID;
                viewCanvas.sortingOrder = canvas.sortingOrder + 10;
                viewObject.AddComponent<GraphicRaycaster>();
                view = viewObject.AddComponent<PlayerSelectionView>();
                view.Initialize(soloButton, sampleText.font);
            }

            view._soloButton = soloButton;
            view.Refresh();
            view.gameObject.SetActive(true);
        }

        private void Initialize(Button soloButton, TMP_FontAsset font)
        {
            _soloButton = soloButton;
            _font = font;

            var root = (RectTransform)transform;
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;

            var backdrop = gameObject.AddComponent<Image>();
            backdrop.color = new Color(0f, 0f, 0f, 0.78f);

            var panel = CreateImage("Panel", transform, new Color(0.055f, 0.075f, 0.11f, 1f),
                Vector2.zero, new Vector2(1020f, 820f));
            CreateText("Caption", panel, string.Empty, 38f, TextAlignmentOptions.Center,
                new Vector2(0f, 330f), new Vector2(900f, 64f), FontStyles.Bold, Color.white, out _caption);

            CreatePlayerList(panel);

            CreateText("NewPlayerLabel", panel, "New player:", 28f, TextAlignmentOptions.MidlineLeft,
                new Vector2(-335f, -240f), new Vector2(190f, 56f), FontStyles.Normal, Color.white, out _);
            _nameInput = CreateInputField(panel, new Vector2(20f, -240f), new Vector2(490f, 58f));
            CreateButton("AddButton", panel, "Add", new Vector2(335f, -240f), new Vector2(150f, 58f),
                () => AddPlayer());

            CreateText("Message", panel, string.Empty, 20f, TextAlignmentOptions.Center,
                new Vector2(0f, -295f), new Vector2(850f, 38f), FontStyles.Normal,
                new Color(1f, 0.7f, 0.45f, 1f), out _message);

            _deleteButton = CreateButton("DeleteButton", panel, "Delete player",
                new Vector2(0f, -355f), new Vector2(240f, 58f), () => DeletePlayer());
            CreateButton("CloseButton", panel, "Back", new Vector2(-300f, -355f),
                new Vector2(180f, 58f), () => gameObject.SetActive(false));
            _playButton = CreateButton("PlayButton", panel, "Play", new Vector2(300f, -355f),
                new Vector2(180f, 58f), Play);
        }

        private void CreatePlayerList(Transform panel)
        {
            var list = new GameObject("PlayerList", typeof(RectTransform), typeof(Image), typeof(Mask), typeof(ScrollRect));
            list.transform.SetParent(panel, false);
            var listRect = (RectTransform)list.transform;
            SetRect(listRect, new Vector2(0f, 40f), new Vector2(850f, 490f));
            list.GetComponent<Image>().color = new Color(0.025f, 0.04f, 0.065f, 0.9f);
            list.GetComponent<Mask>().showMaskGraphic = false;

            var contentObject = new GameObject("Content", typeof(RectTransform));
            contentObject.transform.SetParent(list.transform, false);
            _listContainer = (RectTransform)contentObject.transform;
            _listContainer.anchorMin = new Vector2(0f, 1f);
            _listContainer.anchorMax = new Vector2(1f, 1f);
            _listContainer.pivot = new Vector2(0.5f, 1f);
            _listContainer.anchoredPosition = Vector2.zero;
            _listContainer.sizeDelta = new Vector2(0f, 0f);

            _scrollRect = list.GetComponent<ScrollRect>();
            _scrollRect.content = _listContainer;
            _scrollRect.viewport = listRect;
            _scrollRect.horizontal = false;
            _scrollRect.vertical = true;
            _scrollRect.scrollSensitivity = 55f;
            _listObject = list;
        }

        private void Refresh()
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
                    if (!player.IsSelected)
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
            _message.text = string.Empty;
            if (_scrollRect != null)
                _scrollRect.verticalNormalizedPosition = 1f;
        }

        private void AddPlayerRow(PlayerProfile player, int index)
        {
            var row = new GameObject("Player_" + index, typeof(RectTransform), typeof(Image), typeof(Button));
            row.transform.SetParent(_listContainer, false);
            var rect = (RectTransform)row.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(0f, RowHeight);
            rect.anchoredPosition = new Vector2(0f, -index * RowHeight);

            var isSelected = player.Id == _selectedPlayerId;
            var image = row.GetComponent<Image>();
            image.color = isSelected
                ? new Color(0.12f, 0.35f, 0.55f, 1f)
                : new Color(0.06f, 0.1f, 0.16f, 0.9f);
            var button = row.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() =>
            {
                _selectedPlayerId = player.Id;
                Refresh();
            });

            TMP_Text text;
            CreateText("Name", row.transform, player.Name, 28f, TextAlignmentOptions.MidlineLeft,
                Vector2.zero, Vector2.zero, isSelected ? FontStyles.Bold : FontStyles.Normal,
                Color.white, out text);
            var textRect = (RectTransform)text.transform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(24f, 0f);
            textRect.offsetMax = new Vector2(-24f, 0f);
            text.raycastTarget = false;
            _rows.Add(row);
        }

        private void AddPlayer()
        {
            try
            {
                var player = ProfileDatabase.CreatePlayer(_nameInput.text);
                _selectedPlayerId = player.Id;
                _nameInput.text = string.Empty;
                Refresh();
            }
            catch (Exception exception)
            {
                Plugin.Log.Error("Could not create player: " + exception);
                _message.text = exception.Message;
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
                _message.text = exception.Message;
            }
        }

        private void Play()
        {
            if (_selectedPlayerId == null)
                return;

            try
            {
                ProfileDatabase.SelectPlayer(_selectedPlayerId);
                gameObject.SetActive(false);
                _soloButton.onClick.Invoke();
            }
            catch (Exception exception)
            {
                Plugin.Log.Error("Could not select player: " + exception);
                _message.text = exception.Message;
            }
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

        private TMP_InputField CreateInputField(Transform parent, Vector2 position, Vector2 size)
        {
            var fieldObject = new GameObject("NameInput", typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
            fieldObject.transform.SetParent(parent, false);
            SetRect((RectTransform)fieldObject.transform, position, size);
            var background = fieldObject.GetComponent<Image>();
            background.color = new Color(0.02f, 0.035f, 0.055f, 1f);

            var viewport = new GameObject("Text Area", typeof(RectTransform), typeof(Image), typeof(Mask));
            viewport.transform.SetParent(fieldObject.transform, false);
            var viewportRect = (RectTransform)viewport.transform;
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = new Vector2(12f, 5f);
            viewportRect.offsetMax = new Vector2(-12f, -5f);
            viewport.GetComponent<Image>().color = Color.white;
            viewport.GetComponent<Mask>().showMaskGraphic = false;

            TMP_Text text;
            CreateText("Text", viewport.transform, string.Empty, 27f, TextAlignmentOptions.MidlineLeft,
                Vector2.zero, Vector2.zero, FontStyles.Normal, Color.white, out text);
            var textRect = (RectTransform)text.transform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            text.raycastTarget = false;

            TMP_Text placeholder;
            CreateText("Placeholder", viewport.transform, "Enter a name", 27f,
                TextAlignmentOptions.MidlineLeft, Vector2.zero, Vector2.zero, FontStyles.Italic,
                new Color(0.65f, 0.68f, 0.72f, 0.75f), out placeholder);
            var placeholderRect = (RectTransform)placeholder.transform;
            placeholderRect.anchorMin = Vector2.zero;
            placeholderRect.anchorMax = Vector2.one;
            placeholderRect.offsetMin = Vector2.zero;
            placeholderRect.offsetMax = Vector2.zero;
            placeholder.raycastTarget = false;

            var input = fieldObject.GetComponent<TMP_InputField>();
            input.textViewport = viewportRect;
            input.textComponent = text;
            input.placeholder = placeholder;
            input.targetGraphic = background;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.navigation = new Navigation { mode = Navigation.Mode.None };
            input.onEndEdit.AddListener(_ => { });
            return input;
        }

        private Button CreateButton(string name, Transform parent, string label, Vector2 position,
            Vector2 size, Action onClick)
        {
            var buttonObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            SetRect((RectTransform)buttonObject.transform, position, size);
            var image = buttonObject.GetComponent<Image>();
            image.color = new Color(0.1f, 0.23f, 0.36f, 1f);
            var button = buttonObject.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => onClick());

            TMP_Text text;
            CreateText("Label", buttonObject.transform, label, 25f, TextAlignmentOptions.Center,
                Vector2.zero, Vector2.zero, FontStyles.Bold, Color.white, out text);
            var textRect = (RectTransform)text.transform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            text.raycastTarget = false;
            return button;
        }

        private void CreateText(string name, Transform parent, string value, float fontSize,
            TextAlignmentOptions alignment, Vector2 position, Vector2 size, FontStyles style,
            Color color, out TMP_Text text)
        {
            var textObject = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(parent, false);
            var rect = (RectTransform)textObject.transform;
            SetRect(rect, position, size);
            text = textObject.GetComponent<TextMeshProUGUI>();
            text.font = _font;
            text.text = value;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.fontStyle = style;
            text.color = color;
            text.enableWordWrapping = false;
        }

        private static Image CreateImage(string name, Transform parent, Color color,
            Vector2 position, Vector2 size)
        {
            var imageObject = new GameObject(name, typeof(RectTransform), typeof(Image));
            imageObject.transform.SetParent(parent, false);
            SetRect((RectTransform)imageObject.transform, position, size);
            var image = imageObject.GetComponent<Image>();
            image.color = color;
            return image;
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
}
