using System;
using System.Globalization;
using System.Threading;
using HarmonyLib;

namespace ProfilesMod
{
    internal sealed class ProfileRun
    {
        internal string Id { get; set; }
        internal string PlayerId { get; set; }
        internal string SongId { get; set; }
        internal DateTimeOffset When { get; set; }
        internal string Mode { get; set; }
        internal string Difficulty { get; set; }
        internal int Score { get; set; }
        internal int Points { get; set; }
        internal string Modifiers { get; set; }
        internal bool Cleared { get; set; }
        internal int Combo { get; set; }
    }

    internal sealed class ProfileRunRecorder
    {
        private readonly string _playerId;
        private readonly BeatmapKey _key;
        private readonly Action<StandardLevelScenesTransitionSetupData, LevelCompletionResults> _finished;
        private int _handled;

        internal ProfileRunRecorder(string playerId, BeatmapKey key,
            Action<StandardLevelScenesTransitionSetupData, LevelCompletionResults> finished)
        {
            _playerId = playerId;
            _key = key;
            _finished = finished;
        }

        internal void Finish(StandardLevelScenesTransitionSetupData setup, LevelCompletionResults result)
        {
            if (Interlocked.Exchange(ref _handled, 1) == 0)
            {
                try
                {
                    if (result == null || setup == null)
                        throw new InvalidOperationException("The level completion result or setup is missing.");
                    if (setup.gameMode != GameMode.Solo || setup.practiceSettings != null || setup.beatmapKey != _key)
                        throw new InvalidOperationException("The completed level does not match the captured Solo attempt.");

                    if (IsCompleted(result.levelEndStateType, result.levelEndAction))
                        ProfileDatabase.InsertRun(CreateRun(_playerId, _key, result));
                }
                catch (Exception exception)
                {
                    Plugin.Log.Error("Could not save the completed profile run: " + exception);
                }
            }
            _finished(setup, result);
        }

        internal static bool IsCompleted(LevelCompletionResultsBase.LevelEndStateType state,
            LevelCompletionResultsBase.LevelEndAction action)
        {
            return action == LevelCompletionResultsBase.LevelEndAction.None &&
                (state == LevelCompletionResultsBase.LevelEndStateType.Cleared ||
                 state == LevelCompletionResultsBase.LevelEndStateType.Failed);
        }

        internal static ProfileRun CreateRun(string playerId, BeatmapKey key, LevelCompletionResults result)
        {
            if (string.IsNullOrEmpty(playerId) || string.IsNullOrEmpty(key.levelId) ||
                result.gameplayModifiers == null)
                throw new InvalidOperationException("The completed run is missing its player, song, mode, or modifiers.");
            if (result.maxCombo < 0)
                throw new InvalidOperationException("The game's maximum combo is negative.");

            var mode = key.characteristic.SerializedName();
            if (string.IsNullOrEmpty(mode))
                throw new InvalidOperationException("The beatmap characteristic has no serialized name.");
            var modifiers = result.gameplayModifiers;
            var json = string.Format(CultureInfo.InvariantCulture,
                "{{\"energyType\":{0},\"noFailOn0Energy\":{1},\"instaFail\":{2}," +
                "\"failOnSaberClash\":{3},\"enabledObstacleType\":{4},\"noBombs\":{5}," +
                "\"fastNotes\":{6},\"strictAngles\":{7},\"disappearingArrows\":{8}," +
                "\"songSpeed\":{9},\"noArrows\":{10},\"ghostNotes\":{11}," +
                "\"proMode\":{12},\"zenMode\":{13},\"smallCubes\":{14}}}",
                (int)modifiers.energyType, JsonBool(modifiers.noFailOn0Energy),
                JsonBool(modifiers.instaFail), JsonBool(modifiers.failOnSaberClash),
                (int)modifiers.enabledObstacleType, JsonBool(modifiers.noBombs),
                JsonBool(modifiers.fastNotes), JsonBool(modifiers.strictAngles),
                JsonBool(modifiers.disappearingArrows), (int)modifiers.songSpeed,
                JsonBool(modifiers.noArrows), JsonBool(modifiers.ghostNotes),
                JsonBool(modifiers.proMode), JsonBool(modifiers.zenMode), JsonBool(modifiers.smallCubes));

            return new ProfileRun
            {
                Id = ProfileDatabase.NewId(),
                PlayerId = playerId,
                SongId = key.levelId,
                When = DateTimeOffset.UtcNow,
                Mode = mode,
                Difficulty = key.difficulty.SerializedName(),
                Score = result.score.modified,
                Points = result.score.multiplied,
                Modifiers = json,
                Cleared = result.levelEndStateType == LevelCompletionResultsBase.LevelEndStateType.Cleared,
                Combo = result.levelEndStateType == LevelCompletionResultsBase.LevelEndStateType.Cleared &&
                    result.fullCombo ? -result.maxCombo : result.maxCombo
            };
        }

        private static string JsonBool(bool value) => value ? "true" : "false";
    }

    [HarmonyPatch(typeof(MenuTransitionsHelper), nameof(MenuTransitionsHelper.StartStandardLevel))]
    internal static class ProfileRunStartPatch
    {
        private static void Prefix(GameMode gameMode, in BeatmapKey beatmapKey, PracticeSettings practiceSettings,
            ref Action<StandardLevelScenesTransitionSetupData, LevelCompletionResults> levelFinishedCallback)
        {
            var solo = levelFinishedCallback == null ? null : levelFinishedCallback.Target as SoloFreePlayFlowCoordinator;
            var player = ProfileSoloSession.PlayerFor(solo);
            if (player == null || gameMode != GameMode.Solo || practiceSettings != null)
                return;
            var recorder = new ProfileRunRecorder(player.Id, beatmapKey, levelFinishedCallback);
            levelFinishedCallback = recorder.Finish;
        }
    }
}
