/*
 * FavoritesUI.cs
 * Smart Grand Egyptian Museum — HCI Interactive Table
 * 
 * UI components for displaying and managing favorites.
 * Provides reusable controls for favorite buttons, lists, and panels.
 * 
 * Components:
 * - FavoriteButton: Toggle button with star icon
 * - FavoritesListPanel: Scrollable list of favorites
 * - FavoritesSummaryPanel: Compact summary with counts
 */

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

/// <summary>
/// A toggle button for adding/removing favorites with visual feedback.
/// Shows a filled star when favorited, outline star when not.
/// </summary>
public class FavoriteButton : Button
{
    private bool _isFavorited;
    private Color _favoritedColor = Color.Gold;
    private Color _unfavoritedColor = Color.Gray;
    private Font _starFont;

    public event EventHandler FavoriteToggled;

    public bool IsFavorited
    {
        get { return _isFavorited; }
        set
        {
            _isFavorited = value;
            UpdateAppearance();
        }
    }

    public Color FavoritedColor
    {
        get { return _favoritedColor; }
        set
        {
            _favoritedColor = value;
            UpdateAppearance();
        }
    }

    public Color UnfavoritedColor
    {
        get { return _unfavoritedColor; }
        set
        {
            _unfavoritedColor = value;
            UpdateAppearance();
        }
    }

    public FavoriteButton()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        this.Size = new Size(40, 40);
        this.FlatStyle = FlatStyle.Flat;
        this.FlatAppearance.BorderSize = 0;
        this.BackColor = Color.Transparent;
        this.Cursor = Cursors.Hand;
        this.Click += FavoriteButton_Click;
        _starFont = new Font("Segoe UI", 18f, FontStyle.Regular);
        UpdateAppearance();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_starFont != null)
            {
                _starFont.Dispose();
                _starFont = null;
            }
        }
        base.Dispose(disposing);
    }

    private void FavoriteButton_Click(object sender, EventArgs e)
    {
        IsFavorited = !IsFavorited;
        FavoriteToggled?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateAppearance()
    {
        // Use Unicode star characters
        this.Text = _isFavorited ? "★" : "☆";
        this.ForeColor = _isFavorited ? _favoritedColor : _unfavoritedColor;
        if (_starFont != null)
            this.Font = _starFont;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        
        // Optional: Add hover effect
        if (this.ClientRectangle.Contains(this.PointToClient(Cursor.Position)))
        {
            using (Pen pen = new Pen(_isFavorited ? _favoritedColor : _unfavoritedColor, 2))
            {
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            }
        }
    }
}

/// <summary>
/// A panel that displays a list of favorites with options to view or remove them.
/// </summary>
public class FavoritesListPanel : Panel
{
    private readonly FavoritesManager _manager;
    private VisitorProfile _profile;
    private FlowLayoutPanel _flowPanel;
    private Label _headerLabel;
    private Label _emptyLabel;

    public event EventHandler<FavoriteItemEventArgs> FavoriteClicked;
    public event EventHandler<FavoriteItemEventArgs> FavoriteRemoved;

    public FavoritesListPanel(FavoritesManager manager)
    {
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        this.AutoScroll = true;
        this.BackColor = Color.FromArgb(240, 240, 240);
        this.Padding = new Padding(10);

        // Header
        _headerLabel = new Label
        {
            Text = "My Favorites",
            Font = new Font("Segoe UI", 16f, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(10, 10),
            ForeColor = Color.FromArgb(40, 40, 40)
        };
        this.Controls.Add(_headerLabel);

        // Flow panel for items
        _flowPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Location = new Point(10, 50),
            Width = this.Width - 40
        };
        this.Controls.Add(_flowPanel);

        // Empty state label
        _emptyLabel = new Label
        {
            Text = "No favorites yet.\nExplore the museum and add items to your favorites!",
            Font = new Font("Segoe UI", 12f, FontStyle.Italic),
            ForeColor = Color.Gray,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Size = new Size(this.Width - 40, 100),
            Location = new Point(10, 100),
            Visible = false
        };
        this.Controls.Add(_emptyLabel);

        this.Resize += (s, e) =>
        {
            _flowPanel.Width = this.Width - 40;
            _emptyLabel.Width = this.Width - 40;
        };
    }

    public void LoadFavorites(VisitorProfile profile)
    {
        _profile = profile;
        RefreshList();
    }

