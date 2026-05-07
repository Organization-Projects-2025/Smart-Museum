/*
 * UserPreferencesManager.cs
 * Handles persistent storage of user favorites and watched items in CSV format
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public class UserPreferencesManager
{
    private readonly string _csvPath;

    public UserPreferencesManager(string csvPath)
    {
        _csvPath = csvPath;
        EnsureDirectoryExists();
    }

    private void EnsureDirectoryExists()
    {
        string dir = Path.GetDirectoryName(_csvPath);
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);
    }

    /// <summary>
    /// Load user preferences (favorites and watched) from CSV.
    /// Returns null if user not found or file doesn't exist.
    /// </summary>
    public UserPreferences Load(string userId)
    {
        if (!File.Exists(_csvPath))
            return null;

        try
        {
            var lines = File.ReadAllLines(_csvPath);
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                var parts = CsvParse(line);
                if (parts.Count < 3) continue;

                string storedUserId = parts[0];
                if (!string.Equals(storedUserId, userId, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Found the user
                var prefs = new UserPreferences { UserId = userId };

                // Parse favorites (comma-separated, pipe-delimited from main CSV)
                if (parts.Count > 1 && !string.IsNullOrEmpty(parts[1]))
                {
                    prefs.Favorites = parts[1]
                        .Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => s.Trim())
                        .Where(s => !string.IsNullOrEmpty(s))
                        .ToList();
                }

                // Parse watched
                if (parts.Count > 2 && !string.IsNullOrEmpty(parts[2]))
                {
                    prefs.Watched = parts[2]
                        .Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => s.Trim())
                        .Where(s => !string.IsNullOrEmpty(s))
                        .ToList();
                }

                return prefs;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[UserPreferencesManager] Error loading preferences: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// Save user preferences to CSV. Creates or updates the entry.
    /// </summary>
    public void Save(UserPreferences prefs)
    {
        if (prefs == null || string.IsNullOrEmpty(prefs.UserId))
            return;

        try
        {
            var lines = new List<string>();

            // Read existing lines, skip the user we're updating
            if (File.Exists(_csvPath))
            {
                var existingLines = File.ReadAllLines(_csvPath);
                foreach (var line in existingLines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var parts = CsvParse(line);
                    if (parts.Count > 0 && !string.Equals(parts[0], prefs.UserId, StringComparison.OrdinalIgnoreCase))
                        lines.Add(line);
                }
            }

            // Add/update the user's entry
            string favoritesStr = string.Join("|", prefs.Favorites ?? new List<string>());
            string watchedStr = string.Join("|", prefs.Watched ?? new List<string>());
            string newLine = CsvEscape(prefs.UserId) + "," + CsvEscape(favoritesStr) + "," + CsvEscape(watchedStr);
            lines.Add(newLine);

            File.WriteAllLines(_csvPath, lines);
            Console.WriteLine($"[UserPreferencesManager] Saved preferences for {prefs.UserId}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[UserPreferencesManager] Error saving preferences: {ex.Message}");
        }
    }

    private static List<string> CsvParse(string line)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
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

        if (value.Contains(",") || value.Contains("\"") || value.Contains("\n"))
            return "\"" + value.Replace("\"", "\"\"") + "\"";

        return value;
    }
}

public class UserPreferences
{
    public string UserId { get; set; }
    public List<string> Favorites { get; set; } = new List<string>();
    public List<string> Watched { get; set; } = new List<string>();
}
