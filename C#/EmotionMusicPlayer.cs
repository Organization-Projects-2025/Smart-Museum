using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

/// <summary>
/// Plays looping mood music (neutral / happy / angry) during slideshows via winmm MCI (MP3).
/// </summary>
public sealed class EmotionMusicPlayer : IDisposable
{
    private const string Alias = "smart_museum_emotion_music";

    private readonly string musicDir;
    private string playingEmotion;

    public EmotionMusicPlayer(string musicDirectory)
    {
        musicDir = musicDirectory ?? "";
    }

    /// <param name="slideshowActive">A content slide is currently on screen.</param>
    /// <param name="gazeValid">Face detected and emotion stream is OK.</param>
    /// <param name="dominantEmotion">Lowercase emotion label from gaze_emotion_service.</param>
    public void Update(bool slideshowActive, bool gazeValid, string dominantEmotion)
    {
        if (!slideshowActive || !gazeValid)
        {
            Stop();
            return;
        }

        string mood = NormalizePlayableEmotion(dominantEmotion);
        if (mood == null)
        {
            Stop();
            return;
        }

        if (string.Equals(playingEmotion, mood, StringComparison.OrdinalIgnoreCase))
            return;

        Play(mood);
    }

    public void Stop()
    {
        if (playingEmotion == null) return;
        Mci("stop " + Alias);
        Mci("close " + Alias);
        playingEmotion = null;
    }

    private void Play(string mood)
    {
        Stop();

        string path = Path.Combine(musicDir, mood + ".mp3");
        if (!File.Exists(path))
            return;

        string openCmd = string.Format("open \"{0}\" type mpegvideo alias {1}", path, Alias);
        if (Mci(openCmd) != 0)
            return;
        if (Mci("play " + Alias + " repeat") != 0)
        {
            Mci("close " + Alias);
            return;
        }

        playingEmotion = mood;
    }

    private static string NormalizePlayableEmotion(string dominant)
    {
        if (string.IsNullOrWhiteSpace(dominant)) return null;
        string e = dominant.Trim().ToLowerInvariant();
        if (e == "neutral" || e == "happy" || e == "angry")
            return e;
        return null;
    }

    private static int Mci(string command)
    {
        var err = new StringBuilder(256);
        int result = mciSendString(command, err, err.Capacity, IntPtr.Zero);
        return result;
    }

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern int mciSendString(string command, StringBuilder returnString, int returnLength, IntPtr hwndCallback);

    public void Dispose()
    {
        Stop();
    }
}
