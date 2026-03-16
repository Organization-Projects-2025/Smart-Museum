/*
 * TuioDemo.cs  (completely rewritten)
 * Smart Grand Egyptian Museum -- HCI Interactive Table
 *
 * Place one figure  -> solo content slideshow for that character
 * Place two figures -> hint to rotate them face-to-face
 * Two figures facing each other -> relationship content slideshow
 *
 * Keyboard: F1 = full-screen, Escape = exit
 *
 * Requires: FigureData.cs, SlideShowManager.cs, TUIO library, OSC.NET
 * Content: place images in content/figures/ and content/relationships/
 *          relative to the .exe. Missing images show placeholders.
 */

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using TUIO;

// ─────────────────────────────────────────────────────────────────────────────
//  Application state machine
// ─────────────────────────────────────────────────────────────────────────────
public enum AppState
{
	Idle,           // No markers on the table
	SingleFigure,   // Exactly one known marker -- solo content
	PairNotFacing,  // Two known markers but not facing each other -- hint to rotate
	PairFacing      // Two known markers facing each other -- relationship content
}

// ─────────────────────────────────────────────────────────────────────────────
//  Facing detection helper
// ─────────────────────────────────────────────────────────────────────────────
public static class FacingDetector
{
	// Tolerance window: 45 degrees either side of the ideal facing angle.
	private const float ThresholdRad = (float)(Math.PI / 4.0);

	/// <summary>
	/// Returns true when marker A and marker B are oriented so that they
	/// point toward each other (within the tolerance window).
	/// </summary>
	public static bool AreFacing(TuioObject a, FigureDef defA,
								  TuioObject b, FigureDef defB)
	{
		float dx = b.X - a.X;
		float dy = b.Y - a.Y;
		float dirAtoB = (float)Math.Atan2(dy, dx);

		float effectiveA = a.Angle + (defA != null ? defA.FacingAngleOffset : 0f);
		float effectiveB = b.Angle + (defB != null ? defB.FacingAngleOffset : 0f);

		float diffA = NormalizeAngle(effectiveA - dirAtoB);
		float diffB = NormalizeAngle(effectiveB - (dirAtoB + (float)Math.PI));

		return Math.Abs(diffA) < ThresholdRad && Math.Abs(diffB) < ThresholdRad;
	}

	/// <summary>Returns how far A needs to rotate (radians) to face B.</summary>
	public static float FacingDeviation(TuioObject a, FigureDef defA, TuioObject b)
	{
		float dx = b.X - a.X;
		float dy = b.Y - a.Y;
		float dirAtoB = (float)Math.Atan2(dy, dx);
		float effectiveA = a.Angle + (defA != null ? defA.FacingAngleOffset : 0f);
		return NormalizeAngle(effectiveA - dirAtoB);
	}

	private static float NormalizeAngle(float angle)
	{
		while (angle >  (float)Math.PI) angle -= 2f * (float)Math.PI;
		while (angle < -(float)Math.PI) angle += 2f * (float)Math.PI;
		return angle;
	}
}

// ─────────────────────────────────────────────────────────────────────────────
//  Main application form
// ─────────────────────────────────────────────────────────────────────────────
public class TuioDemo : Form, TuioListener
{
	// ── TUIO ─────────────────────────────────────────────────────────────────
	private TuioClient                            _client;
	private readonly Dictionary<long, TuioObject> _objectList = new Dictionary<long, TuioObject>(64);

	// ── State ─────────────────────────────────────────────────────────────────
	private struct StarPoint { public int X, Y, S; }

	private AppState        _state       = AppState.Idle;
	private FigureDef       _activeFig;
	private RelationshipDef _activeRel;
	private TuioObject      _objA, _objB;

	// ── Slideshow ─────────────────────────────────────────────────────────────
	private SlideShowManager _slideShow;
	private ContentSlide     _currentSlide;

	// ── Window ───────────────────────────────────────────────────────────────
	private int  _W, _H;
	private int  _winW = 1280, _winH = 720;
	private int  _winLeft, _winTop;
	private bool _fullscreen;

	// ── Animation ────────────────────────────────────────────────────────────
	private Timer _animTimer;
	private float _idlePhase = 0f;
	private float _fadeAlpha = 1f;
	private bool  _fadingIn  = false;

