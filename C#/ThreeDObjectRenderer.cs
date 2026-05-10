using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

/// <summary>
/// A 3-D mesh: vertices in object space, faces as vertex-index lists, per-face base colors.
/// All coordinates should live in roughly the −1..1 cube for predictable rendering.
/// </summary>
public class ThreeDMesh
{
    /// <summary>Vertex positions in object space.  Each element is float[3] = {x, y, z}.</summary>
    public float[][] Verts;

    /// <summary>
    /// Face definitions.  Each element is an int[] of vertex indices (3 = triangle, 4 = quad).
    /// </summary>
    public int[][] Faces;

    /// <summary>Per-face base color.  Length must match Faces.Length.</summary>
    public Color[] FaceColors;

    // -----------------------------------------------------------------------
    // Built-in mesh factories
    // -----------------------------------------------------------------------

    /// <summary>Axis-aligned cube centred at origin, side length 2*<paramref name="half"/>.</summary>
    public static ThreeDMesh CreateCube(float half = 0.7f)
    {
        float s = half;
        return new ThreeDMesh
        {
            Verts = new[]
            {
                new float[] {-s,-s,-s}, new float[] { s,-s,-s},
                new float[] { s, s,-s}, new float[] {-s, s,-s},
                new float[] {-s,-s, s}, new float[] { s,-s, s},
                new float[] { s, s, s}, new float[] {-s, s, s},
            },
            Faces = new[]
            {
                new[] {0,1,2,3}, // back
                new[] {4,5,6,7}, // front
                new[] {0,4,7,3}, // left
                new[] {1,5,6,2}, // right
                new[] {3,2,6,7}, // top
                new[] {0,1,5,4}, // bottom
            },
            FaceColors = new[]
            {
                Color.FromArgb(180,120, 80),   // back   – darkest
                Color.FromArgb(220,160, 90),   // front  – brightest
                Color.FromArgb(160,100, 60),   // left
                Color.FromArgb(200,140, 80),   // right
                Color.FromArgb(240,200,120),   // top    – lightest
                Color.FromArgb(140, 90, 50),   // bottom
            }
        };
    }

    /// <summary>
    /// Stylised Nefertiti bust composed of coloured boxes:
    /// shoulders → neck → head → crown-band → crown body → crown cap.
    /// All geometry lives in the −1..1 cube.
    /// </summary>
    public static ThreeDMesh CreateNefertitiSilhouette()
    {
        var verts  = new List<float[]>();
        var faces  = new List<int[]>();
        var colors = new List<Color>();

        // Egyptian palette
        Color gold     = Color.FromArgb(212, 175,  55);
        Color goldDark = Color.FromArgb(155, 120,  30);
        Color skin     = Color.FromArgb(200, 155, 110);
        Color skinDark = Color.FromArgb(150, 105,  65);
        Color blue     = Color.FromArgb( 62, 100, 160);
        Color blueDark = Color.FromArgb( 38,  65, 108);

        // Adds a box as 6 quad faces and appends vertices/faces/colors.
        AddBox(verts, faces, colors,
               -0.65f, 0.65f,  -1.0f, -0.68f,  -0.30f, 0.30f,  // shoulders
               skin, skinDark);
        AddBox(verts, faces, colors,
               -0.20f, 0.20f,  -0.68f, -0.30f,  -0.20f, 0.20f,  // neck
               skin, skinDark);
        AddBox(verts, faces, colors,
               -0.28f, 0.28f,  -0.30f,  0.28f,  -0.28f, 0.28f,  // head
               skin, skinDark);
        AddBox(verts, faces, colors,
               -0.28f, 0.28f,   0.28f,  0.40f,  -0.28f, 0.28f,  // crown blue band
               blue, blueDark);
        AddBox(verts, faces, colors,
               -0.22f, 0.22f,   0.40f,  0.88f,  -0.22f, 0.22f,  // crown cylinder
               gold, goldDark);
        AddBox(verts, faces, colors,
               -0.18f, 0.18f,   0.86f,  1.00f,  -0.18f, 0.18f,  // crown cap
               gold, goldDark);

        return new ThreeDMesh
        {
            Verts      = verts.ToArray(),
            Faces      = faces.ToArray(),
            FaceColors = colors.ToArray(),
        };
    }

