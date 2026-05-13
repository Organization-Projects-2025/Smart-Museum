/*
 * FavoritesManager.cs
 * Smart Grand Egyptian Museum — HCI Interactive Table
 * 
 * Per-user favorites system with CSV persistence.
 * Supports authenticated users (persistent) and guest users (session-only).
 * 
 * Features:
 * - Add/remove favorites for figures, relationships, and objects
 * - Persist favorites to CSV for authenticated users
 * - Session-only favorites for guest users
 * - Check if items are favorited
 * - Retrieve all favorites for a user
 * - Automatic CSV file management
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

/// <summary>
/// Represents a favorited item with its type and identifier.
/// </summary>
public class FavoriteItem
{
    public FavoriteType Type { get; set; }
    public string Identifier { get; set; }
    public DateTime AddedDate { get; set; }

    public FavoriteItem()
    {
        AddedDate = DateTime.Now;
    }

    public FavoriteItem(FavoriteType type, string identifier)
    {
        Type = type;
        Identifier = identifier;
        AddedDate = DateTime.Now;
    }

    /// <summary>
    /// Creates a serialized string representation for CSV storage.
    /// Format: Type:Identifier:Timestamp
    /// </summary>
    public string Serialize()
    {
        return $"{Type}:{Identifier}:{AddedDate:yyyy-MM-dd HH:mm:ss}";
    }

    /// <summary>
    /// Parses a serialized favorite item string.
    /// Format: Type:Identifier:Timestamp
    /// </summary>
    public static FavoriteItem Deserialize(string serialized)
    {
        if (string.IsNullOrWhiteSpace(serialized))
            return null;

        // Split only on first and last colon to handle colons in identifier
        int firstColon = serialized.IndexOf(':');
        if (firstColon < 0)
            return null;

        int lastColon = serialized.LastIndexOf(':');
        if (lastColon < 0 || lastColon == firstColon)
            return null;

        var item = new FavoriteItem();
        
        // Parse type (before first colon)
        string typeStr = serialized.Substring(0, firstColon);
        if (!Enum.TryParse(typeStr, out FavoriteType type))
            return null;
        item.Type = type;

        // Parse identifier (between first and last colon)
        item.Identifier = serialized.Substring(firstColon + 1, lastColon - firstColon - 1);

        // Parse timestamp (after last colon)
        string timestampStr = serialized.Substring(lastColon + 1);
        if (DateTime.TryParse(timestampStr, out DateTime date))
            item.AddedDate = date;

        return item;
    }

    public override bool Equals(object obj)
    {
        if (obj is FavoriteItem other)
            return Type == other.Type && 
                   string.Equals(Identifier, other.Identifier, StringComparison.OrdinalIgnoreCase);
        return false;
    }

    public override int GetHashCode()
    {
        return (Type.ToString() + Identifier.ToLowerInvariant()).GetHashCode();
    }
}

/// <summary>
/// Types of items that can be favorited.
/// </summary>
public enum FavoriteType
{
    Figure,
    Relationship,
    Object
}

/// <summary>
/// Manages per-user favorites with CSV persistence.
/// Authenticated users have persistent favorites; guest users have session-only favorites.
/// </summary>
public class FavoritesManager
{
    private readonly string _csvPath;
    private readonly Dictionary<string, HashSet<FavoriteItem>> _cache;
    private readonly HashSet<FavoriteItem> _guestFavorites;
    private readonly object _lock = new object();

    /// <summary>
    /// Creates a new FavoritesManager with the specified CSV file path.
    /// </summary>
    /// <param name="csvPath">Path to the favorites CSV file (default: content/auth/user_favorites.csv)</param>
    public FavoritesManager(string csvPath = "content/auth/user_favorites.csv")
    {
        _csvPath = csvPath;
        _cache = new Dictionary<string, HashSet<FavoriteItem>>(StringComparer.OrdinalIgnoreCase);
        _guestFavorites = new HashSet<FavoriteItem>();
        EnsureDirectoryExists();
        LoadAllFromCsv();
    }

    private void EnsureDirectoryExists()
    {
        string dir = Path.GetDirectoryName(_csvPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
    }

    /// <summary>
    /// Loads all favorites from CSV into memory cache.
    /// </summary>
    private void LoadAllFromCsv()
    {
        lock (_lock)
        {
            _cache.Clear();

            if (!File.Exists(_csvPath))
                return;

            try
            {
                var lines = File.ReadAllLines(_csvPath, Encoding.UTF8);
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    var parts = CsvParse(line);
                    if (parts.Count < 2)
                        continue;

                    string userId = parts[0];
                    string favoritesStr = parts[1];

                    if (string.IsNullOrWhiteSpace(userId))
                        continue;

                    var favorites = new HashSet<FavoriteItem>();
                    if (!string.IsNullOrWhiteSpace(favoritesStr))
                    {
                        var items = favoritesStr.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var itemStr in items)
                        {
                            var item = FavoriteItem.Deserialize(itemStr.Trim());
                            if (item != null)
                                favorites.Add(item);
                        }
                    }

                    _cache[userId] = favorites;
                }

                Console.WriteLine($"[FavoritesManager] Loaded favorites for {_cache.Count} users from {_csvPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FavoritesManager] Error loading favorites: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Saves all favorites to CSV file.
    /// </summary>
    private void SaveAllToCsv()
    {
        lock (_lock)
        {
            try
            {
                var lines = new List<string>();

                foreach (var kvp in _cache.OrderBy(x => x.Key))
                {
                    string userId = kvp.Key;
                    var favorites = kvp.Value;

                    var serialized = favorites
                        .OrderBy(f => f.AddedDate)
                        .Select(f => f.Serialize())
                        .ToList();

                    string favoritesStr = string.Join("|", serialized);
                    string line = CsvEscape(userId) + "," + CsvEscape(favoritesStr);
                    lines.Add(line);
                }

                File.WriteAllLines(_csvPath, lines, Encoding.UTF8);
                Console.WriteLine($"[FavoritesManager] Saved favorites for {_cache.Count} users to {_csvPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FavoritesManager] Error saving favorites: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Adds a favorite for the specified user.
    /// For guest users, favorites are stored in memory only.
    /// </summary>
    public bool AddFavorite(VisitorProfile profile, FavoriteType type, string identifier)
    {
        if (profile == null || string.IsNullOrWhiteSpace(identifier))
            return false;

        var item = new FavoriteItem(type, identifier);

        // Guest users: session-only favorites
        if (profile.GuestSession)
        {
            lock (_lock)
            {
                bool added = _guestFavorites.Add(item);
                if (added)
                    Console.WriteLine($"[FavoritesManager] Guest added favorite: {type} - {identifier}");
                return added;
            }
        }

        // Authenticated users: persistent favorites
        lock (_lock)
        {
            if (!_cache.ContainsKey(profile.FaceUserId))
                _cache[profile.FaceUserId] = new HashSet<FavoriteItem>();

            bool added = _cache[profile.FaceUserId].Add(item);
            if (added)
            {
                SaveAllToCsv();
                Console.WriteLine($"[FavoritesManager] User {profile.FaceUserId} added favorite: {type} - {identifier}");
            }
            return added;
        }
    }

    /// <summary>
    /// Removes a favorite for the specified user.
    /// </summary>
    public bool RemoveFavorite(VisitorProfile profile, FavoriteType type, string identifier)
    {
        if (profile == null || string.IsNullOrWhiteSpace(identifier))
            return false;

        var item = new FavoriteItem(type, identifier);

        // Guest users
        if (profile.GuestSession)
        {
            lock (_lock)
            {
                bool removed = _guestFavorites.Remove(item);
                if (removed)
                    Console.WriteLine($"[FavoritesManager] Guest removed favorite: {type} - {identifier}");
                return removed;
            }
        }

        // Authenticated users
        lock (_lock)
        {
            if (!_cache.ContainsKey(profile.FaceUserId))
                return false;

            bool removed = _cache[profile.FaceUserId].Remove(item);
            if (removed)
            {
                SaveAllToCsv();
                Console.WriteLine($"[FavoritesManager] User {profile.FaceUserId} removed favorite: {type} - {identifier}");
            }
            return removed;
        }
    }

    /// <summary>
    /// Toggles a favorite (adds if not present, removes if present).
    /// Returns true if the item is now favorited, false if unfavorited.
    /// </summary>
    public bool ToggleFavorite(VisitorProfile profile, FavoriteType type, string identifier)
    {
        if (IsFavorite(profile, type, identifier))
        {
            RemoveFavorite(profile, type, identifier);
            return false;
        }
        else
        {
            AddFavorite(profile, type, identifier);
            return true;
        }
    }

    /// <summary>
    /// Checks if an item is favorited by the user.
    /// </summary>
    public bool IsFavorite(VisitorProfile profile, FavoriteType type, string identifier)
    {
        if (profile == null || string.IsNullOrWhiteSpace(identifier))
            return false;

        var item = new FavoriteItem(type, identifier);

        lock (_lock)
        {
            if (profile.GuestSession)
                return _guestFavorites.Contains(item);

            if (!_cache.ContainsKey(profile.FaceUserId))
                return false;

            return _cache[profile.FaceUserId].Contains(item);
        }
    }

    /// <summary>
    /// Gets all favorites for the specified user.
    /// </summary>
    public List<FavoriteItem> GetFavorites(VisitorProfile profile)
    {
        if (profile == null)
        {
            Console.WriteLine("[FavoritesManager] GetFavorites called with null profile");
            return new List<FavoriteItem>();
        }

        lock (_lock)
        {
            if (profile.GuestSession)
            {
                Console.WriteLine($"[FavoritesManager] GetFavorites for GUEST - returning {_guestFavorites.Count} items");
                return _guestFavorites.OrderByDescending(f => f.AddedDate).ToList();
            }

            Console.WriteLine($"[FavoritesManager] GetFavorites for user '{profile.FaceUserId}'");
            
            if (!_cache.ContainsKey(profile.FaceUserId))
            {
                Console.WriteLine($"[FavoritesManager] User '{profile.FaceUserId}' has NO favorites (not in cache)");
                Console.WriteLine($"[FavoritesManager] Available users in cache: {string.Join(", ", _cache.Keys)}");
                return new List<FavoriteItem>();
            }

            int count = _cache[profile.FaceUserId].Count;
            Console.WriteLine($"[FavoritesManager] User '{profile.FaceUserId}' has {count} favorites");
            return _cache[profile.FaceUserId].OrderByDescending(f => f.AddedDate).ToList();
        }
    }

    /// <summary>
    /// Gets favorites filtered by type.
    /// </summary>
    public List<FavoriteItem> GetFavoritesByType(VisitorProfile profile, FavoriteType type)
    {
        return GetFavorites(profile)
            .Where(f => f.Type == type)
            .ToList();
    }

    /// <summary>
    /// Gets the count of favorites for a user.
    /// </summary>
    public int GetFavoritesCount(VisitorProfile profile)
    {
        if (profile == null)
            return 0;

        lock (_lock)
        {
            if (profile.GuestSession)
                return _guestFavorites.Count;

            if (!_cache.ContainsKey(profile.FaceUserId))
                return 0;

            return _cache[profile.FaceUserId].Count;
        }
    }

    /// <summary>
    /// Clears all favorites for a user (use with caution).
    /// </summary>
    public void ClearFavorites(VisitorProfile profile)
    {
        if (profile == null)
            return;

        lock (_lock)
        {
            if (profile.GuestSession)
            {
                _guestFavorites.Clear();
                Console.WriteLine("[FavoritesManager] Cleared guest favorites");
                return;
            }

            if (_cache.ContainsKey(profile.FaceUserId))
            {
                _cache[profile.FaceUserId].Clear();
                SaveAllToCsv();
                Console.WriteLine($"[FavoritesManager] Cleared favorites for user {profile.FaceUserId}");
            }
        }
    }

    /// <summary>
    /// Clears guest favorites (called when guest session ends).
    /// </summary>
    public void ClearGuestFavorites()
    {
        lock (_lock)
        {
            _guestFavorites.Clear();
            Console.WriteLine("[FavoritesManager] Cleared guest favorites");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // CSV Parsing Utilities
    // ─────────────────────────────────────────────────────────────────────────

    private static List<string> CsvParse(string line)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        result.Add(current.ToString());
        return result;
    }

    private static string CsvEscape(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "\"\"";

        if (value.Contains(",") || value.Contains("\"") || value.Contains("\n") || value.Contains("|"))
            return "\"" + value.Replace("\"", "\"\"") + "\"";

        return value;
    }
}
