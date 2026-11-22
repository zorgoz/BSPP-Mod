using System;
using HarmonyLib;
using HMUI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using VRUIControls;
using Zenject;

namespace ProfilesMod
{
    internal static class ProfileSoloSession
    {
        private static PlayerProfile _player;
        private static FlowCoordinator _main;
        private static SoloFreePlayFlowCoordinator _solo;
        private static SimpleDialogPromptViewController _prompt;
        private static ProfileLeaderboardViewController _view;
        private static bool _pending;
        private static bool _active;
        private static DiContainer _container;
        private static string _playlistName;
        private static HiddenControls _categoryControls;
        private static HiddenControls _packControls;
        private static Component _hiddenCategorySelector;
        private static bool _categorySelectorVisible;
        private static AnnotatedBeatmapLevelCollectionsGridView _hiddenPackGrid;
        private static bool _packGridVisible;

        private sealed class HiddenControls
        {
            private readonly CanvasGroup _group;
            private readonly bool _created;
            private readonly float _alpha;
            private readonly bool _interactable;
            private readonly bool _blocksRaycasts;

            internal HiddenControls(GameObject controls)
            {
                _group = controls.GetComponent<CanvasGroup>();
                _created = _group == null;
                if (_created)
                    _group = controls.AddComponent<CanvasGroup>();
                _alpha = _group.alpha;
                _interactable = _group.interactable;
                _blocksRaycasts = _group.blocksRaycasts;
                _group.alpha = 0f;
                _group.interactable = false;
                _group.blocksRaycasts = false;
            }

            internal void Restore()
            {
                if (_group == null)
                    return;
                _group.alpha = _alpha;
                _group.interactable = _interactable;
                _group.blocksRaycasts = _blocksRaycasts;
                if (_created)
                    UnityEngine.Object.Destroy(_group);
            }
        }

        internal static PlayerProfile Player => _active ? _player : null;
        internal static SoloFreePlayFlowCoordinator ActiveSolo => _active ? _solo : null;
        internal static DiContainer Container => _container;

        internal static PlayerProfile PlayerFor(SoloFreePlayFlowCoordinator solo)
        {
            return _active && solo == _solo ? _player : null;
        }