    public void RefreshList()
    {
        if (_profile == null)
            return;

        _flowPanel.Controls.Clear();

        var favorites = _manager.GetFavorites(_profile);

        if (favorites.Count == 0)
        {
            _emptyLabel.Visible = true;
            return;
        }

        _emptyLabel.Visible = false;

        // Group by type
        var figures = favorites.Where(f => f.Type == FavoriteType.Figure).ToList();
        var relationships = favorites.Where(f => f.Type == FavoriteType.Relationship).ToList();
        var objects = favorites.Where(f => f.Type == FavoriteType.Object).ToList();

        if (figures.Count > 0)
        {
            AddCategoryHeader("Figures", figures.Count);
            foreach (var item in figures)
                AddFavoriteItem(item);
        }

        if (relationships.Count > 0)
        {
            AddCategoryHeader("Relationships", relationships.Count);
            foreach (var item in relationships)
                AddFavoriteItem(item);
        }

        if (objects.Count > 0)
        {
            AddCategoryHeader("Objects", objects.Count);
            foreach (var item in objects)
                AddFavoriteItem(item);
        }
    }

    private void AddCategoryHeader(string category, int count)
    {
        var header = new Label
        {
            Text = $"{category} ({count})",
            Font = new Font("Segoe UI", 12f, FontStyle.Bold),
            ForeColor = Color.FromArgb(60, 60, 60),
            AutoSize = true,
            Margin = new Padding(0, 10, 0, 5)
        };
        _flowPanel.Controls.Add(header);
    }

    private void AddFavoriteItem(FavoriteItem item)
    {
        var itemPanel = new Panel
        {
            Width = _flowPanel.Width - 20,
            Height = 60,
            BackColor = Color.White,
            Margin = new Padding(0, 2, 0, 2),
            BorderStyle = BorderStyle.FixedSingle
        };

        // Item label
        var label = new Label
        {
            Text = item.Identifier,
            Font = new Font("Segoe UI", 11f),
            Location = new Point(10, 10),
            AutoSize = false,
            Size = new Size(itemPanel.Width - 120, 40),
            TextAlign = ContentAlignment.MiddleLeft,
            Cursor = Cursors.Hand
        };
        label.Click += (s, e) => FavoriteClicked?.Invoke(this, new FavoriteItemEventArgs(item));
        itemPanel.Controls.Add(label);

        // Date label
        var dateLabel = new Label
        {
            Text = item.AddedDate.ToString("MMM dd, yyyy"),
            Font = new Font("Segoe UI", 8f),
            ForeColor = Color.Gray,
            Location = new Point(10, 35),
            AutoSize = true
        };
        itemPanel.Controls.Add(dateLabel);

        // Remove button
        var removeBtn = new Button
        {
            Text = "✕",
            Font = new Font("Segoe UI", 14f, FontStyle.Bold),
            Size = new Size(40, 40),
            Location = new Point(itemPanel.Width - 50, 10),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(220, 53, 69),
            ForeColor = Color.White,
            Cursor = Cursors.Hand
        };
        removeBtn.FlatAppearance.BorderSize = 0;
        removeBtn.Click += (s, e) =>
        {
            _manager.RemoveFavorite(_profile, item.Type, item.Identifier);
            FavoriteRemoved?.Invoke(this, new FavoriteItemEventArgs(item));
            RefreshList();
        };
        itemPanel.Controls.Add(removeBtn);

        _flowPanel.Controls.Add(itemPanel);
    }
}

/// <summary>
/// A compact panel showing favorites summary with counts by type.
/// </summary>
public class FavoritesSummaryPanel : Panel
{
    private readonly FavoritesManager _manager;
    private VisitorProfile _profile;
    private Label _totalLabel;
    private Label _figuresLabel;
    private Label _relationshipsLabel;
    private Label _objectsLabel;

    public FavoritesSummaryPanel(FavoritesManager manager)
    {
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        this.Size = new Size(250, 120);
        this.BackColor = Color.FromArgb(250, 250, 250);
        this.BorderStyle = BorderStyle.FixedSingle;
        this.Padding = new Padding(10);

        var titleLabel = new Label
        {
            Text = "★ Favorites",
            Font = new Font("Segoe UI", 14f, FontStyle.Bold),
            ForeColor = Color.Gold,
            Location = new Point(10, 10),
            AutoSize = true
        };
        this.Controls.Add(titleLabel);

        _totalLabel = new Label
        {
            Text = "Total: 0",
            Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            Location = new Point(10, 40),
            AutoSize = true
        };
        this.Controls.Add(_totalLabel);

        _figuresLabel = CreateCountLabel("Figures: 0", 60);
        _relationshipsLabel = CreateCountLabel("Relationships: 0", 78);
        _objectsLabel = CreateCountLabel("Objects: 0", 96);

        this.Controls.Add(_figuresLabel);
        this.Controls.Add(_relationshipsLabel);
        this.Controls.Add(_objectsLabel);
    }

    private Label CreateCountLabel(string text, int y)
    {
        return new Label
        {
            Text = text,
            Font = new Font("Segoe UI", 9f),
            ForeColor = Color.FromArgb(80, 80, 80),
            Location = new Point(20, y),
            AutoSize = true
        };
    }