    /// <summary>Creates an octahedron — nice for general 3-D demonstration.</summary>
    public static ThreeDMesh CreateOctahedron(float r = 0.8f)
    {
        Color topC  = Color.FromArgb(240, 200, 80);
        Color midC  = Color.FromArgb(200, 140, 60);
        Color botC  = Color.FromArgb(160, 100, 40);

        return new ThreeDMesh
        {
            Verts = new[]
            {
                new float[] { 0, r, 0},   // 0 top
                new float[] { r, 0, 0},   // 1
                new float[] { 0, 0, r},   // 2
                new float[] {-r, 0, 0},   // 3
                new float[] { 0, 0,-r},   // 4
                new float[] { 0,-r, 0},   // 5 bottom
            },
            Faces = new[]
            {
                new[] {0,1,2}, new[] {0,2,3}, new[] {0,3,4}, new[] {0,4,1},
                new[] {5,2,1}, new[] {5,3,2}, new[] {5,4,3}, new[] {5,1,4},
            },
            FaceColors = new[]
            {
                topC, topC, topC, topC,
                botC, botC, botC, botC,
            }
        };
    }

    // -----------------------------------------------------------------------
    // Private geometry helpers
    // -----------------------------------------------------------------------

    private static void AddBox(
        List<float[]> verts, List<int[]> faces, List<Color> colors,
        float x0, float x1, float y0, float y1, float z0, float z1,
        Color topColor, Color sideColor)
    {
        int b = verts.Count;

        verts.Add(new float[] {x0, y0, z0}); // 0 back-bot-left
        verts.Add(new float[] {x1, y0, z0}); // 1 back-bot-right
        verts.Add(new float[] {x1, y1, z0}); // 2 back-top-right
        verts.Add(new float[] {x0, y1, z0}); // 3 back-top-left
        verts.Add(new float[] {x0, y0, z1}); // 4 front-bot-left
        verts.Add(new float[] {x1, y0, z1}); // 5 front-bot-right
        verts.Add(new float[] {x1, y1, z1}); // 6 front-top-right
        verts.Add(new float[] {x0, y1, z1}); // 7 front-top-left

        // front, back, left, right, top, bottom
        faces.Add(new[] {b+4, b+5, b+6, b+7}); colors.Add(topColor);   // front (toward viewer)
        faces.Add(new[] {b+1, b+0, b+3, b+2}); colors.Add(sideColor);  // back
        faces.Add(new[] {b+0, b+4, b+7, b+3}); colors.Add(sideColor);  // left
        faces.Add(new[] {b+5, b+1, b+2, b+6}); colors.Add(sideColor);  // right
        faces.Add(new[] {b+3, b+7, b+6, b+2}); colors.Add(topColor);   // top
        faces.Add(new[] {b+0, b+1, b+5, b+4}); colors.Add(sideColor);  // bottom
    }
}

// ===========================================================================

/// <summary>
/// Stateless software 3-D renderer.  Draws a <see cref="ThreeDMesh"/> using only
/// <see cref="System.Drawing.Graphics"/> (GDI+).  No native or OpenGL dependency.
///
/// Rendering pipeline:
///   1. Rotate vertices around Y then X (controlled by hand pose).
///   2. Perspective-project onto the 2-D canvas.
///   3. Backface-cull faces whose normal points away from the viewer.
///   4. Painter-sort (back-to-front) then fill + edge pass.
///   5. Flat shading: diffuse dot-product with a fixed directional light + ambient.
/// </summary>
public static class ThreeDObjectRenderer
{
    // Fixed directional light — upper-left-front, pointing toward origin.
    private static readonly float[] LightDir = Vec3Norm(new float[] {-0.42f, 0.72f, 0.55f});

