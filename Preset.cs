// A preset is one point in the estimator's parameter space. Every field is
// numeric except the name, so two presets can be blended (morph mode).
using System.Numerics;

namespace Kleinian;

internal readonly record struct Preset(
    string Name,
    Vector3 CSize,      // box-fold half-extents (the main shape lever)
    float Size,         // inversion radius factor
    int Iters,          // fold/inversion iterations (detail)
    float Bound,        // sphere the structure is carved out of
    float Thickness,    // cross-section: filigree (small) .. solid (big)
    float Rot,          // per-iteration twist in xz (spirals / helices)
    Vector3 JuliaC,     // constant added each iteration; zero = off (organic)
    Vector3 ColShift,   // palette hue/phase shift
    float Bloom,        // bloom intensity
    float Dist)         // camera distance applied on selection
{
    // Copy a row, change the numbers, and you have a new variant; the shader
    // already exposes every field as a uniform. Number keys 1..9 pick by index.
    public static readonly Preset[] All =
    [
        //   name         CSize                     size iters bound  thick   rot    juliaC                   colShift                  bloom  dist
        new("Cross",      new(1.00f, 1.00f, 1.30f), 1f, 14, 1.20f, 0.928f, 0.00f, Vector3.Zero,            new(0.00f, 0.00f, 0.00f),  0.75f, 2.10f),
        new("Snowflake",  new(0.92f, 0.92f, 0.92f), 1f, 16, 1.15f, 0.860f, 0.00f, Vector3.Zero,            new(-0.05f, 0.04f, 0.14f), 0.82f, 1.90f),
        new("Cathedral",  new(1.00f, 1.12f, 1.22f), 1f, 14, 1.70f, 1.000f, 0.00f, Vector3.Zero,            new(0.12f, 0.02f, -0.10f), 0.70f, 2.65f),
        new("Spiral",     new(1.00f, 1.00f, 1.15f), 1f, 13, 1.30f, 0.900f, 0.22f, Vector3.Zero,            new(-0.18f, 0.06f, 0.22f), 0.82f, 2.20f),
        new("Helix",      new(0.95f, 1.00f, 1.25f), 1f, 12, 1.35f, 0.950f, 0.42f, Vector3.Zero,            new(0.33f, 0.00f, 0.00f),  0.88f, 2.30f),
        new("Organic",    new(1.00f, 1.00f, 1.00f), 1f, 10, 1.25f, 0.950f, 0.05f, new(0.00f, 0.14f, -0.16f), new(0.20f, 0.40f, 0.10f),  0.80f, 2.05f),
    ];

    // Linear blend of every parameter (iterations rounded to the nearest int).
    public static Preset Lerp(in Preset a, in Preset b, float t) => new(
        b.Name,
        Vector3.Lerp(a.CSize, b.CSize, t),
        float.Lerp(a.Size, b.Size, t),
        (int)MathF.Round(float.Lerp(a.Iters, b.Iters, t)),
        float.Lerp(a.Bound, b.Bound, t),
        float.Lerp(a.Thickness, b.Thickness, t),
        float.Lerp(a.Rot, b.Rot, t),
        Vector3.Lerp(a.JuliaC, b.JuliaC, t),
        Vector3.Lerp(a.ColShift, b.ColShift, t),
        float.Lerp(a.Bloom, b.Bloom, t),
        float.Lerp(a.Dist, b.Dist, t));
}

// Morph mode: a continuous tour through the preset table. It dwells on a
// variant for `Hold` seconds, then eases to the next one over `Travel`.
internal struct Morph
{
    public const float Hold = 1.1f;    // dwell at each variant, seconds
    public const float Travel = 2.2f;  // time spent gliding to the next

    public bool Active;
    public int From, To;
    public float Phase;                // seconds into the current segment

    // Restart the timeline at `preset`, heading for the one after it.
    public void Anchor(int preset)
    {
        From = preset;
        To = (preset + 1) % Preset.All.Length;
        Phase = 0f;
    }

    // Advances the timeline; returns true when a segment ended and `From` moved on.
    public bool Step(float dt)
    {
        Phase += dt;
        if (Phase < Hold + Travel) return false;
        Phase -= Hold + Travel;
        From = To;
        To = (To + 1) % Preset.All.Length;
        return true;
    }

    // The blend currently driving the shader.
    public readonly Preset Current()
    {
        float travel = Math.Clamp((Phase - Hold) / Travel, 0f, 1f);
        float s = travel * travel * (3f - 2f * travel);   // smoothstep easing
        return Preset.Lerp(Preset.All[From], Preset.All[To], s);
    }
}
