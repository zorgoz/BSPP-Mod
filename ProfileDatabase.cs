using System;
using System.Data;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;

namespace ProfilesMod
{
    internal static partial class ProfileDatabase
    {
        private static readonly string[] SchemaStatements =
        {
            "CREATE TABLE IF NOT EXISTS players (" +
                "id TEXT NOT NULL PRIMARY KEY, " +
                "name TEXT NOT NULL, " +
                "selected INTEGER NOT NULL DEFAULT 0 CHECK (selected IN (0, 1)))",
            "CREATE UNIQUE INDEX IF NOT EXISTS UX_players_selected " +
                "ON players (selected) WHERE selected = 1",
            "CREATE TABLE IF NOT EXISTS playlists (" +
                "id TEXT NOT NULL PRIMARY KEY, " +
                "player_id TEXT NOT NULL REFERENCES players (id) ON DELETE CASCADE, " +
                "name TEXT NOT NULL, " +
                "created TEXT NOT NULL, " +
                "selected INTEGER NOT NULL DEFAULT 0 CHECK (selected IN (0, 1)))",
            "CREATE INDEX IF NOT EXISTS IX_playlists_player_id ON playlists (player_id)",
            "CREATE UNIQUE INDEX IF NOT EXISTS UX_playlists_selected_per_player " +
                "ON playlists (player_id) WHERE selected = 1",
            "CREATE TABLE IF NOT EXISTS playlist_songs (" +
                "playlist_id TEXT NOT NULL REFERENCES playlists (id) ON DELETE CASCADE, " +
                "song_id TEXT NOT NULL, " +
                "PRIMARY KEY (playlist_id, song_id))",
            "CREATE INDEX IF NOT EXISTS IX_playlist_songs_song_id ON playlist_songs (song_id)",
            "CREATE TABLE IF NOT EXISTS runs (" +
                "id TEXT NOT NULL PRIMARY KEY, " +
                "player_id TEXT NOT NULL REFERENCES players (id) ON DELETE CASCADE, " +
                "song_id TEXT NOT NULL, " +
                "\"when\" TEXT NOT NULL, " +
                "mode TEXT NOT NULL, " +
                "difficulty TEXT NOT NULL, " +
                "score INTEGER NOT NULL, " +
                "points INTEGER NOT NULL, " +
                "modifiers TEXT NOT NULL DEFAULT '{}', " +
                "cleared INTEGER NOT NULL DEFAULT 0 CHECK (cleared IN (0, 1)), " +
                "combo INTEGER NOT NULL)",
            "CREATE INDEX IF NOT EXISTS IX_runs_player_when ON runs (player_id, \"when\")",
            "CREATE INDEX IF NOT EXISTS IX_runs_player_song_score ON runs (player_id, song_id, score DESC, id)"
        };

        private static string _databasePath;

        internal static bool IsAvailable { get; private set; }
        internal static string InitializationFailure { get; private set; }

        internal static void Initialize()
        {
            IsAvailable = false;
            InitializationFailure = null;

            try
            {
                var pluginDirectory = Path.GetDirectoryName(typeof(Plugin).Assembly.Location);
                var gameDirectory = pluginDirectory == null ? null : Directory.GetParent(pluginDirectory);
                if (gameDirectory == null)
                    throw new InvalidOperationException("Could not determine the Beat Saber instance directory.");

                _databasePath = Path.Combine(gameDirectory.FullName, "ProfilesMod.sqlite3");
                using (var connection = OpenConnection())
                {
                    foreach (var statement in SchemaStatements)
                        ExecuteStatement(connection, statement);

                    VerifyWritable(connection);
                }

                if (!File.Exists(_databasePath) || new FileInfo(_databasePath).Length == 0)
                    throw new IOException("SQLite did not create a non-empty profiles database file.");

                IsAvailable = true;
            }
            catch (Exception exception)
            {
                InitializationFailure = exception.Message;
                throw;
            }
        }