        internal static void Prepare(SoloFreePlayFlowCoordinator solo, DiContainer container)
        {
            if (_view != null && _solo == solo)
                return;
            if (_view != null)
                UnityEngine.Object.Destroy(_view.gameObject);

            var stock = ProfileFlowCoordinator.ReadDependency<PlatformLeaderboardViewController>(
                solo, typeof(SoloFreePlayFlowCoordinator), "_platformLeaderboardViewController");
            var stats = ProfileFlowCoordinator.ReadDependency<LevelStatsView>(
                stock, typeof(PlatformLeaderboardViewController), "_levelStatsView");
            var text = ProfileFlowCoordinator.ReadDependency<TMPro.TextMeshProUGUI>(
                stats, typeof(LevelStatsView), "_highScoreText") as CurvedTextMeshPro;
            var background = stock == null ? null : stock.transform.Find("HeaderPanel/BG");
            var image = background == null ? null : background.GetComponent<ImageView>();
            var stockCanvas = stock == null ? null : stock.GetComponent<Canvas>();
            if (stock == null || stockCanvas == null || text == null || text.font == null ||
                text.fontSharedMaterial == null || image == null || image.material == null)
                throw new InvalidOperationException("The native curved leaderboard rendering templates are unavailable.");

            var viewObject = new GameObject("ProfilesMod.ProfileLeaderboard",
                typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
            viewObject.SetActive(false);
            viewObject.transform.SetParent(solo.transform, false);
            viewObject.transform.localRotation = stock.transform.localRotation;
            viewObject.transform.localScale = stock.transform.localScale;
            try
            {
                var inputModule = container.Resolve<BaseInputModule>();
                if (inputModule is VRInputModule)
                    container.InstantiateComponent<VRGraphicRaycaster>(viewObject, new object[0]);
                else
                    viewObject.AddComponent<GraphicRaycaster>();
                _view = viewObject.AddComponent<ProfileLeaderboardViewController>();
                _view.Initialize(stock, stockCanvas, text, image.material);
                _solo = solo;
                _container = container;
            }
            catch
            {
                UnityEngine.Object.Destroy(viewObject);
                throw;
            }
        }

        internal static void Begin(PlayerProfile player, FlowCoordinator main,
            SoloFreePlayFlowCoordinator solo, SimpleDialogPromptViewController prompt)
        {
            if (_view == null || _solo != solo)
                throw new InvalidOperationException("The profile leaderboard has not been prepared.");
            _player = player;
            _main = main;
            _prompt = prompt;
            _pending = true;
            _active = false;
            _view.ResetSong();
        }

        internal static void CheckPendingTransition()
        {
            if (_pending && (_main == null || _main.topViewController != _prompt))
                Clear();
        }

        internal static bool Presenting(FlowCoordinator parent, FlowCoordinator child)
        {
            if (!_pending || parent != _main)
                return false;
            if (child == _solo)
            {
                _pending = false;
                _active = true;
                return true;
            }
            Clear();
            return false;
        }

        internal static void PromptRemoved(SimpleDialogPromptViewController prompt)
        {
            if (_pending && prompt == _prompt)
                Clear();
        }

        internal static void TutorialStarting()
        {
            if (_pending)
                Clear();
        }

        internal static void SoloRemoved(SoloFreePlayFlowCoordinator solo)
        {
            if (_active && solo == _solo)
            {
                try
                {
                    ProfileDatabase.ClearPlaylistSelection(_player.Id);
                }
                catch (Exception exception)
                {
                    Plugin.Log.Error("Could not reset the playlist filter on Solo exit: " + exception);
                }
                Clear();
            }
        }

        internal static bool TryGetView(SoloFreePlayFlowCoordinator solo,
            out LeaderboardViewController view)
        {
            view = _active && solo == _solo ? _view : null;
            return view != null;
        }

        internal static void Clear()
        {
            RestoreCategoryControls();
            ProfilePlaylistFilter.Clear();
            _playlistName = null;
            _player = null;
            _main = null;
            _prompt = null;
            _pending = false;
            _active = false;
        }

        internal static string MainTitle => _playlistName == null
            ? "Solo: " + _player.Name
            : "Playlist: " + _player.Name + " / " + _playlistName;

        internal static void SetPlaylistName(string playlistName)
        {
            if (!_active || _solo == null)
                return;
            if (playlistName != null)
                HideCategoryControls();
            else
                RestoreCategoryControls();
            var changed = _playlistName != playlistName;
            _playlistName = playlistName;
            if (!changed)
                return;
            var navigation = ProfileFlowCoordinator.ReadDependency<LevelSelectionNavigationController>(
                _solo, typeof(LevelSelectionFlowCoordinator), "levelSelectionNavigationController");
            if (_solo.topViewController != navigation)
                return;
            var setter = AccessTools.Method(typeof(FlowCoordinator), "SetTitle");
            if (setter == null)
                throw new MissingMethodException("FlowCoordinator.SetTitle");
            setter.Invoke(_solo, new object[] { MainTitle, ViewController.AnimationType.None });
        }

        private static void HideCategoryControls()
        {
            if (_categoryControls != null)
                return;
            var navigation = ProfileFlowCoordinator.ReadDependency<LevelSelectionNavigationController>(
                _solo, typeof(LevelSelectionFlowCoordinator), "levelSelectionNavigationController");
            var filtering = ProfileFlowCoordinator.ReadDependency<LevelFilteringNavigationController>(
                navigation, typeof(LevelSelectionNavigationController), "_levelFilteringNavigationController");
            var categories = ProfileFlowCoordinator.ReadDependency<SelectLevelCategoryViewController>(
                filtering, typeof(LevelFilteringNavigationController), "_selectLevelCategoryViewController");
            var selector = ProfileFlowCoordinator.ReadDependency<Component>(
                categories, typeof(SelectLevelCategoryViewController), "_levelFilterCategoryIconSegmentedControl");
            var packs = ProfileFlowCoordinator.ReadDependency<AnnotatedBeatmapLevelCollectionsViewController>(
                filtering, typeof(LevelFilteringNavigationController), "_annotatedBeatmapLevelCollectionsViewController");
            var grid = ProfileFlowCoordinator.ReadDependency<AnnotatedBeatmapLevelCollectionsGridView>(
                packs, typeof(AnnotatedBeatmapLevelCollectionsViewController), "_annotatedBeatmapLevelCollectionsGridView");
            if (categories == null || selector == null || packs == null || grid == null)
                throw new InvalidOperationException("The native song category or pack controls are unavailable.");
            _categoryControls = new HiddenControls(categories.gameObject);
            try
            {
                _packControls = new HiddenControls(packs.gameObject);
                _hiddenCategorySelector = selector;
                _categorySelectorVisible = selector.gameObject.activeSelf;
                selector.gameObject.SetActive(false);
                _hiddenPackGrid = grid;
                _packGridVisible = grid.gameObject.activeSelf;
                grid.gameObject.SetActive(false);
            }
            catch
            {
                _categoryControls.Restore();
                _categoryControls = null;
                _packControls?.Restore();
                _packControls = null;
                if (_hiddenCategorySelector != null)
                    _hiddenCategorySelector.gameObject.SetActive(_categorySelectorVisible);
                _hiddenCategorySelector = null;
                _hiddenPackGrid = null;
                throw;
            }
        }

        private static void RestoreCategoryControls()
        {
            var grid = _hiddenPackGrid;
            var visible = _packGridVisible;
            var selector = _hiddenCategorySelector;
            var selectorVisible = _categorySelectorVisible;
            _hiddenPackGrid = null;
            _hiddenCategorySelector = null;
            _categoryControls?.Restore();
            _packControls?.Restore();
            _categoryControls = null;
            _packControls = null;
            if (selector != null)
                selector.gameObject.SetActive(selectorVisible);
            if (grid != null)
                grid.gameObject.SetActive(visible);
        }

        internal static bool SetPackGridVisibility(AnnotatedBeatmapLevelCollectionsGridView grid, bool visible)
        {
            if (_hiddenPackGrid == null || grid != _hiddenPackGrid)
                return true;
            _packGridVisible = visible;
            return false;
        }

        internal static void ShowPanel(SoloFreePlayFlowCoordinator solo, in BeatmapKey key)
        {
            if (PlayerFor(solo) == null || _view == null)
                return;
            _view.SetData(in key);
            var setter = AccessTools.Method(typeof(FlowCoordinator), "SetRightScreenViewController");
            if (setter == null)
                throw new MissingMethodException("FlowCoordinator.SetRightScreenViewController");
            setter.Invoke(solo, new object[] { _view, ViewController.AnimationType.None });
        }

        internal static void ShowPlaylistError(string message)
        {
            if (_view != null)
                _view.SetPlaylistError(message);
        }
    }

