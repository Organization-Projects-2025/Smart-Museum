using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

public class SlideShowManager
{
    private List<ContentSlide> slides;
    private int currentIndex;
    private Timer timer;
    private bool playOnce;

    // Gaze-based adaptive timing
    private int slideElapsedMs = 0;
    private int gazeAwayMs = 0;
    private int gazeOnContentMs = 0;
    private const int GazeAwayThreshold = 3000; // 3 seconds of looking away triggers early advance
    private const int MaxExtensionMs = 5000; // Maximum 5 seconds extension for engaged viewing
    private int currentSlideBaselineDuration = 0;
    private int currentSlideMaxDuration = 0;
    private bool gazeAdaptiveEnabled = true;

    public ContentSlide CurrentSlide
    {
        get
        {
            return (slides != null && slides.Count > 0) ? slides[currentIndex] : null;
        }
    }

    public int CurrentIndex
    {
        get { return currentIndex; }
    }

    public int TotalSlides
    {
        get { return (slides != null) ? slides.Count : 0; }
    }

    // True while slideshow timer is running.
    public bool IsRunning
    {
        get { return timer.Enabled; }
    }

    public bool GazeAdaptiveEnabled
    {
        get { return gazeAdaptiveEnabled; }
        set { gazeAdaptiveEnabled = value; }
    }

    /// <summary>Progress of current slide (0.0 to 1.0)</summary>
    public float SlideProgress
    {
        get
        {
            if (currentSlideMaxDuration <= 0) return 0f;
            return Math.Min(1f, (float)slideElapsedMs / currentSlideMaxDuration);
        }
    }

    /// <summary>Time spent looking away from content in current slide (ms)</summary>
    public int GazeAwayMs
    {
        get { return gazeAwayMs; }
    }

    /// <summary>Time spent looking at content in current slide (ms)</summary>
    public int GazeOnContentMs
    {
        get { return gazeOnContentMs; }
    }

    public event Action<ContentSlide> SlideChanged;
    public event Action SlideShowCompleted;

    public SlideShowManager()
    {
        timer = new Timer();
        timer.Interval = 50; // 50ms tick for smooth gaze tracking
        timer.Tick += OnTimerTick;
    }

    public void StartSlideShow(List<ContentSlide> slides, bool playOnce = false)
    {
        timer.Stop();
        this.playOnce = playOnce;

        if (slides == null || slides.Count == 0)
        {
            this.slides = null;
            return;
        }

        this.slides = slides;
        currentIndex = 0;

        ResetSlideTimers();
        currentSlideBaselineDuration = this.slides[0].DurationMs;
        currentSlideMaxDuration = currentSlideBaselineDuration + MaxExtensionMs;

        timer.Start();

        if (SlideChanged != null) SlideChanged(this.slides[0]);
    }

    // Stop slideshow and clear state.
    public void Stop()
    {
        timer.Stop();
        slides = null;
        currentIndex = 0;
        playOnce = false;
        ResetSlideTimers();
    }

    /// <summary>
    /// Update gaze attention for the current slide. Call this from your animation tick.
    /// </summary>
    /// <param name="gazeValid">Is face detected and gaze data valid?</param>
    /// <param name="gazeOnContent">Is the user looking at the content area (image or text)?</param>
    public void UpdateGazeAttention(bool gazeValid, bool gazeOnContent)
    {
        if (!gazeAdaptiveEnabled || !timer.Enabled) return;

        if (gazeValid && gazeOnContent)
        {
            // User is looking at content
            gazeOnContentMs += 50;
            gazeAwayMs = 0; // Reset away counter
        }
        else
        {
            // User is looking elsewhere on screen OR no face detected
            gazeAwayMs += 50;

            // Advance timing is handled only in OnTimerTick to avoid double-advance.
        }
    }

    private void ResetSlideTimers()
    {
        slideElapsedMs = 0;
        gazeAwayMs = 0;
        gazeOnContentMs = 0;
    }

    private void AdvanceSlide()
    {
        if (slides == null || slides.Count == 0) return;

        if (playOnce && currentIndex >= slides.Count - 1)
        {
            timer.Stop();
            slides = null;
            currentIndex = 0;
            playOnce = false;

            if (SlideShowCompleted != null) SlideShowCompleted();
            return;
        }

        currentIndex = (currentIndex + 1) % slides.Count;
        ResetSlideTimers();
        currentSlideBaselineDuration = slides[currentIndex].DurationMs;
        currentSlideMaxDuration = currentSlideBaselineDuration + MaxExtensionMs;

        if (SlideChanged != null) SlideChanged(slides[currentIndex]);
    }

    private void OnTimerTick(object sender, EventArgs e)
    {
        if (slides == null || slides.Count == 0) return;

        slideElapsedMs += 50;

        // Check if we should advance
        bool shouldAdvance = false;

        if (gazeAdaptiveEnabled)
        {
            // Advance if we've reached max duration (baseline + extension)
            if (slideElapsedMs >= currentSlideMaxDuration)
            {
                shouldAdvance = true;
            }
            // Early skip: halfway through baseline and looked away for GazeAwayThreshold
            else if (slideElapsedMs >= currentSlideBaselineDuration * 0.5f && gazeAwayMs >= GazeAwayThreshold)
            {
                shouldAdvance = true;
            }
            // Or full baseline elapsed with brief look-away
            else if (slideElapsedMs >= currentSlideBaselineDuration && gazeAwayMs > 1000)
            {
                shouldAdvance = true;
            }
        }
        else
        {
            // Non-adaptive mode: just use baseline duration
            if (slideElapsedMs >= currentSlideBaselineDuration)
            {
                shouldAdvance = true;
            }
        }

        if (shouldAdvance)
        {
            AdvanceSlide();
        }
    }
}
