using System;
using System.Data;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;

namespace ProfilesMod
{
    internal static class ProfileDatabase
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
                "cleared INTEGER NOT NULL DEFAULT 0 CHECK (cleared IN (0, 1)))",
            "CREATE INDEX IF NOT EXISTS IX_runs_player_when ON runs (player_id, \"when\")"
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
                    "SELECT p.id, p.name, p.selected, EXISTS (" +
                    "SELECT 1 FROM runs r WHERE r.player_id = p.id) " +
                    "FROM players p ORDER BY p.name COLLATE NOCASE, p.id";

                using (var reader = command.ExecuteReader())
                {
                    var players = new System.Collections.Generic.List<PlayerProfile>();
                    while (reader.Read())
                    {
                        players.Add(new PlayerProfile(
                            reader.GetString(0),
                            reader.GetString(1),
                            reader.GetBoolean(2),
                            reader.GetBoolean(3)));
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

            var player = new PlayerProfile(NewId(), normalizedName, false, false);
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
        internal PlayerProfile(string id, string name, bool isSelected, bool hasRuns)
        {
            Id = id;
            Name = name;
            IsSelected = isSelected;
            HasRuns = hasRuns;
        }

        internal string Id { get; }
        internal string Name { get; }
        internal bool IsSelected { get; }
        internal bool HasRuns { get; }
    }
}
