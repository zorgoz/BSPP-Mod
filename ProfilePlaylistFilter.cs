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
        private static bool _awaitingCatalog;
        private static BeatmapLevel[] _playlistLevels;
        private static bool _hasPendingSelection;
        private static BeatmapLevel _pendingSelection;

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
            _hasPendingSelection = false;
            _pendingSelection = null;
            _collection.SetData(_pack, _showPackHeader, _showPracticeButton, _actionButtonText,
                _sortAlphabetically, _noDataInfoPrefab, _allowed, _notAllowed, _isFiltered);
            if (_awaitingCatalog)
            {
                _pendingSelection = oldLevel;
                _hasPendingSelection = true;
                return;
            }
            RestoreSelection(oldLevel);
        }

        private static void RestoreSelection(BeatmapLevel oldLevel)
        {
            IEnumerable<BeatmapLevel> levels = _playlistLevels;
            if (levels == null)
                levels = _pack?.AllBeatmapLevels();
            var selectedLevel = oldLevel == null ? null :
                levels?.FirstOrDefault(level => level != null && level.levelID == oldLevel.levelID);
            if (selectedLevel != null)
                _collection.SelectLevel(selectedLevel);
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
            _awaitingCatalog = false;
            _playlistLevels = null;
            _hasPendingSelection = false;
            _pendingSelection = null;
        }

        internal static BeatmapLevel[] Apply(IEnumerable<BeatmapLevelPack> packs, IEnumerable<string> songIds)
        {
            var ids = new HashSet<string>(songIds, StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            return packs.Where(pack => pack != null).SelectMany(pack => pack.AllBeatmapLevels())
                .Where(level => level != null && ids.Contains(level.levelID) && seen.Add(level.levelID))
                .ToArray();
        }

        internal static bool SetPlaylistSource(LevelCollectionNavigationController collection,
            IEnumerable<string> songIds, out BeatmapLevelPack pack)
        {
            pack = null;
            var navigation = ProfileFlowCoordinator.ReadDependency<LevelSelectionNavigationController>(
                ProfileSoloSession.ActiveSolo, typeof(LevelSelectionFlowCoordinator), "levelSelectionNavigationController");
            var filtering = ProfileFlowCoordinator.ReadDependency<LevelFilteringNavigationController>(
                navigation, typeof(LevelSelectionNavigationController), "_levelFilteringNavigationController");
            if (filtering == null)
                throw new InvalidOperationException("The native song library is unavailable.");
            if (AccessTools.Field(typeof(LevelFilteringNavigationController), "_allBeatmapLevelPacks") == null)
                throw new MissingFieldException("LevelFilteringNavigationController._allBeatmapLevelPacks");
            var packs = ProfileFlowCoordinator.ReadDependency<BeatmapLevelPack[]>(
                filtering, typeof(LevelFilteringNavigationController), "_allBeatmapLevelPacks");
            if (packs == null)
            {
                var load = AccessTools.Method(typeof(LevelFilteringNavigationController), "UpdateCustomSongs");
                if (load == null)
                    throw new MissingMethodException("LevelFilteringNavigationController.UpdateCustomSongs");
                _awaitingCatalog = true;
                collection.ShowLoading();
                load.Invoke(filtering, null);
                return false;
            }

            _awaitingCatalog = false;
            _playlistLevels = Apply(packs, songIds);
            pack = BeatmapLevelPack.CreateLevelPackForFiltering(_playlistLevels);
            return true;
        }

        internal static void ClearPlaylistSource()
        {
            _awaitingCatalog = false;
            _playlistLevels = null;
        }

        internal static void SourceUnavailable()
        {
            _awaitingCatalog = true;
        }

        internal static void DataApplied(LevelCollectionNavigationController collection)
        {
            if (collection != _collection || _awaitingCatalog || !_hasPendingSelection)
                return;
            var oldLevel = _pendingSelection;
            _hasPendingSelection = false;
            _pendingSelection = null;
            RestoreSelection(oldLevel);
        }
    }

    [HarmonyPatch(typeof(LevelCollectionNavigationController), nameof(LevelCollectionNavigationController.SetData))]
    internal static class ProfilePlaylistFilterPatch
    {
        private static bool Prefix(LevelCollectionNavigationController __instance,
            ref BeatmapLevelPack beatmapLevelPack, bool showPackHeader, bool showPracticeButton,
            string actionButtonText, bool sortAlphabetically, ref GameObject noDataInfoPrefab,
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
                    if (!ProfilePlaylistFilter.SetPlaylistSource(__instance, ids, out var playlistPack))
                        return false;
                    beatmapLevelPack = playlistPack;
                    isFiltered = true;
                    noDataInfoPrefab = null;
                }
                else
                    ProfilePlaylistFilter.ClearPlaylistSource();
                return true;
            }
            catch (Exception exception)
            {
                ProfilePlaylistFilter.SourceUnavailable();
                Plugin.Log.Error("Could not filter songs by playlist: " + exception);
                ProfileSoloSession.ShowPlaylistError("Playlist filter unavailable.");
                return false;
            }
        }

        private static void Postfix(LevelCollectionNavigationController __instance)
        {
            if (ProfilePlaylistFilter.IsProfileCollection(__instance))
                ProfilePlaylistFilter.DataApplied(__instance);
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