        internal static PlayerProfile[] GetPlayers()
        {
            EnsureAvailable();
            using (var connection = OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT p.id, p.name, p.selected, COALESCE(s.run_count, 0), COALESCE(s.max_points, 0) " +
                    "FROM players p LEFT JOIN (" +
                    "SELECT player_id, COUNT(*) AS run_count, MAX(points) AS max_points " +
                    "FROM runs GROUP BY player_id) s ON s.player_id = p.id " +
                    "ORDER BY p.name COLLATE NOCASE, p.id";

                using (var reader = command.ExecuteReader())
                {
                    var players = new System.Collections.Generic.List<PlayerProfile>();
                    while (reader.Read())
                    {
                        players.Add(new PlayerProfile(
                            reader.GetString(0),
                            reader.GetString(1),
                            reader.GetBoolean(2),
                            reader.GetInt64(3),
                            reader.GetInt64(4)));
                    }

                    return players.ToArray();
                }
            }
        }

        internal static PlayerProfile CreatePlayer(string name)
        {
            EnsureAvailable();
            var normalizedName = (name ?? string.Empty).Trim();
            if (normalizedName.Length == 0)
                throw new ArgumentException("Enter a player name.", nameof(name));

            var player = new PlayerProfile(NewId(), normalizedName, false, 0, 0);
            using (var connection = OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "INSERT INTO players (id, name) VALUES (@id, @name)";
                command.Parameters.AddWithValue("@id", player.Id);
                command.Parameters.AddWithValue("@name", player.Name);
                command.ExecuteNonQuery();
            }

            return player;
        }

        internal static void DeletePlayer(string playerId)
        {
            EnsureAvailable();
            using (var connection = OpenConnection())
            using (var transaction = connection.BeginTransaction())
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "SELECT EXISTS (SELECT 1 FROM players WHERE id = @id)";
                command.Parameters.AddWithValue("@id", playerId);
                if (Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) == 0)
                    throw new InvalidOperationException("The selected player no longer exists.");

                command.CommandText =
                    "SELECT EXISTS (SELECT 1 FROM runs WHERE player_id = @id)";
                if (Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) != 0)
                    throw new InvalidOperationException("A player with recorded runs cannot be deleted.");

