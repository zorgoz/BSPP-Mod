using System;
using System.Globalization;
using HMUI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProfilesMod
{
    internal sealed partial class ProfileLeaderboardViewController : LeaderboardViewController
    {
        private TMP_FontAsset _font;
        private Material _textMaterial;
        private Material _imageMaterial;
        private PlatformLeaderboardViewController _stock;
        private Canvas _stockCanvas;
        private TMP_Text _heading;
        private TMP_Text _status;
        private readonly TMP_Text[] _captions = new TMP_Text[3];
        private readonly TMP_Text[] _values = new TMP_Text[3];
        private string _songId;
        private bool _songIdentifierMissing;
        private Sprite _outlineSprite;

        internal TMP_FontAsset NativeFont => _font;
        internal Material NativeTextMaterial => _textMaterial;
        internal Material NativeImageMaterial => _imageMaterial;

        internal void Initialize(PlatformLeaderboardViewController stock, Canvas stockCanvas,
            CurvedTextMeshPro text, Material imageMaterial)
        {
            _stock = stock;
            _stockCanvas = stockCanvas;
            _font = text.font;
            _textMaterial = text.fontSharedMaterial;
            _imageMaterial = imageMaterial;
        }

        internal void ResetSong()
        {
            _songId = null;
            _songIdentifierMissing = false;
        }

        public override void SetData(in BeatmapKey beatmapKey)
        {
            _songId = beatmapKey.levelId;
            _songIdentifierMissing = beatmapKey != default(BeatmapKey) && string.IsNullOrEmpty(_songId);
            if (isActivated)
                RefreshLevelStats();
        }

        protected override void DidActivate(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
        {
            var canvas = GetComponent<Canvas>();
            var screenCanvas = screen == null ? null : screen.GetComponentInParent<Canvas>();
            if (canvas == null || screenCanvas == null)
                throw new InvalidOperationException("The profile leaderboard has no native screen canvas.");

            var rootCanvas = screenCanvas.rootCanvas;
            var curvedSettings = rootCanvas.GetComponent<CurvedCanvasSettings>();
            if (_stock == null || _stockCanvas == null || curvedSettings == null || curvedSettings.radius <= 0f)
                throw new InvalidOperationException("The native leaderboard's curved screen settings are unavailable.");

            transform.localRotation = _stock.transform.localRotation;
            transform.localScale = _stock.transform.localScale;
            canvas.renderMode = _stockCanvas.renderMode;
            canvas.worldCamera = rootCanvas.worldCamera;
            canvas.planeDistance = _stockCanvas.planeDistance;
            canvas.pixelPerfect = _stockCanvas.pixelPerfect;
            canvas.targetDisplay = _stockCanvas.targetDisplay;
            canvas.additionalShaderChannels = _stockCanvas.additionalShaderChannels |
                rootCanvas.additionalShaderChannels | AdditionalCanvasShaderChannels.TexCoord2;
            canvas.overrideSorting = _stockCanvas.overrideSorting;

            if (firstActivation)
                BuildView();
            RefreshLevelStats();
        }

        public override void RefreshLevelStats()
        {
            if (_heading == null)
                return;
            var player = ProfileSoloSession.Player;
            if (player == null)
            {
                ShowError("Player session unavailable.");
                return;
            }

            _heading.text = "Player: " + player.Name;
            RefreshPlaylists();
            if (string.IsNullOrEmpty(_songId))
            {
                if (_songIdentifierMissing)
                {
                    Plugin.Log.Error("Cannot load profile stats or playlist membership: the selected beatmap has no levelId.");
                    ShowError("Song ID unavailable.");
                    return;
                }
                _status.text = string.Empty;
                for (var i = 0; i < _values.Length; i++)
                {
                    _captions[i].color = new Color(0.65f, 0.65f, 0.65f, 1f);
                    _values[i].color = _captions[i].color;
                    _values[i].text = "-";
                }
                return;
            }

            try
            {
                var summary = ProfileDatabase.GetSongRunSummary(player.Id, _songId);
                var color = summary != null && summary.HasClearedRun
                    ? Color.white : new Color(0.65f, 0.65f, 0.65f, 1f);
                for (var i = 0; i < _values.Length; i++)
                {
                    _captions[i].color = color;
                    _values[i].color = color;
                }
                _status.text = string.Empty;
                _values[0].text = summary == null ? "-" : FormatCombo(summary.Combo);
                _values[1].text = summary == null ? "-" :
                    summary.Points.ToString(CultureInfo.InvariantCulture) + "/" +
                    summary.Score.ToString(CultureInfo.InvariantCulture);
                _values[2].text = summary == null ? "-" : summary.Difficulty;
            }
            catch (Exception exception)
            {
                Plugin.Log.Error("Could not load profile song stats: " + exception);
                ShowError("Stats unavailable.");
            }
        }

        internal static string FormatCombo(long combo)
        {
            return combo < 0
                ? "ALL (" + (-(decimal)combo).ToString(CultureInfo.InvariantCulture) + ")"
                : combo.ToString(CultureInfo.InvariantCulture);
        }

        private void ShowError(string message)
        {
            _status.text = message;
            for (var i = 0; i < _values.Length; i++)
            {
                _captions[i].color = Color.white;
                _values[i].color = Color.white;
                _values[i].text = "!";
            }
        }

        private void BuildView()
        {
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.pivot = _stock.rectTransform.pivot;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;

            _heading = CreateText("Player", "", 4f, new Vector2(0.04f, 0.88f),
                new Vector2(0.96f, 0.98f), FontStyles.Bold);
            CreateText("PlaylistsCaption", "Playlists", 3.5f, new Vector2(0.04f, 0.76f),
                new Vector2(0.96f, 0.86f), FontStyles.Bold);
            var box = new GameObject("PlaylistsBox", typeof(RectTransform), typeof(CanvasRenderer));
            box.transform.SetParent(transform, false);
            SetAnchors((RectTransform)box.transform, new Vector2(0.06f, 0.32f), new Vector2(0.94f, 0.75f));
            _outlineSprite = CreateOutlineSprite();
            var image = box.AddComponent<ImageView>();
            image.sprite = _outlineSprite;
            image.material = _imageMaterial;
            image.type = Image.Type.Sliced;
            image.color = new Color(0.75f, 0.75f, 0.75f, 0.8f);
            image.raycastTarget = false;
            BuildPlaylists(box.transform);

            var labels = new[] { "COMBO", "HIGHSCORE", "DIFFICULTY" };
            for (var i = 0; i < labels.Length; i++)
            {
                var left = 0.02f + i * 0.32f;
                var right = left + 0.32f;
                _captions[i] = CreateText(labels[i] + "Caption", labels[i], 2.8f,
                    new Vector2(left, 0.19f), new Vector2(right, 0.25f), FontStyles.Bold);
                _values[i] = CreateText(labels[i] + "Value", "-", 3.2f,
                    new Vector2(left, 0.08f), new Vector2(right, 0.18f), FontStyles.Normal);
            }
            _status = CreateText("Status", "", 2.5f, new Vector2(0.05f, 0.01f),
                new Vector2(0.95f, 0.07f), FontStyles.Normal);
            _status.color = new Color(1f, 0.7f, 0.45f, 1f);
        }

        private TMP_Text CreateText(string name, string value, float size,
            Vector2 min, Vector2 max, FontStyles style)
        {
            var obj = new GameObject(name, typeof(RectTransform));
            obj.transform.SetParent(transform, false);
            SetAnchors((RectTransform)obj.transform, min, max);
            var text = obj.AddComponent<CurvedTextMeshPro>();
            text.font = _font;
            text.fontSharedMaterial = _textMaterial;
            text.text = value;
            text.richText = false;
            text.fontSize = size;
            text.fontStyle = style;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.enableAutoSizing = true;
            text.fontSizeMin = 1.2f;
            text.fontSizeMax = size;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;
            return text;
        }

        private static void SetAnchors(RectTransform rect, Vector2 min, Vector2 max)
        {
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static Sprite CreateOutlineSprite()
        {
            const int size = 64;
            const float radius = 12f;
            const float thickness = 2f;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var outer = InsideRoundedRect(x + 0.5f, y + 0.5f, 0f, size, radius);
                var inner = InsideRoundedRect(x + 0.5f, y + 0.5f, thickness, size - thickness, radius - thickness);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(outer && !inner ? 255 : 0));
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            return Sprite.Create(texture, new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f), 400f, 0, SpriteMeshType.FullRect,
                new Vector4(radius, radius, radius, radius));
        }

        private static bool InsideRoundedRect(float x, float y, float min, float max, float radius)
        {
            if (x < min || x > max || y < min || y > max)
                return false;
            var dx = Mathf.Max(Mathf.Max(min + radius - x, x - (max - radius)), 0f);
            var dy = Mathf.Max(Mathf.Max(min + radius - y, y - (max - radius)), 0f);
            return dx * dx + dy * dy <= radius * radius;
        }

        protected override void OnDestroy()
        {
            DestroyPlaylistDialog();
            base.OnDestroy();
            if (_outlineSprite != null)
            {
                Destroy(_outlineSprite.texture);
                Destroy(_outlineSprite);
            }
        }
    }
}
