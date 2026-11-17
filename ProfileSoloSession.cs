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

        internal static PlayerProfile Player => _active ? _player : null;

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
                Clear();
        }

        internal static bool TryGetView(SoloFreePlayFlowCoordinator solo,
            out LeaderboardViewController view)
        {
            view = _active && solo == _solo ? _view : null;
            return view != null;
        }

        internal static void Clear()
        {
            _player = null;
            _main = null;
            _prompt = null;
            _pending = false;
            _active = false;
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
}
