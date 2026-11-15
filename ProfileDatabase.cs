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

                var databasePath = Path.Combine(gameDirectory.FullName, "ProfilesMod.sqlite3");
                var connectionString = new SQLiteConnectionStringBuilder
                {
                    DataSource = databasePath,
                    Version = 3
                }.ToString();

                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();
                    if (connection.State != ConnectionState.Open)
                        throw new InvalidOperationException("SQLite did not open the profiles database.");

                    ExecuteStatement(connection, "PRAGMA foreign_keys = ON");
                    foreach (var statement in SchemaStatements)
                        ExecuteStatement(connection, statement);

                    VerifyWritable(connection);
                }

                if (!File.Exists(databasePath) || new FileInfo(databasePath).Length == 0)
                    throw new IOException("SQLite did not create a non-empty profiles database file.");

                IsAvailable = true;
            }
            catch (Exception exception)
            {
                InitializationFailure = exception.Message;
                throw;
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
}
