// The frame: raymarch into an HDR scene texture, bright-pass and blur at
// half resolution, then composite with bloom and tonemapping into the
// destination (the window, or an export texture).
using System.Numerics;

namespace Kleinian;

// A texture with a framebuffer rendering into it.
internal readonly unsafe record struct Target(uint Texture, uint Framebuffer, int Width, int Height)
{
    public static Target Create(int width, int height, uint internalFormat, uint type)
    {
        uint texture = GL.CreateTexture(width, height, internalFormat, type);
        return new Target(texture, GL.CreateFramebuffer(texture), width, height);
    }

    public void Delete()
    {
        GL.DeleteFramebuffer(Framebuffer);
        GL.DeleteTexture(Texture);
    }
}

// The textures one frame passes through: the HDR scene at full size and the
// two half-size bloom buffers. Recreated when the size changes.
internal sealed unsafe class RenderTargets : IDisposable
{
    public int Width { get; }
    public int Height { get; }
    public Target Scene { get; }
    public Target BloomA { get; }   // bright-pass, then the blurred result
    public Target BloomB { get; }   // ping-pong partner

    public RenderTargets(int width, int height)
    {
        Width = width;
        Height = height;
        int halfW = Math.Max(1, width / 2), halfH = Math.Max(1, height / 2);
        Scene = Target.Create(width, height, GL.RGBA16F, GL.FLOAT);
        BloomA = Target.Create(halfW, halfH, GL.RGBA16F, GL.FLOAT);
        BloomB = Target.Create(halfW, halfH, GL.RGBA16F, GL.FLOAT);
    }

    public void Dispose()
    {
        Scene.Delete();
        BloomA.Delete();
        BloomB.Delete();
    }
}

internal sealed unsafe class Renderer
{
    private const int BlurPasses = 2;   // horizontal + vertical, this many times

    private readonly uint _fractal, _bright, _blur, _composite, _copy;

    // uniform locations, looked up once
    private readonly int _uRes, _uCamPos, _uCamTarget, _uCSize, _uSize, _uIters, _uBound;
    private readonly int _uThickness, _uRot, _uJuliaC, _uColShift;
    private readonly int _brightSize, _blurSize, _blurDir, _compositeSize, _compositeBloom, _copySize;

    public Renderer()
    {
        _fractal = GL.BuildProgram(Shaders.QuadVertex, Shaders.FractalFragment);
        _bright = GL.BuildProgram(Shaders.QuadVertex, Shaders.BrightFragment);
        _blur = GL.BuildProgram(Shaders.QuadVertex, Shaders.BlurFragment);
        _composite = GL.BuildProgram(Shaders.QuadVertex, Shaders.CompositeFragment);
        _copy = GL.BuildProgram(Shaders.QuadVertex, Shaders.CopyFragment);

        _uRes = GL.Uniform(_fractal, "uRes"u8);
        _uCamPos = GL.Uniform(_fractal, "uCamPos"u8);
        _uCamTarget = GL.Uniform(_fractal, "uCamTarget"u8);
        _uCSize = GL.Uniform(_fractal, "uCSize"u8);
        _uSize = GL.Uniform(_fractal, "uSize"u8);
        _uIters = GL.Uniform(_fractal, "uIters"u8);
        _uBound = GL.Uniform(_fractal, "uBound"u8);
        _uThickness = GL.Uniform(_fractal, "uThickness"u8);
        _uRot = GL.Uniform(_fractal, "uRot"u8);
        _uJuliaC = GL.Uniform(_fractal, "uJuliaC"u8);
        _uColShift = GL.Uniform(_fractal, "uColShift"u8);
        _brightSize = GL.Uniform(_bright, "uTexSize"u8);
        _blurSize = GL.Uniform(_blur, "uTexSize"u8);
        _blurDir = GL.Uniform(_blur, "uDir"u8);
        _compositeSize = GL.Uniform(_composite, "uTexSize"u8);
        _compositeBloom = GL.Uniform(_composite, "uBloomI"u8);
        _copySize = GL.Uniform(_copy, "uTexSize"u8);

        // the samplers never move: unit 0 is the input, unit 1 the bloom
        GL.glUseProgram(_bright);
        GL.glUniform1i(GL.Uniform(_bright, "uScene"u8), 0);
        GL.glUseProgram(_blur);
        GL.glUniform1i(GL.Uniform(_blur, "uTex"u8), 0);
        GL.glUseProgram(_composite);
        GL.glUniform1i(GL.Uniform(_composite, "uScene"u8), 0);
        GL.glUniform1i(GL.Uniform(_composite, "uBloom"u8), 1);
        GL.glUseProgram(_copy);
        GL.glUniform1i(GL.Uniform(_copy, "uTex"u8), 0);

        GL.glBindVertexArray(GL.CreateQuad());   // the only geometry; stays bound
    }

