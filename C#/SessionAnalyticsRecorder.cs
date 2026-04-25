using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
/// <summary>
/// Records gaze and emotion samples during slideshows for later admin replay.
/// </summary>
public class SessionAnalyticsRecorder
{
    private readonly object sync = new object();
    private string visitId;
    private string faceUserId;
    private string displayName;
    private DateTime visitStartedUtc;
    private readonly List<AnalyticsSegment> segments = new List<AnalyticsSegment>();
    private AnalyticsSegment current;
    private string analyticsRoot;

    public bool HasActiveVisit
    {
        get
        {
            lock (sync) { return !string.IsNullOrEmpty(visitId); }
        }
    }

    public void BeginVisit(string rootDir, string userId, string name)
    {
        lock (sync)
        {
            analyticsRoot = rootDir;
            visitId = Guid.NewGuid().ToString("N");
            faceUserId = userId ?? "";
            displayName = name ?? "";
            visitStartedUtc = DateTime.UtcNow;
            segments.Clear();
            current = null;
        }
    }

    public void NotifySlideShowEnded()
    {
        lock (sync)
        {
            if (string.IsNullOrEmpty(visitId)) return;
            long nowMs = (long)(DateTime.UtcNow - visitStartedUtc).TotalMilliseconds;
            if (current != null)
            {
                current.EndedOffsetMs = nowMs;
                segments.Add(current);
                current = null;
            }
        }
    }

    public void NotifySlideChanged(string storyKey, string storyTitle, int slideIndex, ContentSlide slide)
    {
        lock (sync)
        {
            if (string.IsNullOrEmpty(visitId)) return;

            long nowMs = (long)(DateTime.UtcNow - visitStartedUtc).TotalMilliseconds;
            if (current != null)
            {
                current.EndedOffsetMs = nowMs;
                segments.Add(current);
            }

            string summary = SummarizeSlide(slide);
            current = new AnalyticsSegment
            {
                StoryKey = storyKey ?? "",
                StoryTitle = storyTitle ?? "",
                SlideIndex = slideIndex,
                ContentSummary = summary,
                StartedOffsetMs = nowMs,
                EndedOffsetMs = nowMs,
                Samples = new List<AnalyticsSample>()
            };
        }
    }

    private static string SummarizeSlide(ContentSlide slide)
    {
        if (slide == null) return "";
        return slide.Type + ":" + (slide.Content ?? "");
    }

    public void AddSample(GazeEmotionFrame frame)
    {
        if (frame == null || !frame.Ok) return;
        lock (sync)
        {
            if (current == null) return;
            long tRel = (long)(DateTime.UtcNow - visitStartedUtc).TotalMilliseconds - current.StartedOffsetMs;
            var s = new AnalyticsSample
            {
                TRelMs = Math.Max(0, tRel),
                Gx = frame.Gx,
                Gy = frame.Gy,
                Dominant = frame.Dominant ?? "neutral",
                Emotions = new Dictionary<string, double>(frame.Emotions, StringComparer.OrdinalIgnoreCase)
            };
            if (current.Samples.Count == 0 || tRel - current.Samples[current.Samples.Count - 1].TRelMs >= 90)
                current.Samples.Add(s);
        }
    }

