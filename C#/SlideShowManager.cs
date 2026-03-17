/*
 * SlideShowManager.cs
 * Smart Grand Egyptian Museum — HCI Interactive Table
 *
 * Manages cycling through a list of ContentSlides with per-slide timing.
 * Fires a SlideChanged event on each transition so the main form can
 * redraw with a fade-in effect.
 *
 * Thread safety: This class uses a System.Windows.Forms.Timer, which fires
 * on the UI thread, so all events and state updates happen safely on the
 * UI thread without additional locking.
 */

using System;
using System.Collections.Generic;
using System.Windows.Forms;

public class SlideShowManager
{
    // ─── State ───────────────────────────────────────────────────────────────

    private List<ContentSlide> _slides;
    private int                _currentIndex;
    private Timer              _timer;
    private bool               _playOnce;

    // ─── Properties ──────────────────────────────────────────────────────────

    /// <summary>The slide currently being displayed, or null when stopped.</summary>
    public ContentSlide CurrentSlide
    {
        get
        {
            return (_slides != null && _slides.Count > 0) ? _slides[_currentIndex] : null;
        }
    }

    /// <summary>Zero-based index of the current slide.</summary>
    public int CurrentIndex
    {
        get { return _currentIndex; }
    }

    /// <summary>Total number of slides in the current show, or 0 when stopped.</summary>
    public int TotalSlides
    {
        get { return (_slides != null) ? _slides.Count : 0; }
    }

    /// <summary>True while a slideshow is actively running.</summary>
    public bool IsRunning
    {
        get { return _timer.Enabled; }
    }

    // ─── Events ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Fired on the UI thread whenever the displayed slide changes.
    /// The argument is the new ContentSlide to display.
    /// </summary>
    public event Action<ContentSlide> SlideChanged;
    public event Action SlideShowCompleted;

    // ─── Constructor ─────────────────────────────────────────────────────────

    public SlideShowManager()
    {
        _timer          = new Timer();
        _timer.Tick    += OnTimerTick;
    }

    // ─── Public API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Start (or restart) the slideshow with a new set of slides.
    /// Fires SlideChanged immediately for the first slide.
    /// </summary>
    public void StartSlideShow(List<ContentSlide> slides)
    {
        StartSlideShow(slides, false);
    }

    /// <summary>
    /// Start (or restart) the slideshow with optional play-once behavior.
    /// When playOnce is true, SlideShowCompleted fires after the final slide.
    /// </summary>
    public void StartSlideShow(List<ContentSlide> slides, bool playOnce)
    {
        _timer.Stop();
        _playOnce = playOnce;

        if (slides == null || slides.Count == 0)
        {
            _slides = null;
            return;
        }

        _slides       = slides;
        _currentIndex = 0;

        _timer.Interval = _slides[0].DurationMs;
        _timer.Start();

        if (SlideChanged != null) SlideChanged(_slides[0]);
    }

    /// <summary>Stop the slideshow and clear current state.</summary>
    public void Stop()
    {
        _timer.Stop();
        _slides       = null;
        _currentIndex = 0;
        _playOnce     = false;
    }

    // ─── Private ─────────────────────────────────────────────────────────────

    private void OnTimerTick(object sender, EventArgs e)
    {
        if (_slides == null || _slides.Count == 0) return;

        if (_playOnce && _currentIndex >= _slides.Count - 1)
        {
            _timer.Stop();
            _slides = null;
            _currentIndex = 0;
            _playOnce = false;

            if (SlideShowCompleted != null) SlideShowCompleted();
            return;
        }

        _currentIndex = (_currentIndex + 1) % _slides.Count;

        _timer.Interval = _slides[_currentIndex].DurationMs;

        if (SlideChanged != null) SlideChanged(_slides[_currentIndex]);
    }
}
