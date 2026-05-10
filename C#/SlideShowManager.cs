using System;
using System.Collections.Generic;
using System.Windows.Forms;

public class SlideShowManager
{
    private List<ContentSlide> slides;
    private int currentIndex;
    private Timer timer;
    private bool playOnce;

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

    public event Action<ContentSlide> SlideChanged;
    public event Action SlideShowCompleted;

    public SlideShowManager()
    {
        timer = new Timer();
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

        timer.Interval = this.slides[0].DurationMs;
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
    }

    private void OnTimerTick(object sender, EventArgs e)
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

        timer.Interval = slides[currentIndex].DurationMs;

        if (SlideChanged != null) SlideChanged(slides[currentIndex]);
    }
}