    public void FlushAndSave()
    {
        lock (sync)
        {
            if (string.IsNullOrEmpty(visitId) || string.IsNullOrEmpty(analyticsRoot)) return;

            long nowMs = (long)(DateTime.UtcNow - visitStartedUtc).TotalMilliseconds;
            if (current != null)
            {
                current.EndedOffsetMs = nowMs;
                segments.Add(current);
                current = null;
            }

            try
            {
                Directory.CreateDirectory(analyticsRoot);
                string safeUser = string.Join("_", (faceUserId ?? "unknown").Split(Path.GetInvalidFileNameChars()));
                string path = Path.Combine(analyticsRoot,
                    string.Format(CultureInfo.InvariantCulture,
                        "visit_{0}_{1}_{2:yyyyMMdd_HHmmss}.json",
                        safeUser, visitId.Substring(0, Math.Min(8, visitId.Length)), visitStartedUtc));

                var doc = new JObject
                {
                    ["visitId"] = visitId,
                    ["faceUserId"] = faceUserId,
                    ["displayName"] = displayName,
                    ["startedUtc"] = visitStartedUtc.ToString("o", CultureInfo.InvariantCulture),
                    ["endedUtc"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                    ["segments"] = new JArray(segments.Select(SegmentToJson))
                };
                File.WriteAllText(path, doc.ToString(Formatting.Indented));
            }
            catch
            {
                /* best effort */
            }

            visitId = null;
            segments.Clear();
        }
    }

    private static JObject SegmentToJson(AnalyticsSegment seg)
    {
        var jo = new JObject
        {
            ["storyKey"] = seg.StoryKey,
            ["storyTitle"] = seg.StoryTitle,
            ["slideIndex"] = seg.SlideIndex,
            ["contentSummary"] = seg.ContentSummary,
            ["startedOffsetMs"] = seg.StartedOffsetMs,
            ["endedOffsetMs"] = seg.EndedOffsetMs,
            ["samples"] = new JArray(seg.Samples.Select(SampleToJson))
        };
        return jo;
    }

    private static JObject SampleToJson(AnalyticsSample s)
    {
        var em = new JObject();
        foreach (var kv in s.Emotions.OrderByDescending(x => x.Value))
            em[kv.Key] = kv.Value;
        return new JObject
        {
            ["tRelMs"] = s.TRelMs,
            ["gx"] = s.Gx,
            ["gy"] = s.Gy,
            ["dominant"] = s.Dominant,
            ["emotions"] = em
        };
    }

    public static List<string> ListSessionFiles(string analyticsRoot)
    {
        if (string.IsNullOrEmpty(analyticsRoot) || !Directory.Exists(analyticsRoot))
            return new List<string>();
        return Directory.GetFiles(analyticsRoot, "visit_*.json")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToList();
    }

    public static VisitAnalyticsDocument Load(string path)
    {
        var json = File.ReadAllText(path);
        return JsonConvert.DeserializeObject<VisitAnalyticsDocument>(json);
    }

    /// <summary>Shallow copy of the in-progress segment for live admin preview.</summary>
    public LiveSessionSnapshot GetLiveSnapshot()
    {
        lock (sync)
        {
            if (current == null || string.IsNullOrEmpty(visitId)) return null;
            var snap = new LiveSessionSnapshot
            {
                FaceUserId = faceUserId,
                DisplayName = displayName,
                StoryKey = current.StoryKey,
                StoryTitle = current.StoryTitle,
                SlideIndex = current.SlideIndex,
                ContentSummary = current.ContentSummary
            };
            int n = current.Samples.Count;
            int start = Math.Max(0, n - 500);
            for (int i = start; i < n; i++)
                snap.RecentSamples.Add(CloneSample(current.Samples[i]));
            return snap;
        }
    }

    private static AnalyticsSample CloneSample(AnalyticsSample s)
    {
        return new AnalyticsSample
        {
            TRelMs = s.TRelMs,
            Gx = s.Gx,
            Gy = s.Gy,
            Dominant = s.Dominant,
            Emotions = s.Emotions != null
                ? new Dictionary<string, double>(s.Emotions, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        };
    }
}

public class LiveSessionSnapshot
{
    public string FaceUserId;
    public string DisplayName;
    public string StoryKey;
    public string StoryTitle;
    public int SlideIndex;
    public string ContentSummary;
    public readonly List<AnalyticsSample> RecentSamples = new List<AnalyticsSample>();
}

public class AnalyticsSegment
{
    public string StoryKey;
    public string StoryTitle;
    public int SlideIndex;
    public string ContentSummary;
    public long StartedOffsetMs;
    public long EndedOffsetMs;
    public List<AnalyticsSample> Samples;
}

public class AnalyticsSample
{
    public long TRelMs;
    public double Gx;
    public double Gy;
    public string Dominant;
    public Dictionary<string, double> Emotions;
}

public class VisitAnalyticsDocument
{
    [JsonProperty("visitId")] public string VisitId { get; set; }
    [JsonProperty("faceUserId")] public string FaceUserId { get; set; }
    [JsonProperty("displayName")] public string DisplayName { get; set; }
    [JsonProperty("startedUtc")] public string StartedUtc { get; set; }
    [JsonProperty("endedUtc")] public string EndedUtc { get; set; }
    [JsonProperty("segments")] public List<AnalyticsSegmentDoc> Segments { get; set; }
}

public class AnalyticsSegmentDoc
{
    [JsonProperty("storyKey")] public string StoryKey { get; set; }
    [JsonProperty("storyTitle")] public string StoryTitle { get; set; }
    [JsonProperty("slideIndex")] public int SlideIndex { get; set; }
    [JsonProperty("contentSummary")] public string ContentSummary { get; set; }
    [JsonProperty("startedOffsetMs")] public long StartedOffsetMs { get; set; }
    [JsonProperty("endedOffsetMs")] public long EndedOffsetMs { get; set; }
    [JsonProperty("samples")] public List<AnalyticsSampleDoc> Samples { get; set; }
}

public class AnalyticsSampleDoc
{
    [JsonProperty("tRelMs")] public long TRelMs { get; set; }
    [JsonProperty("gx")] public double Gx { get; set; }
    [JsonProperty("gy")] public double Gy { get; set; }
    [JsonProperty("dominant")] public string Dominant { get; set; }
    [JsonProperty("emotions")] public Dictionary<string, double> Emotions { get; set; }
}