    // Renders one frame of `preset` seen from `eye` into `destination`
    // (framebuffer 0 = the window) of the given size.
    public void Render(RenderTargets targets, uint destination, int destWidth, int destHeight, in Preset preset, Vector3 eye)
    {
        // 1) raymarch -> HDR scene
        Target scene = targets.Scene;
        Bind(scene);
        GL.glClearColor(0f, 0f, 0f, 1f);
        GL.glClear(GL.COLOR_BUFFER_BIT);
        GL.glUseProgram(_fractal);
        GL.glUniform2f(_uRes, scene.Width, scene.Height);
        GL.Uniform(_uCamPos, eye);
        GL.Uniform(_uCamTarget, Vector3.Zero);
        GL.Uniform(_uCSize, preset.CSize);
        GL.glUniform1f(_uSize, preset.Size);
        GL.glUniform1i(_uIters, preset.Iters);
        GL.glUniform1f(_uBound, preset.Bound);
        GL.glUniform1f(_uThickness, preset.Thickness);
        GL.glUniform1f(_uRot, preset.Rot);
        GL.Uniform(_uJuliaC, preset.JuliaC);
        GL.Uniform(_uColShift, preset.ColShift);
        Draw();

        // 2) bright-pass -> BloomA (half size)
        Target a = targets.BloomA, b = targets.BloomB;
        Bind(a);
        GL.glUseProgram(_bright);
        GL.BindTexture(0, scene.Texture);
        GL.glUniform2f(_brightSize, a.Width, a.Height);
        Draw();

        // 3) separable Gaussian blur, ping-pong A -> B -> A
        GL.glUseProgram(_blur);
        GL.glUniform2f(_blurSize, a.Width, a.Height);
        for (int pass = 0; pass < BlurPasses; pass++)
        {
            Bind(b);
            GL.BindTexture(0, a.Texture);
            GL.glUniform2f(_blurDir, 1f, 0f);
            Draw();

            Bind(a);
            GL.BindTexture(0, b.Texture);
            GL.glUniform2f(_blurDir, 0f, 1f);
            Draw();
        }

        // 4) composite -> destination
        GL.glBindFramebuffer(GL.FRAMEBUFFER, destination);
        GL.glViewport(0, 0, destWidth, destHeight);
        GL.glUseProgram(_composite);
        GL.BindTexture(0, scene.Texture);
        GL.BindTexture(1, a.Texture);
        GL.glUniform2f(_compositeSize, destWidth, destHeight);
        GL.glUniform1f(_compositeBloom, preset.Bloom);
        Draw();
    }

    // Stretches `texture` over the window.
    public void Blit(uint texture, int width, int height)
    {
        GL.glBindFramebuffer(GL.FRAMEBUFFER, 0);
        GL.glViewport(0, 0, width, height);
        GL.glUseProgram(_copy);
        GL.BindTexture(0, texture);
        GL.glUniform2f(_copySize, width, height);
        Draw();
    }

    private static void Bind(in Target target)
    {
        GL.glBindFramebuffer(GL.FRAMEBUFFER, target.Framebuffer);
        GL.glViewport(0, 0, target.Width, target.Height);
    }

    private static void Draw() => GL.glDrawArrays(GL.TRIANGLE_STRIP, 0, 4);
}