                command.CommandText = "DELETE FROM players WHERE id = @id";
                command.ExecuteNonQuery();
                command.CommandText = "UPDATE players SET selected = 0";
                command.Parameters.Clear();
                command.ExecuteNonQuery();
                transaction.Commit();
            }
        }

        internal static void SelectPlayer(string playerId)
        {
            EnsureAvailable();
            using (var connection = OpenConnection())
            using (var transaction = connection.BeginTransaction())
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "UPDATE players SET selected = 0";
                command.ExecuteNonQuery();

                command.CommandText = "UPDATE players SET selected = 1 WHERE id = @id";
                command.Parameters.AddWithValue("@id", playerId);
                if (command.ExecuteNonQuery() != 1)
                    throw new InvalidOperationException("The selected player no longer exists.");

                transaction.Commit();
            }
        }

        internal static SongRunSummary GetSongRunSummary(string playerId, string songId)
        {
            EnsureAvailable();
            if (string.IsNullOrEmpty(playerId))
                throw new ArgumentException("Player ID is required.", nameof(playerId));
            if (string.IsNullOrEmpty(songId))
                throw new ArgumentException("Song ID is required.", nameof(songId));

            using (var connection = OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT r.combo, r.points, r.score, r.difficulty, " +
                    "EXISTS (SELECT 1 FROM runs c WHERE c.player_id = @playerId " +
                    "AND c.song_id = @songId AND c.cleared = 1) " +
                    "FROM runs r WHERE r.player_id = @playerId AND r.song_id = @songId " +
                    "ORDER BY r.score DESC, r.id LIMIT 1";
                command.Parameters.AddWithValue("@playerId", playerId);
                command.Parameters.AddWithValue("@songId", songId);
                using (var reader = command.ExecuteReader())
                {
                    if (!reader.Read())
                        return null;

                    return new SongRunSummary(
                        reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2),
                        reader.GetString(3), reader.GetBoolean(4));
                }
            }
        }

        internal static void InsertRun(ProfileRun run)
        {
            EnsureAvailable();
            if (run == null)
                throw new ArgumentNullException(nameof(run));
            using (var connection = OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "INSERT INTO runs (id, player_id, song_id, \"when\", mode, difficulty, " +
                    "score, points, modifiers, cleared, combo) " +
                    "VALUES (@id, @playerId, @songId, @when, @mode, @difficulty, " +
                    "@score, @points, @modifiers, @cleared, @combo)";
                command.Parameters.AddWithValue("@id", run.Id);
                command.Parameters.AddWithValue("@playerId", run.PlayerId);
                command.Parameters.AddWithValue("@songId", run.SongId);
                command.Parameters.AddWithValue("@when", run.When.ToString("O", CultureInfo.InvariantCulture));
                command.Parameters.AddWithValue("@mode", run.Mode);
                command.Parameters.AddWithValue("@difficulty", run.Difficulty);
                command.Parameters.AddWithValue("@score", run.Score);
                command.Parameters.AddWithValue("@points", run.Points);
                command.Parameters.AddWithValue("@modifiers", run.Modifiers);
                command.Parameters.AddWithValue("@cleared", run.Cleared ? 1 : 0);
                command.Parameters.AddWithValue("@combo", run.Combo);
                command.ExecuteNonQuery();
            }
        }

        internal static string NewId()
        {
            var bytes = new byte[16];
            using (var random = RandomNumberGenerator.Create())
                random.GetBytes(bytes);

            var timestamp = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            bytes[0] = (byte)(timestamp >> 40);
            bytes[1] = (byte)(timestamp >> 32);
            bytes[2] = (byte)(timestamp >> 24);
            bytes[3] = (byte)(timestamp >> 16);
            bytes[4] = (byte)(timestamp >> 8);
            bytes[5] = (byte)timestamp;
            bytes[6] = (byte)((bytes[6] & 0x0F) | 0x70);
            bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);

            var hex = BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
            return string.Format(CultureInfo.InvariantCulture, "{0}-{1}-{2}-{3}-{4}",
                hex.Substring(0, 8), hex.Substring(8, 4), hex.Substring(12, 4),
                hex.Substring(16, 4), hex.Substring(20, 12));
        }

        private static void ExecuteStatement(SQLiteConnection connection, string statement)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = statement;
                command.ExecuteNonQuery();
            }
        }

        private static SQLiteConnection OpenConnection()
        {
            if (string.IsNullOrEmpty(_databasePath))
                throw new InvalidOperationException("The profiles database has not been initialized.");

            var connectionString = new SQLiteConnectionStringBuilder
            {
                DataSource = _databasePath,
                Version = 3
            }.ToString();
            var connection = new SQLiteConnection(connectionString);
            try
            {
                connection.Open();
                if (connection.State != ConnectionState.Open)
                    throw new InvalidOperationException("SQLite did not open the profiles database.");

                ExecuteStatement(connection, "PRAGMA foreign_keys = ON");
                return connection;
            }
            catch
            {
                connection.Dispose();
                throw;
            }
        }

        private static void EnsureAvailable()
        {
            if (!IsAvailable)
                throw new InvalidOperationException("The profiles database is unavailable.");
        }

        private static void VerifyWritable(SQLiteConnection connection)
        {
            using (var transaction = connection.BeginTransaction())
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "CREATE TABLE IF NOT EXISTS __ProfilesModWriteProbe (Id INTEGER PRIMARY KEY)";
                command.ExecuteNonQuery();
                command.CommandText = "INSERT INTO __ProfilesModWriteProbe DEFAULT VALUES";
                command.ExecuteNonQuery();
                command.CommandText = "DELETE FROM __ProfilesModWriteProbe";
                command.ExecuteNonQuery();
                command.CommandText = "DROP TABLE __ProfilesModWriteProbe";
                command.ExecuteNonQuery();
                transaction.Commit();
            }
        }
    }

    internal sealed class PlayerProfile
    {
        internal PlayerProfile(string id, string name, bool isSelected, long runCount, long maxPoints)
        {
            Id = id;
            Name = name;
            IsSelected = isSelected;
            RunCount = runCount;
            MaxPoints = maxPoints;
        }

        internal string Id { get; }
        internal string Name { get; }
        internal bool IsSelected { get; }
        internal long RunCount { get; }
        internal long MaxPoints { get; }
        internal bool HasRuns => RunCount > 0;
    }

    internal sealed class SongRunSummary
    {
        internal SongRunSummary(long combo, long points, long score, string difficulty, bool hasClearedRun)
        {
            Combo = combo;
            Points = points;
            Score = score;
            Difficulty = difficulty;
            HasClearedRun = hasClearedRun;
        }

        internal long Combo { get; }
        internal long Points { get; }
        internal long Score { get; }
        internal string Difficulty { get; }
        internal bool HasClearedRun { get; }
    }
}
