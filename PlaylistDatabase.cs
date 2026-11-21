using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;

namespace ProfilesMod
{
    internal sealed class PlayerPlaylist
    {
        internal PlayerPlaylist(string id, string name, bool selected, bool containsSong)
        {
            Id = id;
            Name = name;
            Selected = selected;
            ContainsSong = containsSong;
        }

        internal string Id { get; }
        internal string Name { get; }
        internal bool Selected { get; }
        internal bool ContainsSong { get; }
    }

    internal static partial class ProfileDatabase
    {
        internal static PlayerPlaylist[] GetPlaylists(string playerId, string songId)
        {
            EnsureAvailable();
            RequireId(playerId, nameof(playerId));
            using (var connection = OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT p.id, p.name, p.selected, EXISTS " +
                    "(SELECT 1 FROM playlist_songs s WHERE s.playlist_id = p.id AND s.song_id = @songId) " +
                    "FROM playlists p WHERE p.player_id = @playerId ORDER BY p.name COLLATE NOCASE, p.id";
                command.Parameters.AddWithValue("@playerId", playerId);
                command.Parameters.AddWithValue("@songId", (object)songId ?? DBNull.Value);
                using (var reader = command.ExecuteReader())
                {
                    var playlists = new List<PlayerPlaylist>();
                    while (reader.Read())
                        playlists.Add(new PlayerPlaylist(reader.GetString(0), reader.GetString(1),
                            reader.GetBoolean(2), reader.GetBoolean(3)));
                    return playlists.ToArray();
                }
            }
        }

        internal static string CreatePlaylist(string playerId, string name)
        {
            EnsureAvailable();
            RequireId(playerId, nameof(playerId));
            var title = RequirePlaylistName(name);
            var id = NewId();
            using (var connection = OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "INSERT INTO playlists (id, player_id, name, created) " +
                    "VALUES (@id, @playerId, @name, @created)";
                command.Parameters.AddWithValue("@id", id);
                command.Parameters.AddWithValue("@playerId", playerId);
                command.Parameters.AddWithValue("@name", title);
                command.Parameters.AddWithValue("@created", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
                command.ExecuteNonQuery();
            }
            return id;
        }

        internal static void RenamePlaylist(string playerId, string playlistId, string name)
        {
            EnsureAvailable();
            RequireId(playerId, nameof(playerId));
            RequireId(playlistId, nameof(playlistId));
            var title = RequirePlaylistName(name);
            using (var connection = OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "UPDATE playlists SET name = @name WHERE id = @id AND player_id = @playerId";
                command.Parameters.AddWithValue("@name", title);
                command.Parameters.AddWithValue("@id", playlistId);
                command.Parameters.AddWithValue("@playerId", playerId);
                if (command.ExecuteNonQuery() != 1)
                    throw new InvalidOperationException("The playlist no longer exists for this player.");
            }
        }

        internal static void DeletePlaylist(string playerId, string playlistId)
        {
            EnsureAvailable();
            RequireId(playerId, nameof(playerId));
            RequireId(playlistId, nameof(playlistId));
            using (var connection = OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "DELETE FROM playlists WHERE id = @id AND player_id = @playerId";
                command.Parameters.AddWithValue("@id", playlistId);
                command.Parameters.AddWithValue("@playerId", playerId);
                if (command.ExecuteNonQuery() != 1)
                    throw new InvalidOperationException("The playlist no longer exists for this player.");
            }
        }

        internal static bool TogglePlaylistSelection(string playerId, string playlistId)
        {
            EnsureAvailable();
            RequireId(playerId, nameof(playerId));
            RequireId(playlistId, nameof(playlistId));
            using (var connection = OpenConnection())
            using (var transaction = connection.BeginTransaction())
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.Parameters.AddWithValue("@id", playlistId);
                command.Parameters.AddWithValue("@playerId", playerId);
                command.CommandText = "SELECT selected FROM playlists WHERE id = @id AND player_id = @playerId";
                var existing = command.ExecuteScalar();
                if (existing == null)
                    throw new InvalidOperationException("The playlist no longer exists for this player.");
                var select = Convert.ToInt32(existing, CultureInfo.InvariantCulture) == 0;
                command.CommandText = "UPDATE playlists SET selected = 0 WHERE player_id = @playerId AND selected = 1";
                command.ExecuteNonQuery();
                if (select)
                {
                    command.CommandText = "UPDATE playlists SET selected = 1 WHERE id = @id AND player_id = @playerId";
                    command.ExecuteNonQuery();
                }

                transaction.Commit();
                return select;
            }
        }

        internal static void ClearPlaylistSelection(string playerId)
        {
            EnsureAvailable();
            RequireId(playerId, nameof(playerId));
            using (var connection = OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "UPDATE playlists SET selected = 0 WHERE player_id = @playerId AND selected = 1";
                command.Parameters.AddWithValue("@playerId", playerId);
                command.ExecuteNonQuery();
            }
        }

        internal static bool TogglePlaylistSong(string playerId, string playlistId, string songId)
        {
            EnsureAvailable();
            RequireId(playerId, nameof(playerId));
            RequireId(playlistId, nameof(playlistId));
            RequireId(songId, nameof(songId));
            using (var connection = OpenConnection())
            using (var transaction = connection.BeginTransaction())
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.Parameters.AddWithValue("@id", playlistId);
                command.Parameters.AddWithValue("@playerId", playerId);
                command.Parameters.AddWithValue("@songId", songId);
                command.CommandText = "SELECT EXISTS(SELECT 1 FROM playlists WHERE id = @id AND player_id = @playerId)";
                if (Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) == 0)
                    throw new InvalidOperationException("The playlist no longer exists for this player.");
                command.CommandText = "DELETE FROM playlist_songs WHERE playlist_id = @id AND song_id = @songId";
                var removed = command.ExecuteNonQuery() != 0;
                if (!removed)
                {
                    command.CommandText = "INSERT INTO playlist_songs (playlist_id, song_id) VALUES (@id, @songId)";
                    command.ExecuteNonQuery();
                }
                transaction.Commit();
                return !removed;
            }
        }

        internal static string[] GetSelectedPlaylistSongs(string playerId, out bool hasSelection)
        {
            EnsureAvailable();
            RequireId(playerId, nameof(playerId));
            using (var connection = OpenConnection())
            using (var transaction = connection.BeginTransaction())
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.Parameters.AddWithValue("@playerId", playerId);
                command.CommandText = "SELECT id FROM playlists WHERE player_id = @playerId AND selected = 1";
                var selected = command.ExecuteScalar() as string;
                hasSelection = selected != null;
                var songs = new List<string>();
                if (hasSelection)
                {
                    command.Parameters.AddWithValue("@playlistId", selected);
                    command.CommandText = "SELECT song_id FROM playlist_songs WHERE playlist_id = @playlistId";
                    using (var reader = command.ExecuteReader())
                        while (reader.Read())
                            songs.Add(reader.GetString(0));
                }
                transaction.Commit();
                return songs.ToArray();
            }
        }

        private static string RequirePlaylistName(string name)
        {
            var title = (name ?? string.Empty).Trim();
            if (title.Length == 0)
                throw new ArgumentException("Enter a playlist name.", nameof(name));
            return title;
        }

        private static void RequireId(string id, string name)
        {
            if (string.IsNullOrEmpty(id))
                throw new ArgumentException("An identifier is required.", name);
        }
    }
}
