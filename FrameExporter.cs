// Offline export: one full camera orbit rendered frame by frame into an
// offscreen target and written as a numbered PNG sequence. Frames are
// produced one per loop iteration, so the video is smooth no matter how
// heavy the shader is at this resolution.
using System.Numerics;

namespace Kleinian;

internal sealed unsafe class FrameExporter : IDisposable
{
    public const int Width = 1920, Height = 1080;
    public const int FrameCount = 240;          // 8 s at 30 fps

    public string OutputDirectory { get; } = Path.GetFullPath("frames");
    public int Frame { get; private set; }
    public bool Done => Frame >= FrameCount;
    public uint OutputTexture => _output.Texture;

    private readonly RenderTargets _targets = new(Width, Height);
    private readonly Target _output = Target.Create(Width, Height, GL.RGBA8, GL.UNSIGNED_BYTE);
    private readonly byte[] _pixels = new byte[Width * Height * 3];
    private readonly float _startYaw;

    public FrameExporter(float startYaw)
    {
        _startYaw = startYaw;
        Directory.CreateDirectory(OutputDirectory);
    }

    // Renders and saves the next frame of the orbit; writes the ffmpeg note after the last one.
    public void RenderNext(Renderer renderer, in Preset preset, Camera camera)
    {
        Camera orbit = camera with { Yaw = _startYaw + MathF.Tau * Frame / FrameCount };
        renderer.Render(_targets, _output.Framebuffer, Width, Height, preset, orbit.Eye);

        GL.glBindFramebuffer(GL.FRAMEBUFFER, _output.Framebuffer);
        GL.glPixelStorei(GL.PACK_ALIGNMENT, 1);
        fixed (byte* p = _pixels)
            GL.glReadPixels(0, 0, Width, Height, GL.RGB, GL.UNSIGNED_BYTE, p);
        Png.WriteRgb(Path.Combine(OutputDirectory, $"frame_{Frame + 1:0000}.png"), Width, Height, _pixels);

        Frame++;
        if (Done) WriteFfmpegNote();
    }

    private void WriteFfmpegNote() =>
        File.WriteAllText(Path.Combine(OutputDirectory, "make_video.txt"),
            "Run this from inside the frames\\ folder (needs ffmpeg installed):\r\n\r\n" +
            "ffmpeg -framerate 30 -i frame_%04d.png -c:v libx264 -pix_fmt yuv420p -crf 18 kleinian.mp4\r\n\r\n" +
            "For a looping GIF:\r\n" +
            "ffmpeg -framerate 30 -i frame_%04d.png -vf \"scale=720:-1:flags=lanczos\" kleinian.gif\r\n");

    public void Dispose()
    {
        _targets.Dispose();
        _output.Delete();
    }
}
