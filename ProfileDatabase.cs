using System;
using System.Data;
using System.IO;
using System.Data.SQLite;

namespace ProfilesMod
{
    internal static class ProfileDatabase
    {
        internal static bool IsAvailable { get; private set; }

        internal static void Initialize()
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

                using (var command = connection.CreateCommand())
                {
                    command.CommandText =
                        "CREATE TABLE IF NOT EXISTS Profiles (" +
                        "Id INTEGER PRIMARY KEY AUTOINCREMENT, " +
                        "Name TEXT NOT NULL COLLATE NOCASE UNIQUE)";
                    command.ExecuteNonQuery();
                }
            }

            IsAvailable = true;
        }
    }
}
