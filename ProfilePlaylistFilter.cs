using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using HMUI;
using UnityEngine;

namespace ProfilesMod
{
    internal static class ProfilePlaylistFilter
    {
        private static LevelCollectionNavigationController _collection;
        private static BeatmapLevelPack _pack;
        private static bool _showPackHeader;
        private static bool _showPracticeButton;
        private static string _actionButtonText;
        private static bool _sortAlphabetically;
        private static GameObject _noDataInfoPrefab;
        private static BeatmapDifficultyMask _allowed;
        private static BeatmapCharacteristic[] _notAllowed;
        private static bool _isFiltered;

        internal static bool IsProfileCollection(LevelCollectionNavigationController collection)
        {
            var solo = ProfileSoloSession.ActiveSolo;
            if (solo == null)
                return false;
            var navigation = ProfileFlowCoordinator.ReadDependency<LevelSelectionNavigationController>(
                solo, typeof(LevelSelectionFlowCoordinator), "levelSelectionNavigationController");
            var nativeCollection = ProfileFlowCoordinator.ReadDependency<LevelCollectionNavigationController>(
                navigation, typeof(LevelSelectionNavigationController), "_levelCollectionNavigationController");
            return nativeCollection != null && nativeCollection == collection;
        }

        internal static void Capture(LevelCollectionNavigationController collection, BeatmapLevelPack pack,
            bool showPackHeader, bool showPracticeButton, string actionButtonText, bool sortAlphabetically,
            GameObject noDataInfoPrefab, BeatmapDifficultyMask allowed,
            BeatmapCharacteristic[] notAllowed, bool isFiltered)
        {
            _collection = collection;
            _pack = pack;
            _showPackHeader = showPackHeader;
            _showPracticeButton = showPracticeButton;
            _actionButtonText = actionButtonText;
            _sortAlphabetically = sortAlphabetically;
            _noDataInfoPrefab = noDataInfoPrefab;
            _allowed = allowed;
            _notAllowed = notAllowed;
            _isFiltered = isFiltered;
        }

        internal static void Refresh()
        {
            if (_collection == null || !IsProfileCollection(_collection))
                return;
            var oldLevel = _collection.beatmapLevel;
            _collection.SetData(_pack, _showPackHeader, _showPracticeButton, _actionButtonText,
                _sortAlphabetically, _noDataInfoPrefab, _allowed, _notAllowed, _isFiltered);
            var ids = ProfileDatabase.GetSelectedPlaylistSongs(ProfileSoloSession.Player.Id, out var selected);
            var stillVisible = oldLevel != null && _pack != null &&
                _pack.AllBeatmapLevels().Contains(oldLevel) &&
                (!selected || Array.IndexOf(ids, oldLevel.levelID) >= 0);
            if (stillVisible)
                _collection.SelectLevel(oldLevel);
            else
            {
                _collection.ClearSelected();
                var key = default(BeatmapKey);
                ProfileSoloSession.ShowPanel(ProfileSoloSession.ActiveSolo, in key);
            }
        }

        internal static void Clear()
        {
            _collection = null;
            _pack = null;
            _noDataInfoPrefab = null;
            _notAllowed = null;
        }

        internal static BeatmapLevel[] Apply(BeatmapLevelPack pack, IEnumerable<string> songIds)
        {
            var ids = new HashSet<string>(songIds, StringComparer.Ordinal);
            return pack == null ? Array.Empty<BeatmapLevel>() :
                pack.AllBeatmapLevels().Where(level => level != null && ids.Contains(level.levelID)).ToArray();
        }
    }

    [HarmonyPatch(typeof(LevelCollectionNavigationController), nameof(LevelCollectionNavigationController.SetData))]
    internal static class ProfilePlaylistFilterPatch
    {
        private static bool Prefix(LevelCollectionNavigationController __instance,
            ref BeatmapLevelPack beatmapLevelPack, bool showPackHeader, bool showPracticeButton,
            string actionButtonText, bool sortAlphabetically, GameObject noDataInfoPrefab,
            BeatmapDifficultyMask allowedBeatmapDifficultyMask, BeatmapCharacteristic[] notAllowedCharacteristics,
            ref bool isFiltered)
        {
            if (!ProfilePlaylistFilter.IsProfileCollection(__instance))
                return true;

            ProfilePlaylistFilter.Capture(__instance, beatmapLevelPack, showPackHeader,
                showPracticeButton, actionButtonText, sortAlphabetically, noDataInfoPrefab,
                allowedBeatmapDifficultyMask, notAllowedCharacteristics, isFiltered);
            try
            {
                var ids = ProfileDatabase.GetSelectedPlaylistSongs(ProfileSoloSession.Player.Id, out var selected);
                if (selected)
                {
                    beatmapLevelPack = BeatmapLevelPack.CreateLevelPackForFiltering(
                        ProfilePlaylistFilter.Apply(beatmapLevelPack, ids));
                    isFiltered = true;
                }
                return true;
            }
            catch (Exception exception)
            {
                Plugin.Log.Error("Could not filter songs by playlist: " + exception);
                ProfileSoloSession.ShowPlaylistError("Playlist filter unavailable.");
                return false;
            }
        }
    }

    [HarmonyPatch(typeof(SinglePlayerLevelSelectionFlowCoordinator), "SelectionDidChange")]
    internal static class ProfileEmptySongSelectionPatch
    {
        private static bool Prefix(SinglePlayerLevelSelectionFlowCoordinator __instance, in BeatmapKey beatmapKey)
        {
            var solo = __instance as SoloFreePlayFlowCoordinator;
            if (solo == null || ProfileSoloSession.PlayerFor(solo) == null || beatmapKey != default(BeatmapKey))
                return true;

            ProfileSoloSession.ShowPanel(solo, in beatmapKey);
            return false;
        }
    }

    [HarmonyPatch(typeof(SinglePlayerLevelSelectionFlowCoordinator), "LevelSelectionFlowCoordinatorTopViewControllerWillChange")]
    internal static class ProfileRootScreenPatch
    {
        private static void Postfix(SinglePlayerLevelSelectionFlowCoordinator __instance, ViewController newViewController)
        {
            var solo = __instance as SoloFreePlayFlowCoordinator;
            if (solo == null || ProfileSoloSession.PlayerFor(solo) == null)
                return;
            var navigation = ProfileFlowCoordinator.ReadDependency<LevelSelectionNavigationController>(
                solo, typeof(LevelSelectionFlowCoordinator), "levelSelectionNavigationController");
            if (navigation == newViewController && navigation.beatmapKey == default(BeatmapKey))
            {
                var key = default(BeatmapKey);
                ProfileSoloSession.ShowPanel(solo, in key);
            }
        }

        [HarmonyPatch(typeof(FlowCoordinator), "InitialViewControllerWasPresented")]
        internal static class ProfileInitialScreenPatch
        {
            private static void Postfix(FlowCoordinator __instance)
            {
                var solo = __instance as SoloFreePlayFlowCoordinator;
                if (solo == null || ProfileSoloSession.PlayerFor(solo) == null)
                    return;
                var navigation = ProfileFlowCoordinator.ReadDependency<LevelSelectionNavigationController>(
                    solo, typeof(LevelSelectionFlowCoordinator), "levelSelectionNavigationController");
                if (navigation != null && navigation.beatmapKey == default(BeatmapKey))
                {
                    var key = default(BeatmapKey);
                    ProfileSoloSession.ShowPanel(solo, in key);
                }
            }
        }
    }
}
