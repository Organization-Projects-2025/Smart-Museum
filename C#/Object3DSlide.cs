using System.Drawing;

// ---------------------------------------------------------------------------
// ContentType extension  (ThreeD added here; original Text/Image/Video remain
// in FigureData.cs — this partial-enum comment is just documentation; the
// actual enum lives in FigureData.cs and gets the new value there).
// ---------------------------------------------------------------------------

/// <summary>
/// A <see cref="ContentSlide"/> subtype that drives the software 3-D renderer.
/// Add one to any figure's SoloSlides or StorySlides — the main DrawSlide()
/// dispatcher checks <c>slide is ThreeDObjectSlide</c> and renders it with
/// <see cref="ThreeDObjectRenderer"/>.
/// </summary>
public class ThreeDObjectSlide : ContentSlide
{
    // Mesh identifier — matches the static factory names in ThreeDMesh.
    // Supported values: "cube", "nefertiti", "octahedron"
    public string MeshName { get; set; } = "nefertiti";

    /// <summary>Optional caption shown below the 3-D viewport.</summary>
    public string Caption { get; set; } = "";

    /// <summary>Accent colour used for edges and the grab-ring highlight.</summary>
    public Color AccentColor { get; set; } = Color.FromArgb(212, 175, 55);  // museum gold

    // Lazy-loaded mesh cache so we only build the geometry once.
    private ThreeDMesh _mesh;

    /// <summary>Returns the cached mesh, building it on first access.</summary>
    public ThreeDMesh GetMesh()
    {
        if (_mesh != null) return _mesh;

        switch ((MeshName ?? "").ToLowerInvariant())
        {
            case "cube":
                _mesh = ThreeDMesh.CreateCube();
                break;
            case "octahedron":
                _mesh = ThreeDMesh.CreateOctahedron();
                break;
            default:                        // "nefertiti" or anything unknown
                _mesh = ThreeDMesh.CreateNefertitiSilhouette();
                break;
        }
        return _mesh;
    }

    // Convenience constructor
    public ThreeDObjectSlide(
        string meshName   = "nefertiti",
        string caption    = "",
        int    durationMs = 0)               // 0 = never auto-advance
    {
        MeshName   = meshName;
        Caption    = caption;
        Type       = ContentType.ThreeD;
        DurationMs = durationMs > 0 ? durationMs : int.MaxValue / 2;  // "stay forever"
    }
}