    private const float Ambient    = 0.30f;
    private const float ViewDepth  = 4.0f;  // perspective camera distance

    // -----------------------------------------------------------------------
    // Main draw entry point
    // -----------------------------------------------------------------------

    /// <summary>
    /// Render <paramref name="mesh"/> centred inside <paramref name="bounds"/>.
    /// </summary>
    /// <param name="pose">Live hand pose; <c>pose.Valid == false</c> still renders (idle spin).</param>
    /// <param name="idleRotY">Auto-rotation angle (radians) used when no hand is detected.</param>
    /// <param name="accent">Accent / edge highlight color.</param>
    /// <param name="alpha">Overall opacity 0–1.</param>
    public static void Draw(
        Graphics      g,
        Rectangle     bounds,
        ThreeDMesh    mesh,
        HandPose      pose,
        float         idleRotY,
        Color         accent,
        float         alpha = 1f)
    {
        if (mesh == null || mesh.Verts == null || mesh.Faces == null) return;

        // --- Map hand pose to rotation + scale -----------------------------------
        float rotY, rotX, fovScale;

        if (pose.Valid)
        {
            // Hand X (0–1) → rotY in −π..π  (left side → negative, right → positive)
            rotY = ((pose.X - 0.5f) * 2f) * (float)Math.PI * 1.1f;

            // Hand Y (0–1) → rotX in −0.8..0.8  (tilt up / down)
            rotX = ((0.5f - pose.Y) * 2f) * 0.8f;

            // Hand Z (0=close, 1=far) → FOV scale (0.5=very big, 1.4=normal)
            fovScale = 0.55f + (1f - pose.Z) * 0.9f;
        }
        else
        {
            rotY     = idleRotY;
            rotX     = 0.18f;       // gentle downward tilt so bust reads nicely
            fovScale = 1.0f;
        }

        // --- Project all vertices ------------------------------------------------
        int cx = bounds.Left + bounds.Width  / 2;
        int cy = bounds.Top  + bounds.Height / 2;
        float scale = Math.Min(bounds.Width, bounds.Height) * 0.36f * fovScale;

        var world    = new float[mesh.Verts.Length][];
        var proj     = new PointF[mesh.Verts.Length];
        var eyeZ     = new float[mesh.Verts.Length];

        for (int i = 0; i < mesh.Verts.Length; i++)
        {
            world[i] = RotateYX(mesh.Verts[i], rotY, rotX);
            float z   = world[i][2] + ViewDepth;
            proj[i]   = new PointF(
                cx + world[i][0] / z * scale,
                cy - world[i][1] / z * scale);   // y flipped: +y = up
            eyeZ[i]  = z;
        }

        // --- Build visible face list with depth + shade --------------------------
        var drawList = new List<FaceEntry>(mesh.Faces.Length);

        for (int fi = 0; fi < mesh.Faces.Length; fi++)
        {
            int[] face = mesh.Faces[fi];
            if (face == null || face.Length < 3) continue;

            // Average eye-space depth for painter sort.
            float avgZ = 0;
            foreach (int vi in face) avgZ += eyeZ[vi];
            avgZ /= face.Length;

            // Face normal from first three world-space vertices.
            var  normal = Vec3Norm(Vec3Cross(
                Vec3Sub(world[face[1]], world[face[0]]),
                Vec3Sub(world[face[2]], world[face[0]])));

            // Backface cull: normal should have a negative z-component
            // (pointing toward viewer who is at +z).
            if (normal[2] > 0.05f) continue;

            float diffuse = Math.Max(0f, Vec3Dot(normal, LightDir));
            float shade   = Ambient + (1f - Ambient) * diffuse;

            drawList.Add(new FaceEntry { FaceIndex = fi, Depth = avgZ, Shade = shade });
        }

        // Painter sort: farthest (largest Z) first.
        drawList.Sort((a, b) => b.Depth.CompareTo(a.Depth));

        // --- Render faces --------------------------------------------------------
        int ialpha = Clamp255((int)(alpha * 255f));

        foreach (var entry in drawList)
        {
            int[]  face = mesh.Faces[entry.FaceIndex];
            var    pts  = new PointF[face.Length];
            for (int k = 0; k < face.Length; k++) pts[k] = proj[face[k]];

            Color baseC = (mesh.FaceColors != null && entry.FaceIndex < mesh.FaceColors.Length)
                ? mesh.FaceColors[entry.FaceIndex]
                : accent;

            Color fillC = ShadeColor(baseC, entry.Shade, ialpha);

            using (var br = new SolidBrush(fillC))
                g.FillPolygon(br, pts);

            // Thin edge in accent color for depth definition.
            using (var pen = new Pen(Color.FromArgb(ialpha / 5, accent), 0.8f))
                g.DrawPolygon(pen, pts);
        }

        // --- "Grabbed" highlight ring around object ------------------------------
        if (pose.Valid && pose.Fist)
        {
            int r  = (int)(scale * 0.8f);
            int cx2 = cx + (int)((pose.X - 0.5f) * bounds.Width * 0.3f);
            int cy2 = cy - (int)((pose.Y - 0.5f) * bounds.Height * 0.3f);
            using (var p = new Pen(Color.FromArgb(ialpha * 3 / 4, accent), 3f))
            {
                p.DashStyle = DashStyle.Dash;
                g.DrawEllipse(p, cx2 - r, cy2 - r, r * 2, r * 2);
            }
        }
    }