    [HarmonyPatch(typeof(AnnotatedBeatmapLevelCollectionsGridView), nameof(AnnotatedBeatmapLevelCollectionsGridView.Show))]
    internal static class ProfilePackGridShowPatch
    {
        private static bool Prefix(AnnotatedBeatmapLevelCollectionsGridView __instance)
        {
            return ProfileSoloSession.SetPackGridVisibility(__instance, true);
        }
    }

    [HarmonyPatch(typeof(AnnotatedBeatmapLevelCollectionsGridView), nameof(AnnotatedBeatmapLevelCollectionsGridView.Hide))]
    internal static class ProfilePackGridHidePatch
    {
        private static bool Prefix(AnnotatedBeatmapLevelCollectionsGridView __instance)
        {
            return ProfileSoloSession.SetPackGridVisibility(__instance, false);
        }
    }

    [HarmonyPatch(typeof(FlowCoordinator), nameof(FlowCoordinator.PresentFlowCoordinator))]
    internal static class ProfileSoloPresentPatch
    {
        private static void Prefix(FlowCoordinator __instance, FlowCoordinator flowCoordinator, out bool __state)
        {
            __state = ProfileSoloSession.Presenting(__instance, flowCoordinator);
        }

        private static Exception Finalizer(Exception __exception, bool __state)
        {
            if (__exception != null && __state)
                ProfileSoloSession.Clear();
            return __exception;
        }
    }

    [HarmonyPatch(typeof(SimpleDialogPromptViewController), "DidDeactivate")]
    internal static class ProfileTutorialPromptPatch
    {
        private static void Postfix(SimpleDialogPromptViewController __instance, bool removedFromHierarchy)
        {
            if (removedFromHierarchy)
                ProfileSoloSession.PromptRemoved(__instance);
        }
    }

    [HarmonyPatch(typeof(MenuTransitionsHelper), nameof(MenuTransitionsHelper.StartTutorial))]
    internal static class ProfileTutorialStartPatch
    {
        private static void Prefix()
        {
            ProfileSoloSession.TutorialStarting();
        }
    }

    [HarmonyPatch(typeof(SoloFreePlayFlowCoordinator), "SinglePlayerLevelSelectionFlowCoordinatorDidDeactivate")]
    internal static class ProfileSoloExitPatch
    {
        private static void Postfix(SoloFreePlayFlowCoordinator __instance, bool removedFromHierarchy)
        {
            if (removedFromHierarchy)
                ProfileSoloSession.SoloRemoved(__instance);
        }

        [HarmonyPatch(typeof(LevelSelectionFlowCoordinator), "DidActivate")]
        internal static class ProfileSoloActivationPatch
        {
            private static void Postfix(LevelSelectionFlowCoordinator __instance)
            {
                var solo = __instance as SoloFreePlayFlowCoordinator;
                LeaderboardViewController view;
                if (solo == null || !ProfileSoloSession.TryGetView(solo, out view))
                    return;

                var navigation = ProfileFlowCoordinator.ReadDependency<LevelSelectionNavigationController>(
                    solo, typeof(LevelSelectionFlowCoordinator), "levelSelectionNavigationController");
                if (navigation == null)
                {
                    Plugin.Log.Error("Cannot refresh profile stats: Solo's level selection is unavailable.");
                    return;
                }
                var key = navigation.beatmapKey;
                view.SetData(in key);
            }
        }
    }

    [HarmonyPatch(typeof(SoloFreePlayFlowCoordinator), "get_leaderboardViewController")]
    internal static class ProfileLeaderboardPatch
    {
        private static bool Prefix(SoloFreePlayFlowCoordinator __instance, ref LeaderboardViewController __result)
        {
            LeaderboardViewController view;
            if (!ProfileSoloSession.TryGetView(__instance, out view))
                return true;
            __result = view;
            return false;
        }
    }

    [HarmonyPatch(typeof(SoloFreePlayFlowCoordinator), "get_mainTitle")]
    internal static class ProfileSoloTitlePatch
    {
        private static void Postfix(SoloFreePlayFlowCoordinator __instance, ref string __result)
        {
            if (ProfileSoloSession.PlayerFor(__instance) != null)
                __result = ProfileSoloSession.MainTitle;
        }
    }
}
