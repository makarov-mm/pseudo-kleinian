// Pseudo-Kleinian - a 3D pseudo-Kleinian fractal rendered by distance-estimated
// raymarching, on raw Win32 + WGL + OpenGL 3.3 core. Zero external dependencies
// (System.Numerics and System.IO.Compression are part of the BCL).
//
//   The shape is the infinite pseudo-Kleinian set (Knighty's box-fold + spherical
//   inversion), intersected with a sphere so it reads as a carved orb. It is
//   raymarched in a fragment shader to an HDR texture, then put through a small
//   bloom pipeline (bright-pass -> separable Gaussian blur -> additive composite
//   with ACES tonemapping). The hit threshold scales with on-screen pixel size,
//   so detail keeps resolving no matter how far you zoom in.
//
//   Controls:
//     left mouse drag  - orbit             mouse wheel - zoom (all the way inside)
//     M                - morph mode: ease through every variant in a loop
//     Space / Backspace - next / previous variant, 1-6 - pick one
//     E                - offline export: one clean camera orbit to frames/frame_XXXX.png
//                        (frames/make_video.txt has the ffmpeg command)
//     Esc              - quit (cancels a running export first)
//
//   dotnet run -c Release
//
// Author: Mykhailo Makarov (m.m.makarov@gmail.com), no-library style (P/Invoke only).
using System.Diagnostics;
using System.Numerics;

namespace Kleinian;

// Orbit camera around the origin.
internal struct Camera
{
    public float Yaw, Pitch, Distance;

    public readonly Vector3 Eye => new(
        Distance * MathF.Cos(Pitch) * MathF.Sin(Yaw),
        Distance * MathF.Sin(Pitch),
        Distance * MathF.Cos(Pitch) * MathF.Cos(Yaw));
}

internal static class Program
{
    private const float IdleSpinRadiansPerSecond = 0.05f;
    private const float MaxPitch = 1.5f;
    private const float MinDistance = 0.08f, MaxDistance = 10f;

    private static Camera s_camera = new() { Yaw = 0.6f, Pitch = 0.16f, Distance = 2.1f };
    private static int s_preset;
    private static Morph s_morph;
    private static FrameExporter? s_exporter;
    private static bool s_exportRequested, s_cancelExport, s_quit;

    [STAThread]
    private static int Main()
    {
        try
        {
            Run();
            return 0;
        }
        catch (Exception e)
        {
            Win32.MessageBoxW(0, e.Message, "Pseudo-Kleinian", Win32.MB_ICONERROR);
            return 1;
        }
    }

    private static void Run()
    {
        Window.Create("Pseudo-Kleinian", 800, 800);
        Window.KeyDown = OnKey;
        Window.MouseDrag = OnDrag;
        Window.MouseWheel = OnWheel;

        var renderer = new Renderer();
        RenderTargets? live = null;
        SelectPreset(0);

        var clock = Stopwatch.StartNew();
        float lastTime = 0f;

        while (Window.PumpMessages() && !s_quit)
        {
            if (Window.Minimized) continue;

            float time = (float)clock.Elapsed.TotalSeconds;
            float dt = MathF.Min(time - lastTime, 0.1f);   // no jump after a stall (minimized, export)
            lastTime = time;

            if (s_exportRequested)
            {
                s_exportRequested = false;
                s_exporter ??= new FrameExporter(s_camera.Yaw);
            }
            if (s_exporter is { } exporter)
            {
                if (s_cancelExport)
                {
                    s_cancelExport = false;
                    EndExport($"Export cancelled after {exporter.Frame} frames");
                    continue;
                }
                exporter.RenderNext(renderer, CurrentPreset(), s_camera);
                renderer.Blit(exporter.OutputTexture, Window.Width, Window.Height);
                Window.Swap();
                if (exporter.Done)
                    EndExport($"Export complete: {FrameExporter.FrameCount} frames in {exporter.OutputDirectory}  (see make_video.txt)");
                else
                    Window.SetTitle($"Exporting {exporter.Frame}/{FrameExporter.FrameCount}  ->  {exporter.OutputDirectory}");
                continue;
            }

            if (live is null || live.Width != Window.Width || live.Height != Window.Height)
            {
                live?.Dispose();
                live = new RenderTargets(Window.Width, Window.Height);
            }

            if (s_morph.Active)
            {
                if (s_morph.Step(dt))
                {
                    s_preset = s_morph.From;
                    UpdateTitle();
                }
                s_camera.Distance = CurrentPreset().Dist;   // the camera breathes with the morph
            }
            if (!Window.Dragging) s_camera.Yaw += IdleSpinRadiansPerSecond * dt;

            renderer.Render(live, 0, Window.Width, Window.Height, CurrentPreset(), s_camera.Eye);
            Window.Swap();
        }

        s_exporter?.Dispose();
        live?.Dispose();
        Window.Destroy();
    }

    // The preset driving the shader: the selected one, or a blend in morph mode.
    private static Preset CurrentPreset() => s_morph.Active ? s_morph.Current() : Preset.All[s_preset];

    private static void EndExport(string title)
    {
        s_exporter?.Dispose();
        s_exporter = null;
        Window.SetTitle(title);
    }

    private static void SelectPreset(int index)
    {
        s_preset = index;
        s_camera.Distance = Preset.All[index].Dist;   // reframe; the user can still zoom afterwards
        s_morph.Anchor(index);                        // a running morph continues from here
        UpdateTitle();
    }

    private static void ToggleMorph()
    {
        s_morph.Active = !s_morph.Active;
        if (s_morph.Active) s_morph.Anchor(s_preset);
        else s_camera.Distance = Preset.All[s_preset].Dist;   // settle on the current variant
        UpdateTitle();
    }

    private static void UpdateTitle()
    {
        string state = s_morph.Active
            ? $"MORPH  {Preset.All[s_morph.From].Name} -> {Preset.All[s_morph.To].Name}"
            : $"[{s_preset + 1}/{Preset.All.Length}] {Preset.All[s_preset].Name}";
        Window.SetTitle($"Pseudo-Kleinian - {state}   |   " +
                        "M morph · Space next · 1-6 pick · drag orbit · wheel zoom · E export · Esc quit");
    }

    // ---- input (runs inside Window.PumpMessages, on the render thread) -------

    private static void OnKey(int vk)
    {
        switch (vk)
        {
        case Win32.VK_ESCAPE:
            if (s_exporter is not null) s_cancelExport = true;
            else s_quit = true;
            break;
        case Win32.VK_E: s_exportRequested = true; break;
        case Win32.VK_M: ToggleMorph(); break;
        case Win32.VK_SPACE: SelectPreset((s_preset + 1) % Preset.All.Length); break;
        case Win32.VK_BACK: SelectPreset((s_preset + Preset.All.Length - 1) % Preset.All.Length); break;
        default:
            if (vk >= Win32.VK_1 && vk < Win32.VK_1 + Preset.All.Length) SelectPreset(vk - Win32.VK_1);
            break;
        }
    }

    private static void OnDrag(int dx, int dy)
    {
        s_camera.Yaw -= dx * 0.006f;
        s_camera.Pitch = Math.Clamp(s_camera.Pitch + dy * 0.006f, -MaxPitch, MaxPitch);
    }

    private static void OnWheel(int delta)
    {
        s_camera.Distance = Math.Clamp(s_camera.Distance * MathF.Pow(0.86f, delta / 120f), MinDistance, MaxDistance);
    }
}