    // -----------------------------------------------------------------------
    // Geometry helpers
    // -----------------------------------------------------------------------

    private struct FaceEntry
    {
        public int   FaceIndex;
        public float Depth;
        public float Shade;
    }

    /// <summary>Rotate v around Y then X.</summary>
    private static float[] RotateYX(float[] v, float ry, float rx)
    {
        float x = v[0], y = v[1], z = v[2];

        // Y-axis rotation
        float x1 =  x * (float)Math.Cos(ry) + z * (float)Math.Sin(ry);
        float z1 = -x * (float)Math.Sin(ry) + z * (float)Math.Cos(ry);

        // X-axis rotation
        float y2 = y * (float)Math.Cos(rx) - z1 * (float)Math.Sin(rx);
        float z2 = y * (float)Math.Sin(rx) + z1 * (float)Math.Cos(rx);

        return new float[] { x1, y2, z2 };
    }

    private static float[] Vec3Sub(float[] a, float[] b) =>
        new float[] {a[0]-b[0], a[1]-b[1], a[2]-b[2]};

    private static float[] Vec3Cross(float[] a, float[] b) =>
        new float[] {
            a[1]*b[2] - a[2]*b[1],
            a[2]*b[0] - a[0]*b[2],
            a[0]*b[1] - a[1]*b[0],
        };

    private static float Vec3Dot(float[] a, float[] b) =>
        a[0]*b[0] + a[1]*b[1] + a[2]*b[2];

    private static float[] Vec3Norm(float[] v)
    {
        float len = (float)Math.Sqrt(v[0]*v[0] + v[1]*v[1] + v[2]*v[2]);
        if (len < 1e-7f) return new float[] {0,1,0};
        return new float[] {v[0]/len, v[1]/len, v[2]/len};
    }

    private static Color ShadeColor(Color c, float shade, int alpha)
    {
        int r = Clamp255((int)(c.R * shade));
        int g = Clamp255((int)(c.G * shade));
        int b = Clamp255((int)(c.B * shade));
        return Color.FromArgb(alpha, r, g, b);
    }

    private static int Clamp255(int v)
    {
        if (v < 0)   return 0;
        if (v > 255) return 255;
        return v;
    }
}