	// ── Image cache ──────────────────────────────────────────────────────────
	private readonly Dictionary<string, Image> _imgCache =
		new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);

	// ── Fonts ────────────────────────────────────────────────────────────────
	private Font _fontTitle;
	private Font _fontSubtitle;
	private Font _fontBody;
	private Font _fontSmall;
	private Font _fontHint;

	// ── Palette ──────────────────────────────────────────────────────────────
	private static readonly Color CGold      = Color.FromArgb(212, 175,  55);
	private static readonly Color CGoldLight = Color.FromArgb(255, 220, 100);
	private static readonly Color CGoldDim   = Color.FromArgb( 90,  70,  20);
	private static readonly Color CPapyrus   = Color.FromArgb(240, 220, 165);
	private static readonly Color CBg        = Color.FromArgb( 10,   8,  25);
	private static readonly Color CGreen     = Color.FromArgb( 80, 210, 100);

	// ── Star field (pre-computed, frame-stable) ───────────────────────────────
	private readonly StarPoint[] _stars;

	// ─────────────────────────────────────────────────────────────────────────
	//  Constructor
	// ─────────────────────────────────────────────────────────────────────────
	public TuioDemo(int port)
	{
		// Pre-compute deterministic star positions
		var rng = new Random(1337);
		_stars  = new StarPoint[100];
		for (int i = 0; i < _stars.Length; i++)
		{
			StarPoint sp;
			sp.X = rng.Next(1280);
			sp.Y = rng.Next(720);
			sp.S = rng.Next(1, 3);
			_stars[i] = sp;
		}

		// Fonts
		_fontTitle    = new Font("Georgia", 48f, FontStyle.Bold,    GraphicsUnit.Pixel);
		_fontSubtitle = new Font("Georgia", 28f, FontStyle.Italic,  GraphicsUnit.Pixel);
		_fontBody     = new Font("Georgia", 22f, FontStyle.Regular, GraphicsUnit.Pixel);
		_fontSmall    = new Font("Georgia", 15f, FontStyle.Regular, GraphicsUnit.Pixel);
		_fontHint     = new Font("Georgia", 18f, FontStyle.Italic,  GraphicsUnit.Pixel);

		// Window
		_W = _winW; _H = _winH;
		this.ClientSize = new Size(_W, _H);
		this.Text       = "Smart Grand Egyptian Museum";
		this.BackColor  = CBg;
		this.Cursor     = Cursors.Default;
		this.SetStyle(
			ControlStyles.AllPaintingInWmPaint  |
			ControlStyles.UserPaint             |
			ControlStyles.DoubleBuffer          |
			ControlStyles.OptimizedDoubleBuffer, true);

		this.KeyDown += OnKeyDown;
		this.Closing += OnClosing;

		// Slideshow
		_slideShow = new SlideShowManager();
		_slideShow.SlideChanged += slide =>
		{
			_currentSlide = slide;
			_fadeAlpha    = 0f;
			_fadingIn     = true;
			SafeInvalidate();
		};

		// Animation timer (~30 fps)
		_animTimer       = new Timer { Interval = 33 };
		_animTimer.Tick += OnAnimTick;
		_animTimer.Start();

		// TUIO
		_client = new TuioClient(port);
		_client.addTuioListener(this);
		_client.connect();
	}

	// ─────────────────────────────────────────────────────────────────────────
	//  TuioListener
	// ─────────────────────────────────────────────────────────────────────────

	public void addTuioObject(TuioObject o)
	{
		lock (_objectList) _objectList[o.SessionID] = o;
	}

	public void updateTuioObject(TuioObject o)
	{
		lock (_objectList)
			if (_objectList.ContainsKey(o.SessionID)) _objectList[o.SessionID] = o;
	}

	public void removeTuioObject(TuioObject o)
	{
		lock (_objectList) _objectList.Remove(o.SessionID);
	}

	public void addTuioCursor(TuioCursor c)    { }
	public void updateTuioCursor(TuioCursor c) { }
	public void removeTuioCursor(TuioCursor c) { }
	public void addTuioBlob(TuioBlob b)        { }
	public void updateTuioBlob(TuioBlob b)     { }
	public void removeTuioBlob(TuioBlob b)     { }

	/// <summary>Called once per TUIO frame — triggers state evaluation on UI thread.</summary>
	public void refresh(TuioTime frameTime)
	{
		if (IsHandleCreated)
			this.BeginInvoke(new Action(EvaluateState));
	}

	// ─────────────────────────────────────────────────────────────────────────
	//  State machine
	// ─────────────────────────────────────────────────────────────────────────

	private void EvaluateState()
	{
		List<TuioObject> onTable;
		lock (_objectList) onTable = new List<TuioObject>(_objectList.Values);

		// Keep only recognised figures
		onTable = onTable.FindAll(o => MuseumData.Figures.ContainsKey(o.SymbolID));

		if (onTable.Count == 0)
		{
			Transition(AppState.Idle, null, null, null, null);
		}
		else if (onTable.Count == 1)
		{
			Transition(AppState.SingleFigure,
					   MuseumData.Figures[onTable[0].SymbolID], null, null, null);
		}
		else
		{
			TuioObject a   = onTable[0], b = onTable[1];
			FigureDef  defA = MuseumData.Figures[a.SymbolID];
			FigureDef  defB = MuseumData.Figures[b.SymbolID];
			RelationshipDef rel = FindRelationship(a.SymbolID, b.SymbolID);

			if (rel != null && FacingDetector.AreFacing(a, defA, b, defB))
				Transition(AppState.PairFacing,    null, rel, a, b);
			else
				Transition(AppState.PairNotFacing, null, rel, a, b);
		}
	}

	private void Transition(AppState ns, FigureDef fig, RelationshipDef rel,
							 TuioObject a, TuioObject b)
	{
		bool stateChanged  = ns  != _state;
		bool figureChanged = fig != _activeFig;
		bool relChanged    = rel != _activeRel;

		_state     = ns;
		_activeFig = fig;
		_activeRel = rel;
		_objA      = a;
		_objB      = b;

		switch (ns)
		{
			case AppState.Idle:
				if (stateChanged) { _slideShow.Stop(); _currentSlide = null; }
				break;
			case AppState.SingleFigure:
				if (stateChanged || figureChanged)
					_slideShow.StartSlideShow(fig.SoloSlides);
				break;
			case AppState.PairNotFacing:
				if (stateChanged) { _slideShow.Stop(); _currentSlide = null; }
				break;
			case AppState.PairFacing:
				if ((stateChanged || relChanged) && rel != null)
					_slideShow.StartSlideShow(rel.Slides);
				break;
		}
		Invalidate();
	}

	private static RelationshipDef FindRelationship(int idA, int idB)
	{
		foreach (var r in MuseumData.Relationships)
			if ((r.SymbolIdA == idA && r.SymbolIdB == idB) ||
				(r.SymbolIdA == idB && r.SymbolIdB == idA))
				return r;
		return null;
	}

	// ─────────────────────────────────────────────────────────────────────────
	//  Animation timer
	// ─────────────────────────────────────────────────────────────────────────

	private void OnAnimTick(object sender, EventArgs e)
	{
		_idlePhase = (_idlePhase + 1.5f) % 360f;

		if (_fadingIn)
		{
			_fadeAlpha = Math.Min(1f, _fadeAlpha + 0.08f);
			if (_fadeAlpha >= 1f) _fadingIn = false;
		}

		if (_state == AppState.Idle || _state == AppState.PairNotFacing || _fadingIn)
			Invalidate();
	}

	// ─────────────────────────────────────────────────────────────────────────
	//  Rendering — top level
	// ─────────────────────────────────────────────────────────────────────────

	protected override void OnPaintBackground(PaintEventArgs e) { /* suppress */ }

	protected override void OnPaint(PaintEventArgs e)
	{
		Graphics g = e.Graphics;
		g.SmoothingMode     = SmoothingMode.AntiAlias;
		g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
		g.InterpolationMode = InterpolationMode.HighQualityBicubic;

		using (var bg = new SolidBrush(CBg))
			g.FillRectangle(bg, 0, 0, _W, _H);

		DrawStarField(g);

		switch (_state)
		{
			case AppState.Idle:          DrawIdle(g);          break;
			case AppState.SingleFigure:  DrawSingleFigure(g);  break;
			case AppState.PairNotFacing: DrawPairNotFacing(g); break;
			case AppState.PairFacing:    DrawPairFacing(g);    break;
		}
	}

	// ─────────────────────────────────────────────────────────────────────────
	//  Rendering — IDLE
	// ─────────────────────────────────────────────────────────────────────────

	private void DrawIdle(Graphics g)
	{
		DrawAnimatedSunRing(g, _W / 2, _H / 2, _idlePhase);
		DrawEyeOfRa(g, _W / 2, _H / 2);

		DrawCentered(g, "SMART GRAND EGYPTIAN MUSEUM",
			_fontTitle, CGold,
			new RectangleF(0, _H / 2 - 168, _W, 60));

		DrawCentered(g, "Interactive Tangible Table Experience",
			_fontSubtitle, CPapyrus,
			new RectangleF(0, _H / 2 - 98, _W, 38));

		DrawGoldDivider(g, _W / 2 - 260, _H / 2 - 46, 520);

		DrawCentered(g,
			"Place a figure on the table to begin your journey through ancient Egypt",
			_fontHint, Color.FromArgb(210, CPapyrus),
			new RectangleF(60, _H / 2 + 20, _W - 120, 38));

		DrawCentered(g,
			"Place two figures facing each other to discover their historical connection",
			_fontHint, Color.FromArgb(145, CPapyrus),
			new RectangleF(60, _H / 2 + 68, _W - 120, 36));

		DrawOuterBorder(g);
	}

	// ─────────────────────────────────────────────────────────────────────────
	//  Rendering — SINGLE FIGURE
	// ─────────────────────────────────────────────────────────────────────────

	private void DrawSingleFigure(Graphics g)
	{
		if (_activeFig == null) return;
		Color accent = _activeFig.AccentColor;

		int headerH = 95;
		DrawTopGradientBar(g, headerH,
			Color.FromArgb(180, ScaleBrightness(accent, 0.4f)));

		DrawCentered(g, _activeFig.Name.ToUpper(), _fontTitle, accent,
			new RectangleF(0, 8, _W, 58));

		DrawCentered(g, _activeFig.Period + "   \u00B7   " + _activeFig.ShortDescription,
			_fontSmall, Color.FromArgb(200, CPapyrus),
			new RectangleF(0, 68, _W, 24));

		DrawHeaderSep(g, headerH + 6, accent);

		var contentArea = new Rectangle(50, headerH + 22, _W - 100, _H - headerH - 70);
		if (_currentSlide != null)
			DrawSlide(g, _currentSlide, contentArea, accent, _fadeAlpha);

		DrawProgressDots(g, _slideShow.CurrentIndex, _slideShow.TotalSlides,
						 _W / 2, _H - 26, accent);
		DrawOuterBorder(g);
	}

	// ─────────────────────────────────────────────────────────────────────────
	//  Rendering — PAIR NOT FACING
	// ─────────────────────────────────────────────────────────────────────────

	private void DrawPairNotFacing(Graphics g)
	{
		if (_objA == null || _objB == null) return;

		string nameA = GetName(_objA);
		string nameB = GetName(_objB);
		Color  accA  = GetAccent(_objA);
		Color  accB  = GetAccent(_objB);

		DrawCentered(g, "TWO FIGURES DETECTED", _fontTitle, CGold,
			new RectangleF(0, 28, _W, 60));
		DrawCentered(g, nameA + "  &  " + nameB, _fontSubtitle, CPapyrus,
			new RectangleF(0, 96, _W, 38));
		DrawGoldDivider(g, _W / 2 - 260, 144, 520);

		// Facing compasses
		int cy = _H / 2 + 10;
		FigureDef defA = MuseumData.Figures.ContainsKey(_objA.SymbolID)
			? MuseumData.Figures[_objA.SymbolID] : null;
		FigureDef defB = MuseumData.Figures.ContainsKey(_objB.SymbolID)
			? MuseumData.Figures[_objB.SymbolID] : null;

		float devA = FacingDetector.FacingDeviation(_objA, defA, _objB);
		float devB = FacingDetector.FacingDeviation(_objB, defB, _objA);

		DrawFacingCompass(g, _W / 2 - 185, cy,
			_objA.Angle + (defA != null ? defA.FacingAngleOffset : 0f),
			devA, nameA, accA);
		DrawFacingCompass(g, _W / 2 + 185, cy,
			_objB.Angle + (defB != null ? defB.FacingAngleOffset : 0f),
			devB, nameB, accB);

		using (var pen = new Pen(Color.FromArgb(70, CGold), 1)
			   { DashStyle = DashStyle.Dash })
			g.DrawLine(pen, _W / 2 - 110, cy, _W / 2 + 110, cy);

		string hint = _activeRel != null
			? "Rotate the figures so they face each other to uncover their historical connection!"
			: "These two figures share no direct historical connection in our records.";
		int hintAlpha = _activeRel != null ? 210 : 140;
		DrawCentered(g, hint, _fontHint, Color.FromArgb(hintAlpha, CPapyrus),
			new RectangleF(60, _H - 96, _W - 120, 56));

		if (_activeRel != null)
			DrawCentered(g, "[ " + _activeRel.ConnectionTitle + " ]",
				_fontSmall, Color.FromArgb(150, CGold),
				new RectangleF(60, _H - 48, _W - 120, 28));

		DrawOuterBorder(g);
	}

	// ─────────────────────────────────────────────────────────────────────────
	//  Rendering — PAIR FACING
	// ─────────────────────────────────────────────────────────────────────────

	private void DrawPairFacing(Graphics g)
	{
		if (_activeRel == null)
		{
			DrawCentered(g, GetName(_objA) + "  &  " + GetName(_objB),
				_fontTitle, CGold, new RectangleF(0, 50, _W, 60));
			DrawCentered(g,
				"These figures face each other, but no historical connection has been recorded.",
				_fontBody, CPapyrus,
				new RectangleF(60, _H / 2 - 30, _W - 120, 60));
			DrawOuterBorder(g);
			return;
		}

		int headerH = 105;
		DrawTopGradientBar(g, headerH, Color.FromArgb(155, 80, 20, 0));

		// Shimmer sweep
		float shimX = (_idlePhase / 360f) * (_W + 200) - 100;
		using (var sh = new LinearGradientBrush(
			new PointF(shimX - 40, 0), new PointF(shimX + 40, 0),
			Color.Transparent, Color.FromArgb(50, Color.White)))
			g.FillRectangle(sh, 0, 0, _W, headerH);

		DrawCentered(g, "\u2736   CONNECTION DISCOVERED   \u2736",
			_fontTitle, CGoldLight,
			new RectangleF(0, 8, _W, 58));
		DrawCentered(g, _activeRel.ConnectionTitle,
			_fontSubtitle, CPapyrus,
			new RectangleF(0, 68, _W, 36));
		DrawHeaderSep(g, headerH + 6, CGold);

		var contentArea = new Rectangle(50, headerH + 22, _W - 100, _H - headerH - 70);
		if (_currentSlide != null)
			DrawSlide(g, _currentSlide, contentArea, CGold, _fadeAlpha);

		DrawProgressDots(g, _slideShow.CurrentIndex, _slideShow.TotalSlides,
						 _W / 2, _H - 26, CGoldLight);
		DrawOuterBorder(g);
	}

	// ─────────────────────────────────────────────────────────────────────────
	//  Rendering — Slide content
	// ─────────────────────────────────────────────────────────────────────────

	private void DrawSlide(Graphics g, ContentSlide slide, Rectangle area,
						   Color accent, float alpha)
	{
		int a = Clamp255(alpha);
		if (a == 0) return;

		switch (slide.Type)
		{
			case ContentType.Image: DrawImageSlide(g, slide.Content, area, a);          break;
			case ContentType.Text:  DrawTextSlide (g, slide.Content, area, accent, a);  break;
			case ContentType.Video: DrawVideoSlide(g, slide.Content, area, accent, a);  break;
		}
	}

	private void DrawImageSlide(Graphics g, string path, Rectangle area, int alpha)
	{
		Image img = TryLoadImage(path);
		if (img != null)
		{
			Rectangle dest = FitRect(img.Width, img.Height, area);
			using (var ia = new ImageAttributes())
			{
				var cm      = new ColorMatrix();
				cm.Matrix33 = alpha / 255f;
				ia.SetColorMatrix(cm);
				g.DrawImage(img, dest, 0, 0, img.Width, img.Height,
							GraphicsUnit.Pixel, ia);
			}
		}
		else
		{
			using (var pen = new Pen(Color.FromArgb(alpha, CGoldDim), 2))
				g.DrawRectangle(pen, area);
			DrawCentered(g,
				"[ Image: " + Path.GetFileName(path) + " ]",
				_fontHint, Color.FromArgb(alpha, Color.FromArgb(160, CPapyrus)),
				new RectangleF(area.X, area.Y + area.Height / 2f - 18, area.Width, 36));
		}
	}

	private void DrawTextSlide(Graphics g, string text, Rectangle area,
							   Color accent, int alpha)
	{
		int padX  = Math.Min(100, area.Width / 5);
		var panel = new Rectangle(
			area.X + padX,
			area.Y + area.Height / 5,
			area.Width - padX * 2,
			area.Height * 3 / 5);

		using (var bg = new SolidBrush(Color.FromArgb(alpha * 185 / 255, 0, 0, 0)))
			g.FillRectangle(bg, panel);
		using (var pen = new Pen(Color.FromArgb(alpha, accent), 1))
			g.DrawRectangle(pen, panel);

		DrawCornerAccents(g, panel, Color.FromArgb(alpha, accent), 22);

		var sf = new StringFormat
		{
			Alignment     = StringAlignment.Center,
			LineAlignment = StringAlignment.Center,
			Trimming      = StringTrimming.Word
		};
		using (var br = new SolidBrush(Color.FromArgb(alpha, CPapyrus)))
			g.DrawString(text, _fontBody, br,
				new RectangleF(panel.X + 26, panel.Y + 26,
							   panel.Width - 52, panel.Height - 52), sf);
	}

	private void DrawVideoSlide(Graphics g, string path, Rectangle area,
								Color accent, int alpha)
	{
		int padX  = area.Width / 6;
		var panel = new Rectangle(
			area.X + padX, area.Y + area.Height / 5,
			area.Width - padX * 2, area.Height * 3 / 5);

		using (var bg = new SolidBrush(Color.FromArgb(alpha * 155 / 255, 0, 0, 0)))
			g.FillRectangle(bg, panel);
		using (var pen = new Pen(Color.FromArgb(alpha, accent), 2))
			g.DrawRectangle(pen, panel);

		int bx = panel.X + panel.Width / 2;
		int by = panel.Y + panel.Height / 2;
		DrawPlayTriangle(g, bx, by - 20, 36, Color.FromArgb(alpha, accent));
		DrawCentered(g, Path.GetFileNameWithoutExtension(path),
			_fontSmall, Color.FromArgb(alpha, CPapyrus),
			new RectangleF(panel.X, by + 28, panel.Width, 24));
	}

	// ─────────────────────────────────────────────────────────────────────────
	//  Rendering — Decorative helpers
	// ─────────────────────────────────────────────────────────────────────────

	private void DrawAnimatedSunRing(Graphics g, int cx, int cy, float phase)
	{
		float pulse = 1f + 0.04f * (float)Math.Sin(phase * Math.PI / 180.0);
		int r1 = (int)(165 * pulse), r2 = (int)(205 * pulse), r3 = (int)(235 * pulse);

		using (var p = new Pen(Color.FromArgb(48, CGold), 2))
			g.DrawEllipse(p, cx - r1, cy - r1, r1 * 2, r1 * 2);
		using (var p = new Pen(Color.FromArgb(28, CGold), 1))
			g.DrawEllipse(p, cx - r2, cy - r2, r2 * 2, r2 * 2);
		using (var p = new Pen(Color.FromArgb(16, CGold), 1))
			g.DrawEllipse(p, cx - r3, cy - r3, r3 * 2, r3 * 2);

		for (int i = 0; i < 12; i++)
		{
			float a  = (phase + i * 30f) * (float)(Math.PI / 180.0);
			int   x1 = (int)(cx + Math.Cos(a) * (r1 + 6));
			int   y1 = (int)(cy + Math.Sin(a) * (r1 + 6));
			int   x2 = (int)(cx + Math.Cos(a) * r2);
			int   y2 = (int)(cy + Math.Sin(a) * r2);
			using (var p = new Pen(Color.FromArgb(22, CGold), 1))
				g.DrawLine(p, x1, y1, x2, y2);
		}
	}

	private void DrawEyeOfRa(Graphics g, int cx, int cy)
	{
		using (var p = new Pen(Color.FromArgb(50, CGold), 2))
			g.DrawEllipse(p, cx - 26, cy - 14, 52, 28);
		using (var br = new SolidBrush(Color.FromArgb(32, CGold)))
			g.FillEllipse(br, cx - 9, cy - 9, 18, 18);
	}

	private void DrawFacingCompass(Graphics g, int cx, int cy,
								   float angle, float deviation,
								   string name, Color accent)
	{
		const int R = 68;
		float quality  = Math.Max(0f, 1f - Math.Abs(deviation) / ((float)Math.PI / 4f));
		Color ringColor = InterpolateColor(CGold, CGreen, quality);

		using (var p = new Pen(ringColor, 3))
			g.DrawEllipse(p, cx - R, cy - R, R * 2, R * 2);

		// Target arc
		float idealAngle = angle - deviation;
		float startDeg   = (float)(idealAngle * 180f / Math.PI) - 15f;
		using (var p = new Pen(Color.FromArgb(100, CGreen), 3)
			   { StartCap = LineCap.Round, EndCap = LineCap.Round })
			g.DrawArc(p, cx - (R + 10), cy - (R + 10), (R + 10) * 2, (R + 10) * 2,
					  startDeg, 30f);

		// Current direction arrow
		int ax = (int)(cx + Math.Cos(angle) * (R - 10));
		int ay = (int)(cy + Math.Sin(angle) * (R - 10));
		using (var p = new Pen(Color.FromArgb(220, accent), 3) { EndCap = LineCap.ArrowAnchor })
			g.DrawLine(p, cx, cy, ax, ay);

		DrawCentered(g, name, _fontSmall, accent,
			new RectangleF(cx - 135, cy + R + 8, 270, 24));

		string deg = ((int)(deviation * 180 / Math.PI)).ToString("+#;-#;0") + "\u00B0";
		DrawCentered(g, deg, _fontSmall,
			Color.FromArgb(Clamp255(quality * 255f + 60), CGreen),
			new RectangleF(cx - 45, cy + R + 32, 90, 22));
	}

	private void DrawStarField(Graphics g)
	{
		using (var dim    = new SolidBrush(Color.FromArgb(50, 255, 255, 255)))
		using (var bright = new SolidBrush(Color.FromArgb(105, 255, 255, 215)))
		{
			foreach (StarPoint star in _stars)
			{
				int sx = star.X * _W / 1280;
				int sy = star.Y * _H / 720;
				SolidBrush br = (star.S > 1) ? bright : dim;
				g.FillEllipse(br, sx, sy, star.S, star.S);
			}
		}
	}

	private void DrawTopGradientBar(Graphics g, int height, Color top)
	{
		using (var br = new LinearGradientBrush(
			new Rectangle(0, 0, _W, height), top, Color.Transparent,
			LinearGradientMode.Vertical))
			g.FillRectangle(br, 0, 0, _W, height);
	}

	private void DrawHeaderSep(Graphics g, int y, Color color)
	{
		using (var p = new Pen(Color.FromArgb(175, color), 1))
			g.DrawLine(p, 50, y, _W - 50, y);
		DrawDiamond(g, _W / 2, y, 5, color);
	}

	private void DrawGoldDivider(Graphics g, int x, int y, int length)
	{
		using (var p = new Pen(CGold, 1))
			g.DrawLine(p, x, y, x + length, y);
		DrawDiamond(g, x + length / 2, y, 5, CGold);
	}

	private void DrawDiamond(Graphics g, int cx, int cy, int r, Color color)
	{
		Point[] pts = {
			new Point(cx,     cy - r),
			new Point(cx + r, cy),
			new Point(cx,     cy + r),
			new Point(cx - r, cy),
		};
		using (var br = new SolidBrush(color)) g.FillPolygon(br, pts);
	}

	private void DrawOuterBorder(Graphics g)
	{
		int m = 16;
		using (var p = new Pen(CGoldDim, 1))
		{
			g.DrawRectangle(p, m, m, _W - m * 2, _H - m * 2);
			g.DrawRectangle(p, m + 5, m + 5, _W - m * 2 - 10, _H - m * 2 - 10);
		}
		DrawCornerAccents(g, new Rectangle(m, m, _W - m * 2, _H - m * 2), CGoldDim, 28);
	}

	private void DrawCornerAccents(Graphics g, Rectangle r, Color color, int size)
	{
		using (var p = new Pen(color, 2))
		{
			g.DrawLine(p, r.Left,           r.Top + size, r.Left,  r.Top);
			g.DrawLine(p, r.Left,           r.Top,        r.Left  + size, r.Top);
			g.DrawLine(p, r.Right - size,   r.Top,        r.Right, r.Top);
			g.DrawLine(p, r.Right,          r.Top,        r.Right, r.Top + size);
			g.DrawLine(p, r.Left,           r.Bottom - size, r.Left,  r.Bottom);
			g.DrawLine(p, r.Left,           r.Bottom,     r.Left  + size, r.Bottom);
			g.DrawLine(p, r.Right - size,   r.Bottom,     r.Right, r.Bottom);
			g.DrawLine(p, r.Right,          r.Bottom - size, r.Right, r.Bottom);
		}
	}

	private void DrawProgressDots(Graphics g, int current, int total,
								  int cx, int cy, Color color)
	{
		if (total <= 0) return;
		const int dotR = 5, spacing = 18;
		int startX = cx - (total - 1) * spacing / 2;
		for (int i = 0; i < total; i++)
		{
			using (var br = new SolidBrush(i == current
				? color : Color.FromArgb(55, color)))
				g.FillEllipse(br, startX + i * spacing - dotR,
							  cy - dotR, dotR * 2, dotR * 2);
		}
	}

	private void DrawPlayTriangle(Graphics g, int cx, int cy, int r, Color color)
	{
		Point[] pts = {
			new Point(cx - r / 2, cy - r),
			new Point(cx + r,     cy),
			new Point(cx - r / 2, cy + r),
		};
		using (var p = new Pen(color, 3)) g.DrawPolygon(p, pts);
	}

	// ─────────────────────────────────────────────────────────────────────────
	//  Image loading with cache
	// ─────────────────────────────────────────────────────────────────────────

	private Image TryLoadImage(string relativePath)
	{
		Image cached;
		if (_imgCache.TryGetValue(relativePath, out cached)) return cached;

		string full = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relativePath);
		Image  img  = null;
		if (File.Exists(full))
		{
			try { img = Image.FromFile(full); }
			catch { }
		}
		_imgCache[relativePath] = img;
		return img;
	}

	// ─────────────────────────────────────────────────────────────────────────
	//  Utility helpers
	// ─────────────────────────────────────────────────────────────────────────

	private void DrawCentered(Graphics g, string text, Font font, Color color,
							  RectangleF bounds)
	{
		if (string.IsNullOrEmpty(text)) return;
		var sf = new StringFormat
		{
			Alignment     = StringAlignment.Center,
			LineAlignment = StringAlignment.Center,
			Trimming      = StringTrimming.EllipsisCharacter
		};
		using (var br = new SolidBrush(color))
			g.DrawString(text, font, br, bounds, sf);
	}

	private static Rectangle FitRect(int srcW, int srcH, Rectangle area)
	{
		float a = (float)srcW / srcH, aa = (float)area.Width / area.Height;
		int w, h;
		if (a > aa) { w = area.Width;  h = (int)(area.Width  / a); }
		else        { h = area.Height; w = (int)(area.Height * a); }
		return new Rectangle(area.X + (area.Width  - w) / 2,
							 area.Y + (area.Height - h) / 2, w, h);
	}

	private static Color ScaleBrightness(Color c, float s)
	{
		return Color.FromArgb((int)(c.R * s), (int)(c.G * s), (int)(c.B * s));
	}

	private static Color InterpolateColor(Color a, Color b, float t)
	{
		return Color.FromArgb(
			(int)(a.R + (b.R - a.R) * t),
			(int)(a.G + (b.G - a.G) * t),
			(int)(a.B + (b.B - a.B) * t));
	}

	private static int Clamp255(float v)
	{
		if (v < 0)   return 0;
		if (v > 255) return 255;
		return (int)v;
	}

	private string GetName(TuioObject o)
	{
		if (o != null && MuseumData.Figures.ContainsKey(o.SymbolID))
			return MuseumData.Figures[o.SymbolID].Name;
		return "Unknown";
	}

	private Color GetAccent(TuioObject o)
	{
		if (o != null && MuseumData.Figures.ContainsKey(o.SymbolID))
			return MuseumData.Figures[o.SymbolID].AccentColor;
		return CGold;
	}

	private void SafeInvalidate()
	{
		if (IsHandleCreated) this.BeginInvoke(new Action(this.Invalidate));
	}

	// ─────────────────────────────────────────────────────────────────────────
	//  Resize
	// ─────────────────────────────────────────────────────────────────────────

	protected override void OnResize(EventArgs e)
	{
		base.OnResize(e);
		if (this.ClientSize.Width > 0 && this.ClientSize.Height > 0)
		{
			_W = this.ClientSize.Width;
			_H = this.ClientSize.Height;
		}
	}

	// ─────────────────────────────────────────────────────────────────────────
	//  Keyboard & Form events
	// ─────────────────────────────────────────────────────────────────────────

	private void OnKeyDown(object sender, KeyEventArgs e)
	{
		if (e.KeyCode == Keys.F1)
		{
			if (!_fullscreen)
			{
				_winW = _W; _winH = _H;
				_winLeft = this.Left; _winTop = this.Top;
				_W = Screen.PrimaryScreen.Bounds.Width;
				_H = Screen.PrimaryScreen.Bounds.Height;
				this.FormBorderStyle = FormBorderStyle.None;
				this.Left = 0; this.Top = 0;
				this.Width = _W; this.Height = _H;
				_fullscreen = true;
			}
			else
			{
				_W = _winW; _H = _winH;
				this.FormBorderStyle = FormBorderStyle.Sizable;
				this.Left = _winLeft; this.Top = _winTop;
				this.Width = _W + 16; this.Height = _H + 39;
				_fullscreen = false;
			}
		}
		else if (e.KeyCode == Keys.Escape || e.KeyCode == Keys.Q)
		{
			this.Close();
		}
	}

	private void OnClosing(object sender, CancelEventArgs e)
	{
		_animTimer.Stop();
		_slideShow.Stop();
		if (_client != null)
		{
			_client.removeTuioListener(this);
			_client.disconnect();
		}
		Environment.Exit(0);
	}

	// ─────────────────────────────────────────────────────────────────────────
	//  Entry point
	// ─────────────────────────────────────────────────────────────────────────

	[STAThread]
	static void Main(string[] args)
	{
		int port = 3333;
		int p;
		if (args.Length >= 1 && int.TryParse(args[0], out p)) port = p;
		Application.EnableVisualStyles();
		Application.SetCompatibleTextRenderingDefault(false);
		Application.Run(new TuioDemo(port));
	}
}
