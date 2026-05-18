using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;


/// <summary>
/// Manages the shared ratings.json file at content/ratings/ratings.json.
/// Structure: { "userId": { "tuioSymbolId": rating, ... }, ... }
/// Upserts per user + TUIO symbol — existing entries are updated.
/// </summary>
public static class RatingsManager
{
    private static readonly object _lock = new object();

    public static string GetFilePath(string workspaceRoot)
    {
        return Path.Combine(workspaceRoot, "C#", "content", "ratings", "ratings.json");
    }

    public static void SaveRating(string workspaceRoot, string userId, int tuioSymbolId, int rating)
    {
        if (string.IsNullOrEmpty(userId) || rating < 1 || rating > 5) return;

        lock (_lock)
        {
            string path = GetFilePath(workspaceRoot);
            string dir = Path.GetDirectoryName(path);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            JObject data;
            if (File.Exists(path))
            {
                string raw = File.ReadAllText(path);
                try { data = JObject.Parse(raw); }
                catch { data = new JObject(); }
            }
            else
            {
                data = new JObject();
            }

            string symKey = tuioSymbolId.ToString();

            if (data[userId] is JObject userObj)
            {
                userObj[symKey] = rating;
            }
            else
            {
                data[userId] = new JObject { [symKey] = rating };
            }

            File.WriteAllText(path, data.ToString(Formatting.Indented));
        }
    }

    public static int GetRating(string workspaceRoot, string userId, int tuioSymbolId)
    {
        lock (_lock)
        {
            string path = GetFilePath(workspaceRoot);
            if (!File.Exists(path)) return 0;

            try
            {
                var data = JObject.Parse(File.ReadAllText(path));
                if (data[userId] is JObject userObj &&
                    userObj[tuioSymbolId.ToString()] is JToken val)
                    return val.ToObject<int>();
            }
            catch { }

            return 0;
        }
    }
}