    public void LoadProfile(VisitorProfile profile)
    {
        _profile = profile;
        RefreshCounts();
    }

    public void RefreshCounts()
    {
        if (_profile == null)
            return;

        var favorites = _manager.GetFavorites(_profile);
        int figuresCount = favorites.Count(f => f.Type == FavoriteType.Figure);
        int relationshipsCount = favorites.Count(f => f.Type == FavoriteType.Relationship);
        int objectsCount = favorites.Count(f => f.Type == FavoriteType.Object);

        _totalLabel.Text = $"Total: {favorites.Count}";
        _figuresLabel.Text = $"Figures: {figuresCount}";
        _relationshipsLabel.Text = $"Relationships: {relationshipsCount}";
        _objectsLabel.Text = $"Objects: {objectsCount}";
    }
}

/// <summary>
/// Event args for favorite item events.
/// </summary>
public class FavoriteItemEventArgs : EventArgs
{
    public FavoriteItem Item { get; }

    public FavoriteItemEventArgs(FavoriteItem item)
    {
        Item = item;
    }
}

/// <summary>
/// Helper class for creating favorite-related UI elements with consistent styling.
/// </summary>
public static class FavoritesUIHelper
{
    /// <summary>
    /// Creates a favorite button configured for the specified item.
    /// </summary>
    public static FavoriteButton CreateFavoriteButton(
        VisitorProfile profile,
        FavoritesManager manager,
        FavoriteType type,
        string identifier,
        Color? favoritedColor = null,
        Color? unfavoritedColor = null)
    {
        var button = new FavoriteButton
        {
            IsFavorited = manager.IsFavorite(profile, type, identifier)
        };

        if (favoritedColor.HasValue)
            button.FavoritedColor = favoritedColor.Value;
        if (unfavoritedColor.HasValue)
            button.UnfavoritedColor = unfavoritedColor.Value;

        button.FavoriteToggled += (s, e) =>
        {
            manager.ToggleFavorite(profile, type, identifier);
        };

        return button;
    }

    /// <summary>
    /// Creates a favorite button for a figure.
    /// </summary>
    public static FavoriteButton CreateFigureFavoriteButton(
        FigureDef figure,
        VisitorProfile profile,
        FavoritesManager manager)
    {
        if (figure == null)
            return null;

        return CreateFavoriteButton(
            profile,
            manager,
            FavoriteType.Figure,
            figure.GetFavoriteIdentifier(),
            figure.AccentColor,
            Color.Gray);
    }

    /// <summary>
    /// Creates a favorite button for a relationship.
    /// </summary>
    public static FavoriteButton CreateRelationshipFavoriteButton(
        RelationshipDef relationship,
        VisitorProfile profile,
        FavoritesManager manager)
    {
        if (relationship == null)
            return null;

        return CreateFavoriteButton(
            profile,
            manager,
            FavoriteType.Relationship,
            relationship.GetFavoriteIdentifier(),
            Color.Gold,
            Color.Gray);
    }

    /// <summary>
    /// Creates a favorite button for a scene object.
    /// </summary>
    public static FavoriteButton CreateObjectFavoriteButton(
        SceneObjectDef obj,
        FigureDef parentFigure,
        VisitorProfile profile,
        FavoritesManager manager)
    {
        if (obj == null || parentFigure == null)
            return null;

        return CreateFavoriteButton(
            profile,
            manager,
            FavoriteType.Object,
            obj.GetFavoriteIdentifier(parentFigure),
            parentFigure.AccentColor,
            Color.Gray);
    }

    /// <summary>
    /// Shows a toast notification for favorite actions.
    /// </summary>
    public static void ShowFavoriteToast(Form parentForm, string message, bool isFavorited)
    {
        var toast = new Form
        {
            FormBorderStyle = FormBorderStyle.None,
            BackColor = isFavorited ? Color.FromArgb(40, 167, 69) : Color.FromArgb(220, 53, 69),
            StartPosition = FormStartPosition.Manual,
            Size = new Size(300, 60),
            ShowInTaskbar = false,
            TopMost = true
        };

        var label = new Label
        {
            Text = message,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Fill
        };
        toast.Controls.Add(label);

        // Position at bottom center of parent form
        toast.Location = new Point(
            parentForm.Location.X + (parentForm.Width - toast.Width) / 2,
            parentForm.Location.Y + parentForm.Height - toast.Height - 50);

        toast.Show();

        // Auto-close after 2 seconds
        var timer = new Timer { Interval = 2000 };
        timer.Tick += (s, e) =>
        {
            timer.Stop();
            toast.Close();
            toast.Dispose();
        };
        timer.Start();
    }
}
