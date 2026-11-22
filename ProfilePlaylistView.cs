using System;
using System.Collections.Generic;
using HMUI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProfilesMod
{
    internal sealed partial class ProfileLeaderboardViewController
    {
        private const float PlaylistRowHeight = 5f;
        private readonly List<GameObject> _playlistRows = new List<GameObject>();
        private RectTransform _playlistContent;
        private ScrollRect _playlistScroll;
        private TMP_Text _playlistError;
        private ProfilePlaylistDialog _playlistDialog;
        private Sprite _playlistMaskSprite;
        private Sprite _addBorderSprite;
        private Sprite _editSprite;
        private Sprite _emptyHeartSprite;
        private Sprite _filledHeartSprite;

        private void BuildPlaylists(Transform box)
        {
            var add = MakePlaylistButton("AddPlaylist", transform, "+",
                new Vector2(0.82f, 0.77f), new Vector2(0.96f, 0.85f), FontStyles.Bold, out _);
            var addBorderObject = new GameObject("Border", typeof(RectTransform), typeof(CanvasRenderer));
            addBorderObject.transform.SetParent(add.transform, false);
            addBorderObject.transform.SetAsFirstSibling();
            SetAnchors((RectTransform)addBorderObject.transform, Vector2.zero, Vector2.one);
            _addBorderSprite = CreateRoundedSprite(16, 0f, 3f, 1500f, 3f);
            var addBorder = addBorderObject.AddComponent<ImageView>();
            addBorder.sprite = _addBorderSprite;
            addBorder.material = _imageMaterial;
            addBorder.type = Image.Type.Sliced;
            addBorder.color = Color.white;
            addBorder.raycastTarget = false;
            add.onClick.AddListener(() => OpenPlaylistEditor(null));

            var viewport = new GameObject("PlaylistViewport", typeof(RectTransform), typeof(CanvasRenderer));
            viewport.transform.SetParent(box, false);
            SetAnchors((RectTransform)viewport.transform, new Vector2(0.025f, 0.04f), new Vector2(0.975f, 0.96f));
            var image = viewport.AddComponent<ImageView>();
            _playlistMaskSprite = Sprite.Create(Texture2D.whiteTexture,
                new Rect(0f, 0f, Texture2D.whiteTexture.width, Texture2D.whiteTexture.height),
                new Vector2(0.5f, 0.5f));
            image.sprite = _playlistMaskSprite;
            image.material = _imageMaterial;
            image.color = Color.white;
            var mask = viewport.AddComponent<Mask>();
            mask.showMaskGraphic = false;
            _playlistScroll = viewport.AddComponent<ScrollRect>();
            var content = new GameObject("PlaylistRows", typeof(RectTransform));
            content.transform.SetParent(viewport.transform, false);
            _playlistContent = (RectTransform)content.transform;
            _playlistContent.anchorMin = new Vector2(0f, 1f);
            _playlistContent.anchorMax = new Vector2(1f, 1f);
            _playlistContent.pivot = new Vector2(0.5f, 1f);
            _playlistContent.anchoredPosition = Vector2.zero;
            _playlistScroll.content = _playlistContent;
            _playlistScroll.viewport = (RectTransform)viewport.transform;
            _playlistScroll.horizontal = false;
            _playlistScroll.scrollSensitivity = PlaylistRowHeight;
            _editSprite = CreatePlaylistIcon(false, true);
            _emptyHeartSprite = CreatePlaylistIcon(true, false);
            _filledHeartSprite = CreatePlaylistIcon(true, true);
            _playlistError = CreateText("PlaylistError", "", 2.2f, new Vector2(0.05f, 0.27f),
                new Vector2(0.95f, 0.31f), FontStyles.Normal);
            _playlistError.color = new Color(1f, 0.7f, 0.45f, 1f);
        }

        internal void SetPlaylistError(string message)
        {
            if (_playlistError != null)
                _playlistError.text = message ?? string.Empty;
        }

        private void RefreshPlaylists()
        {
            if (_playlistContent == null)
                return;
            var player = ProfileSoloSession.Player;
            if (player == null)
                return;
            try
            {
                var playlists = ProfileDatabase.GetPlaylists(player.Id, _songId);
                string selectedName = null;
                foreach (var playlist in playlists)
                    if (playlist.Selected)
                        selectedName = playlist.Name;
                ProfileSoloSession.SetPlaylistName(selectedName);
                foreach (var row in _playlistRows)
                {
                    row.SetActive(false);
                    Destroy(row);
                }
                _playlistRows.Clear();
                _playlistContent.sizeDelta = new Vector2(0f, playlists.Length * PlaylistRowHeight);
                var scrollPosition = _playlistContent.anchoredPosition;
                scrollPosition.y = Mathf.Clamp(scrollPosition.y, 0f,
                    Mathf.Max(0f, playlists.Length * PlaylistRowHeight - _playlistScroll.viewport.rect.height));
                _playlistContent.anchoredPosition = scrollPosition;
                for (var i = 0; i < playlists.Length; i++)
                    AddPlaylistRow(playlists[i], i);
                SetPlaylistError(null);
            }
            catch (Exception exception)
            {
                Plugin.Log.Error("Could not load playlists: " + exception);
                SetPlaylistError("Playlists unavailable.");
            }
        }

        private void AddPlaylistRow(PlayerPlaylist playlist, int index)
        {
            var row = new GameObject("PlaylistRow", typeof(RectTransform));
            row.transform.SetParent(_playlistContent, false);
            var rect = (RectTransform)row.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(0f, PlaylistRowHeight);
            rect.anchoredPosition = new Vector2(0f, -index * PlaylistRowHeight);

            var edit = MakePlaylistIconButton("Edit", row.transform, _editSprite,
                new Vector2(0f, 0f), new Vector2(0.15f, 1f));
            edit.onClick.AddListener(() => OpenPlaylistEditor(playlist));
            var title = MakePlaylistButton("Title", row.transform, playlist.Name, new Vector2(0.15f, 0f),
                new Vector2(0.85f, 1f), playlist.Selected ? FontStyles.Bold : FontStyles.Normal, out var titleText);
            if (playlist.Selected)
            {
                var colors = title.colors;
                colors.normalColor = new Color(0.25f, 0.85f, 1f, 1f);
                title.colors = colors;
                titleText.color = colors.normalColor;
            }
            title.onClick.AddListener(() => TogglePlaylist(playlist.Id));
            var heart = MakePlaylistIconButton("SongMembership", row.transform,
                playlist.ContainsSong ? _filledHeartSprite : _emptyHeartSprite,
                new Vector2(0.85f, 0f), new Vector2(1f, 1f));
            heart.interactable = !string.IsNullOrEmpty(_songId);
            heart.onClick.AddListener(() => ToggleSong(playlist.Id));
            _playlistRows.Add(row);
        }

        private Button MakePlaylistButton(string name, Transform parent, string label,
            Vector2 min, Vector2 max, FontStyles style, out TMP_Text text)
        {
            var obj = new GameObject(name, typeof(RectTransform));
            obj.transform.SetParent(parent, false);
            SetAnchors((RectTransform)obj.transform, min, max);
            obj.AddComponent<Touchable>();
            var button = obj.AddComponent<Button>();
            var textObject = new GameObject("Label", typeof(RectTransform));
            textObject.SetActive(false);
            textObject.transform.SetParent(obj.transform, false);
            SetAnchors((RectTransform)textObject.transform, Vector2.zero, Vector2.one);
            text = textObject.AddComponent<CurvedTextMeshPro>();
            text.font = _font;
            text.fontSharedMaterial = _textMaterial;
            text.text = label;
            text.richText = false;
            text.fontStyle = style;
            text.fontSize = 3.2f;
            text.fontSizeMin = 1.2f;
            text.fontSizeMax = 3.2f;
            text.enableAutoSizing = true;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;
            button.targetGraphic = text;
            SetPlaylistButtonColors(button);
            textObject.SetActive(true);
            return button;
        }

        private Button MakePlaylistIconButton(string name, Transform parent, Sprite sprite,
            Vector2 min, Vector2 max)
        {
            var obj = new GameObject(name, typeof(RectTransform));
            obj.transform.SetParent(parent, false);
            SetAnchors((RectTransform)obj.transform, min, max);
            obj.AddComponent<Touchable>();
            var button = obj.AddComponent<Button>();
            var iconObject = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer));
            iconObject.transform.SetParent(obj.transform, false);
            var rect = (RectTransform)iconObject.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(3.2f, 3.2f);
            var icon = iconObject.AddComponent<ImageView>();
            icon.sprite = sprite;
            icon.material = _imageMaterial;
            icon.raycastTarget = false;
            button.targetGraphic = icon;
            SetPlaylistButtonColors(button);
            return button;
        }

        private static void SetPlaylistButtonColors(Button button)
        {
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.45f, 0.9f, 1f, 1f);
            colors.pressedColor = new Color(0.25f, 0.7f, 0.9f, 1f);
            colors.disabledColor = new Color(0.4f, 0.4f, 0.4f, 1f);
            button.colors = colors;
        }

        private static Sprite CreatePlaylistIcon(bool heart, bool filled)
        {
            const int size = 32;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var inside = heart ? InsideHeart(x, y) : InsidePencil(x, y);
                if (inside && heart && !filled)
                    inside = !InsideHeart(x - 2, y) || !InsideHeart(x + 2, y) ||
                        !InsideHeart(x, y - 2) || !InsideHeart(x, y + 2);
                pixels[y * size + x] = new Color32(255, 255, 255, inside ? (byte)255 : (byte)0);
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f));
        }

        private static bool InsideHeart(int x, int y)
        {
            var px = (x - 15.5f) / 11f;
            var py = (y - 16f) / 11f;
            var a = px * px + py * py - 1f;
            return a * a * a - px * px * py * py * py <= 0f;
        }

        private static bool InsidePencil(int x, int y)
        {
            var along = (x + y) * 0.5f;
            var across = Mathf.Abs(x - y);
            return along >= 6f && along <= 26f && across <= 3f;
        }

        private void TogglePlaylist(string id)
        {
            try
            {
                ProfileDatabase.TogglePlaylistSelection(ProfileSoloSession.Player.Id, id);
                ProfilePlaylistFilter.Refresh();
                RefreshLevelStats();
            }
            catch (Exception exception)
            {
                Plugin.Log.Error("Could not select playlist: " + exception);
                SetPlaylistError("Could not select playlist.");
            }
        }

        private void ToggleSong(string id)
        {
            if (string.IsNullOrEmpty(_songId))
                return;
            try
            {
                ProfileDatabase.TogglePlaylistSong(ProfileSoloSession.Player.Id, id, _songId);
                var playlists = ProfileDatabase.GetPlaylists(ProfileSoloSession.Player.Id, _songId);
                foreach (var playlist in playlists)
                {
                    if (playlist.Id == id && playlist.Selected)
                    {
                        ProfilePlaylistFilter.Refresh();
                        break;
                    }
                }
                RefreshLevelStats();
            }
            catch (Exception exception)
            {
                Plugin.Log.Error("Could not update playlist membership: " + exception);
                SetPlaylistError("Could not update playlist.");
            }
        }

        private void OpenPlaylistEditor(PlayerPlaylist playlist)
        {
            try
            {
                if (_playlistDialog == null)
                    _playlistDialog = ProfilePlaylistDialog.Create(this);
                _playlistDialog.Show(playlist);
            }
            catch (Exception exception)
            {
                Plugin.Log.Error("Could not open playlist editor: " + exception);
                SetPlaylistError("Playlist editor unavailable.");
            }
        }

        internal void PlaylistChanged(bool refreshFilter)
        {
            if (refreshFilter)
                ProfilePlaylistFilter.Refresh();
            RefreshLevelStats();
        }

        private void DestroyPlaylistDialog()
        {
            _playlistDialog?.Dispose();
            _playlistDialog = null;
            if (_playlistMaskSprite != null)
                Destroy(_playlistMaskSprite);
            if (_addBorderSprite != null)
            {
                Destroy(_addBorderSprite.texture);
                Destroy(_addBorderSprite);
            }
            DestroyPlaylistIcon(_editSprite);
            DestroyPlaylistIcon(_emptyHeartSprite);
            DestroyPlaylistIcon(_filledHeartSprite);
        }

        private static void DestroyPlaylistIcon(Sprite sprite)
        {
            if (sprite == null)
                return;
            Destroy(sprite.texture);
            Destroy(sprite);
        }
    }
}
