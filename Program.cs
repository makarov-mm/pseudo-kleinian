// Pseudo-Kleinian — a 3D pseudo-Kleinian fractal rendered by distance-estimated
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
//     left mouse drag  - orbit
//     mouse wheel      - zoom (all the way inside)
//     E                - offline export: render one clean camera orbit to
//                        frames/frame_XXXX.png (see frames/make_video.txt for the
//                        ffmpeg command). Smooth video regardless of realtime FPS.
//
//   dotnet run -c Release
//
// Author: Mykhailo Makarov (m.m.makarov@gmail.com), no-library style (P/Invoke only).

using System;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;

namespace Kleinian;

internal static class Program
{
    static int _w = 800, _h = 800;
    static bool _running = true;
    static Win.WndProc _wndProcRef;

    static float _yaw = 0.6f, _pitch = 0.16f, _dist = 2.1f;
    static bool _drag;
    static int _lastX, _lastY;

    // ---- Presets -------------------------------------------------------------
    // Each preset is a distinct point in the fractal's parameter space. SPACE
    // cycles through them; number keys 1..N jump straight to one. Switching also
    // resets the camera distance to a value that frames that variant nicely.
    readonly struct Preset
    {
        public readonly string Name;
        public readonly Vector3 CSize;   // box-fold half-extents (main shape lever)
        public readonly float Size;      // inversion radius factor
        public readonly int Iters;       // fold/inversion iterations (detail)
        public readonly float Bound;     // sphere the structure is carved out of
        public readonly float Thickness; // cross-section: filigree (small) .. solid (big)
        public readonly float Rot;       // per-iteration twist in xz (spirals/helices)
        public readonly Vector3 JuliaC;  // constant added each iteration; zero = off (organic)
        public readonly Vector3 ColShift;// palette hue/phase shift
        public readonly float Bloom;     // bloom intensity
        public readonly float Dist;      // camera distance applied on selection
        public Preset(string name, Vector3 cs, float size, int iters, float bound,
                      float thick, float rot, Vector3 jc, Vector3 col, float bloom, float dist)
        { Name = name; CSize = cs; Size = size; Iters = iters; Bound = bound;
          Thickness = thick; Rot = rot; JuliaC = jc; ColShift = col; Bloom = bloom; Dist = dist; }
    }

    static readonly Preset[] Presets =
    {
        // name           CSize                       size iters bound thick  rot    juliaC                        colShift                    bloom dist
        new("Cross",      new(1.00f,1.00f,1.30f),     1.0f, 14,  1.20f, 0.928f, 0.00f, new(0,0,0),                  new(0.00f,0.00f,0.00f),     0.75f, 2.10f),
        new("Snowflake",  new(0.92f,0.92f,0.92f),     1.0f, 16,  1.15f, 0.860f, 0.00f, new(0,0,0),                  new(-0.05f,0.04f,0.14f),    0.82f, 1.90f),
        new("Cathedral",  new(1.00f,1.12f,1.22f),     1.0f, 14,  1.70f, 1.000f, 0.00f, new(0,0,0),                  new(0.12f,0.02f,-0.10f),    0.70f, 2.65f),
        new("Spiral",     new(1.00f,1.00f,1.15f),     1.0f, 13,  1.30f, 0.900f, 0.22f, new(0,0,0),                  new(-0.18f,0.06f,0.22f),    0.82f, 2.20f),
        new("Helix",      new(0.95f,1.00f,1.25f),     1.0f, 12,  1.35f, 0.950f, 0.42f, new(0,0,0),                  new(0.33f,0.00f,0.00f),     0.88f, 2.30f),
        new("Organic",    new(1.00f,1.00f,1.00f),     1.0f, 10,  1.25f, 0.950f, 0.05f, new(0.00f,0.14f,-0.16f),     new(0.20f,0.40f,0.10f),     0.80f, 2.05f),
    };
    static int _preset;

    // ---- Morph mode ----------------------------------------------------------
    // Toggled with 'M'. Continuously eases from one preset to the next, looping
    // through the whole table. Every numeric parameter is interpolated, so the
    // shape, colour, twist and Julia offset all glide between variants.
    static bool _morph;
    static int _morphFrom, _morphTo;
    static float _morphPhase;            // seconds into the current segment
    const float MorphHold = 1.1f;        // dwell at each variant
    const float MorphTravel = 2.2f;      // time spent gliding to the next
    static IntPtr _hwnd;                 // kept so morph can update the title

    // offline export
    const int ExportW = 800, ExportH = 800, ExportFrames = 240;
    static bool _startExport, _exporting, _expBuilt;
    static int _expFrame;
    static float _expStartYaw;
    static byte[] _expBuf;

    // programs
    static uint _fractalProg, _brightProg, _blurProg, _compProg, _copyProg;
    // fractal uniforms
    static int _uRes, _uTime, _uCamPos, _uCamTarget, _uCSize, _uSize, _uIters, _uBound;
    static int _uThickness, _uRot, _uJuliaC, _uColShift;
    // bright / blur / composite / copy uniforms
    static int _bScene, _bSize;
    static int _blTex, _blSize, _blDir;
    static int _cScene, _cBloom, _cSize, _cInt;
    static int _coTex, _coSize;

    // live render targets (window-sized) + export render targets (fixed-size)
    static uint _texScene, _fboScene, _texB1, _fboB1, _texB2, _fboB2;
    static int _fboW = -1, _fboH = -1;
    static uint _eScene, _eFboScene, _eB1, _eFboB1, _eB2, _eFboB2, _eLdr, _eLdrFbo;

    [STAThread]
    static void Main()
    {
        IntPtr hInstance = Win.GetModuleHandleW(IntPtr.Zero);
        const string cls = "KleinianWindow";

        _wndProcRef = WindowProc;
        var wc = new Win.WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<Win.WNDCLASSEX>(),
            style = Win.CS_OWNDC | Win.CS_HREDRAW | Win.CS_VREDRAW,
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcRef),
            hInstance = hInstance,
            hCursor = Win.LoadCursorW(IntPtr.Zero, (IntPtr)Win.IDC_ARROW),
            lpszClassName = cls,
        };
        if (Win.RegisterClassExW(ref wc) == 0)
            throw new Exception("RegisterClassEx failed: " + Marshal.GetLastWin32Error());

        IntPtr hwnd = Win.CreateWindowExW(
            0, cls, "Pseudo-Kleinian — drag = orbit, wheel = zoom, E = export orbit to PNG",
            Win.WS_OVERLAPPEDWINDOW | Win.WS_VISIBLE,
            Win.CW_USEDEFAULT, Win.CW_USEDEFAULT, _w, _h,
            IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);
        if (hwnd == IntPtr.Zero)
            throw new Exception("CreateWindowEx failed: " + Marshal.GetLastWin32Error());

        IntPtr hdc = Win.GetDC(hwnd);
        IntPtr ctx = CreateGLContext(hdc);
        GL.Load();
        if (GL.wglSwapIntervalEXT != null) GL.wglSwapIntervalEXT(1);

        if (Win.GetClientRect(hwnd, out var rc)) { _w = rc.right - rc.left; _h = rc.bottom - rc.top; }

        _fractalProg = BuildProgram(Shaders.QuadVS, Shaders.FractalFS);
        _brightProg = BuildProgram(Shaders.QuadVS, Shaders.BrightFS);
        _blurProg = BuildProgram(Shaders.QuadVS, Shaders.BlurFS);
        _compProg = BuildProgram(Shaders.QuadVS, Shaders.CompositeFS);
        _copyProg = BuildProgram(Shaders.QuadVS, Shaders.CopyFS);

        _uRes = GL.glGetUniformLocation(_fractalProg, Ascii("uRes"));
        _uTime = GL.glGetUniformLocation(_fractalProg, Ascii("uTime"));
        _uCamPos = GL.glGetUniformLocation(_fractalProg, Ascii("uCamPos"));
        _uCamTarget = GL.glGetUniformLocation(_fractalProg, Ascii("uCamTarget"));
        _uCSize = GL.glGetUniformLocation(_fractalProg, Ascii("uCSize"));
        _uSize = GL.glGetUniformLocation(_fractalProg, Ascii("uSize"));
        _uIters = GL.glGetUniformLocation(_fractalProg, Ascii("uIters"));
        _uBound = GL.glGetUniformLocation(_fractalProg, Ascii("uBound"));
        _uThickness = GL.glGetUniformLocation(_fractalProg, Ascii("uThickness"));
        _uRot = GL.glGetUniformLocation(_fractalProg, Ascii("uRot"));
        _uJuliaC = GL.glGetUniformLocation(_fractalProg, Ascii("uJuliaC"));
        _uColShift = GL.glGetUniformLocation(_fractalProg, Ascii("uColShift"));
        _bScene = GL.glGetUniformLocation(_brightProg, Ascii("uScene"));
        _bSize = GL.glGetUniformLocation(_brightProg, Ascii("uTexSize"));
        _blTex = GL.glGetUniformLocation(_blurProg, Ascii("uTex"));
        _blSize = GL.glGetUniformLocation(_blurProg, Ascii("uTexSize"));
        _blDir = GL.glGetUniformLocation(_blurProg, Ascii("uDir"));
        _cScene = GL.glGetUniformLocation(_compProg, Ascii("uScene"));
        _cBloom = GL.glGetUniformLocation(_compProg, Ascii("uBloom"));
        _cSize = GL.glGetUniformLocation(_compProg, Ascii("uTexSize"));
        _cInt = GL.glGetUniformLocation(_compProg, Ascii("uBloomI"));
        _coTex = GL.glGetUniformLocation(_copyProg, Ascii("uTex"));
        _coSize = GL.glGetUniformLocation(_copyProg, Ascii("uTexSize"));

        uint quadVao = MakeQuadVao();
        _hwnd = hwnd;
        ApplyPreset(hwnd, 0);

        var clock = Stopwatch.StartNew();
        double prevT = 0.0;
        var target = Vector3.Zero;

        while (_running)
        {
            while (Win.PeekMessageW(out var msg, IntPtr.Zero, 0, 0, Win.PM_REMOVE))
            {
                if (msg.message == Win.WM_QUIT) { _running = false; break; }
                Win.TranslateMessage(ref msg);
                Win.DispatchMessageW(ref msg);
            }
            if (!_running) break;

            double now = clock.Elapsed.TotalSeconds;
            float dt = (float)(now - prevT);
            prevT = now;

            GL.glBindVertexArray(quadVao);

            if (_startExport)
            {
                _startExport = false;
                if (!_exporting) BeginExport(hwnd);
            }

            if (_exporting)
            {
                float ey = _expStartYaw + 6.2831853f * (_expFrame / (float)ExportFrames);
                var e2 = OrbitEye(ey, _pitch, _dist);
                RenderPipeline(_eScene, _eFboScene, ExportW, ExportH, _eFboB1, _eB1, _eFboB2, _eB2,
                               _eLdrFbo, ExportW, ExportH, e2, target, now);
                SaveFrame(_expFrame);
                CopyToScreen(_eLdr, _w, _h);
                Win.SwapBuffers(hdc);

                Win.SetWindowTextW(hwnd, $"Exporting {_expFrame + 1}/{ExportFrames}  ->  frames\\");
                _expFrame++;
                if (_expFrame >= ExportFrames)
                {
                    _exporting = false;
                    WriteFfmpegHelper();
                    Win.SetWindowTextW(hwnd, $"Export complete: {ExportFrames} frames in frames\\  (see frames\\make_video.txt)");
                }
                continue;
            }

            EnsureLiveTargets();
            if (_morph)
            {
                StepMorph(dt);
                _dist = CurrentPreset().Dist;   // camera breathes with the morph
            }
            if (!_drag) _yaw += dt * 0.05f;
            var eye = OrbitEye(_yaw, _pitch, _dist);
            RenderPipeline(_fboScene, _texScene, _w, _h, _fboB1, _texB1, _fboB2, _texB2,
                           0, _w, _h, eye, target, now);
            Win.SwapBuffers(hdc);
        }

        Win.wglMakeCurrent(IntPtr.Zero, IntPtr.Zero);
        Win.wglDeleteContext(ctx);
        Win.ReleaseDC(hwnd, hdc);
    }

    static Vector3 OrbitEye(float yaw, float pitch, float dist) => new(
        dist * MathF.Cos(pitch) * MathF.Sin(yaw),
        dist * MathF.Sin(pitch),
        dist * MathF.Cos(pitch) * MathF.Cos(yaw));

    // Full render: fractal -> HDR scene, bright-pass, blur ping-pong, composite -> dest.
    static void RenderPipeline(
        uint sceneFbo, uint sceneTex, int sw, int sh,
        uint b1Fbo, uint b1Tex, uint b2Fbo, uint b2Tex,
        uint destFbo, int destW, int destH,
        Vector3 eye, Vector3 target, double time)
    {
        int bw = Math.Max(1, sw / 2), bh = Math.Max(1, sh / 2);

        // pass 1: raymarch -> HDR scene
        GL.glBindFramebuffer(GL.GL_FRAMEBUFFER, sceneFbo);
        GL.glViewport(0, 0, sw, sh);
        GL.glClearColor(0f, 0f, 0f, 1f);
        GL.glClear(GL.GL_COLOR_BUFFER_BIT);
        GL.glUseProgram(_fractalProg);
        GL.glUniform2f(_uRes, sw, sh);
        GL.glUniform1f(_uTime, (float)time);
        GL.glUniform3f(_uCamPos, eye.X, eye.Y, eye.Z);
        GL.glUniform3f(_uCamTarget, target.X, target.Y, target.Z);
        Preset pr = CurrentPreset();
        GL.glUniform3f(_uCSize, pr.CSize.X, pr.CSize.Y, pr.CSize.Z);
        GL.glUniform1f(_uSize, pr.Size);
        GL.glUniform1i(_uIters, pr.Iters);
        GL.glUniform1f(_uBound, pr.Bound);
        GL.glUniform1f(_uThickness, pr.Thickness);
        GL.glUniform1f(_uRot, pr.Rot);
        GL.glUniform3f(_uJuliaC, pr.JuliaC.X, pr.JuliaC.Y, pr.JuliaC.Z);
        GL.glUniform3f(_uColShift, pr.ColShift.X, pr.ColShift.Y, pr.ColShift.Z);
        GL.glDrawArrays(GL.GL_TRIANGLE_STRIP, 0, 4);

        // pass 2: bright-pass -> B1 (half res)
        GL.glBindFramebuffer(GL.GL_FRAMEBUFFER, b1Fbo);
        GL.glViewport(0, 0, bw, bh);
        GL.glUseProgram(_brightProg);
        BindTex(GL.GL_TEXTURE0, sceneTex); GL.glUniform1i(_bScene, 0);
        GL.glUniform2f(_bSize, bw, bh);
        GL.glDrawArrays(GL.GL_TRIANGLE_STRIP, 0, 4);

        // pass 3: separable Gaussian blur, ping-pong
        GL.glUseProgram(_blurProg);
        GL.glUniform2f(_blSize, bw, bh);
        for (int k = 0; k < 2; k++)
        {
            GL.glBindFramebuffer(GL.GL_FRAMEBUFFER, b2Fbo);
            GL.glViewport(0, 0, bw, bh);
            BindTex(GL.GL_TEXTURE0, b1Tex); GL.glUniform1i(_blTex, 0);
            GL.glUniform2f(_blDir, 1f, 0f);
            GL.glDrawArrays(GL.GL_TRIANGLE_STRIP, 0, 4);

            GL.glBindFramebuffer(GL.GL_FRAMEBUFFER, b1Fbo);
            GL.glViewport(0, 0, bw, bh);
            BindTex(GL.GL_TEXTURE0, b2Tex); GL.glUniform1i(_blTex, 0);
            GL.glUniform2f(_blDir, 0f, 1f);
            GL.glDrawArrays(GL.GL_TRIANGLE_STRIP, 0, 4);
        }

        // pass 4: composite -> dest
        GL.glBindFramebuffer(GL.GL_FRAMEBUFFER, destFbo);
        GL.glViewport(0, 0, destW, destH);
        GL.glUseProgram(_compProg);
        BindTex(GL.GL_TEXTURE0, sceneTex); GL.glUniform1i(_cScene, 0);
        BindTex(GL.GL_TEXTURE1, b1Tex); GL.glUniform1i(_cBloom, 1);
        GL.glUniform2f(_cSize, destW, destH);
        GL.glUniform1f(_cInt, pr.Bloom);
        GL.glDrawArrays(GL.GL_TRIANGLE_STRIP, 0, 4);
    }

    static void CopyToScreen(uint tex, int destW, int destH)
    {
        GL.glBindFramebuffer(GL.GL_FRAMEBUFFER, 0);
        GL.glViewport(0, 0, destW, destH);
        GL.glUseProgram(_copyProg);
        BindTex(GL.GL_TEXTURE0, tex); GL.glUniform1i(_coTex, 0);
        GL.glUniform2f(_coSize, destW, destH);
        GL.glDrawArrays(GL.GL_TRIANGLE_STRIP, 0, 4);
    }

    static void BeginExport(IntPtr hwnd)
    {
        if (!_expBuilt)
        {
            int hw = ExportW / 2, hh = ExportH / 2;
            _eScene = MakeHDRTex(ExportW, ExportH); _eFboScene = MakeFbo(_eScene);
            _eB1 = MakeHDRTex(hw, hh); _eFboB1 = MakeFbo(_eB1);
            _eB2 = MakeHDRTex(hw, hh); _eFboB2 = MakeFbo(_eB2);
            _eLdr = MakeLDRTex(ExportW, ExportH); _eLdrFbo = MakeFbo(_eLdr);
            _expBuf = new byte[ExportW * ExportH * 3];
            _expBuilt = true;
        }
        Directory.CreateDirectory("frames");
        _expFrame = 0;
        _expStartYaw = _yaw;
        _exporting = true;
    }

    static void SaveFrame(int i)
    {
        GL.glBindFramebuffer(GL.GL_FRAMEBUFFER, _eLdrFbo);
        GL.glPixelStorei(GL.GL_PACK_ALIGNMENT, 1);
        GL.glReadPixels(0, 0, ExportW, ExportH, GL.GL_RGB, GL.GL_UNSIGNED_BYTE, _expBuf);
        Png.WriteRgbFlip($"frames/frame_{(i + 1):0000}.png", ExportW, ExportH, _expBuf);
    }

    static void WriteFfmpegHelper()
    {
        File.WriteAllText("frames/make_video.txt",
            "Run this from inside the frames\\ folder (needs ffmpeg installed):\r\n\r\n" +
            "ffmpeg -framerate 30 -i frame_%04d.png -c:v libx264 -pix_fmt yuv420p -crf 18 kleinian.mp4\r\n\r\n" +
            "For a looping GIF:\r\n" +
            "ffmpeg -framerate 30 -i frame_%04d.png -vf \"scale=720:-1:flags=lanczos\" kleinian.gif\r\n");
    }

    static void BindTex(uint unit, uint tex)
    {
        GL.glActiveTexture(unit);
        GL.glBindTexture(GL.GL_TEXTURE_2D, tex);
    }

    static uint MakeHDRTex(int w, int h) => MakeTex(w, h, (int)GL.GL_RGBA16F, GL.GL_FLOAT);
    static uint MakeLDRTex(int w, int h) => MakeTex(w, h, (int)GL.GL_RGBA8, GL.GL_UNSIGNED_BYTE);

    static uint MakeTex(int w, int h, int internalFmt, uint type)
    {
        uint t = 0;
        GL.glGenTextures(1, ref t);
        GL.glBindTexture(GL.GL_TEXTURE_2D, t);
        GL.glTexImage2D(GL.GL_TEXTURE_2D, 0, internalFmt, w, h, 0, GL.GL_RGBA, type, IntPtr.Zero);
        GL.glTexParameteri(GL.GL_TEXTURE_2D, GL.GL_TEXTURE_MIN_FILTER, (int)GL.GL_LINEAR);
        GL.glTexParameteri(GL.GL_TEXTURE_2D, GL.GL_TEXTURE_MAG_FILTER, (int)GL.GL_LINEAR);
        GL.glTexParameteri(GL.GL_TEXTURE_2D, GL.GL_TEXTURE_WRAP_S, (int)GL.GL_CLAMP_TO_EDGE);
        GL.glTexParameteri(GL.GL_TEXTURE_2D, GL.GL_TEXTURE_WRAP_T, (int)GL.GL_CLAMP_TO_EDGE);
        return t;
    }

    static uint MakeFbo(uint tex)
    {
        uint f = 0;
        GL.glGenFramebuffers(1, ref f);
        GL.glBindFramebuffer(GL.GL_FRAMEBUFFER, f);
        GL.glFramebufferTexture2D(GL.GL_FRAMEBUFFER, GL.GL_COLOR_ATTACHMENT0, GL.GL_TEXTURE_2D, tex, 0);
        uint st = GL.glCheckFramebufferStatus(GL.GL_FRAMEBUFFER);
        if (st != GL.GL_FRAMEBUFFER_COMPLETE) throw new Exception("Framebuffer incomplete: 0x" + st.ToString("X"));
        GL.glBindFramebuffer(GL.GL_FRAMEBUFFER, 0);
        return f;
    }

    static void EnsureLiveTargets()
    {
        if (_fboW == _w && _fboH == _h && _fboScene != 0) return;
        _fboW = _w; _fboH = _h;
        int hw = Math.Max(1, _w / 2), hh = Math.Max(1, _h / 2);
        _texScene = MakeHDRTex(_w, _h); _fboScene = MakeFbo(_texScene);
        _texB1 = MakeHDRTex(hw, hh); _fboB1 = MakeFbo(_texB1);
        _texB2 = MakeHDRTex(hw, hh); _fboB2 = MakeFbo(_texB2);
    }

    static IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case Win.WM_SIZE:
                int lp = (int)(long)lParam;
                _w = Math.Max(1, lp & 0xFFFF);
                _h = Math.Max(1, (lp >> 16) & 0xFFFF);
                return IntPtr.Zero;

            case Win.WM_KEYDOWN:
                {
                    int vk = (int)(long)wParam;
                    if (vk == 0x45) _startExport = true;                 // 'E'
                    else if (vk == 0x4D)                                 // 'M' -> toggle morph
                    {
                        _morph = !_morph;
                        if (_morph)
                        {
                            _morphFrom = _preset;
                            _morphTo = (_preset + 1) % Presets.Length;
                            _morphPhase = 0f;
                        }
                        else _dist = Presets[_preset].Dist;             // settle on current variant
                        UpdateTitle(hWnd);
                    }
                    else if (vk == 0x20)                                 // SPACE -> next variant
                        ApplyPreset(hWnd, (_preset + 1) % Presets.Length);
                    else if (vk == 0x08)                                 // BACKSPACE -> previous
                        ApplyPreset(hWnd, (_preset + Presets.Length - 1) % Presets.Length);
                    else if (vk >= 0x31 && vk <= 0x39)                   // '1'..'9' -> jump
                    {
                        int idx = vk - 0x31;
                        if (idx < Presets.Length) ApplyPreset(hWnd, idx);
                    }
                }
                return IntPtr.Zero;

            case Win.WM_LBUTTONDOWN:
                _drag = true; _lastX = Short(lParam, 0); _lastY = Short(lParam, 16);
                return IntPtr.Zero;

            case Win.WM_LBUTTONUP:
                _drag = false;
                return IntPtr.Zero;

            case Win.WM_MOUSEMOVE:
                {
                    int x = Short(lParam, 0), y = Short(lParam, 16);
                    if (_drag)
                    {
                        _yaw -= (x - _lastX) * 0.006f;
                        _pitch += (y - _lastY) * 0.006f;
                        float lim = 1.50f;
                        if (_pitch > lim) _pitch = lim;
                        if (_pitch < -lim) _pitch = -lim;
                        _lastX = x; _lastY = y;
                    }
                }
                return IntPtr.Zero;

            case Win.WM_MOUSEWHEEL:
                int delta = (short)((((long)wParam) >> 16) & 0xFFFF);
                _dist *= MathF.Pow(0.86f, delta / 120f);
                if (_dist < 0.08f) _dist = 0.08f;
                if (_dist > 10f) _dist = 10f;
                return IntPtr.Zero;

            case Win.WM_DESTROY:
                Win.PostQuitMessage(0);
                return IntPtr.Zero;
        }

        return Win.DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    static void ApplyPreset(IntPtr hwnd, int idx)
    {
        _preset = idx;
        _dist = Presets[idx].Dist;   // reframe; the user can still zoom afterwards
        // keep morph anchored at the chosen variant so it continues cleanly
        _morphFrom = idx; _morphTo = (idx + 1) % Presets.Length; _morphPhase = 0f;
        UpdateTitle(hwnd);
    }

    static void UpdateTitle(IntPtr hwnd)
    {
        string state = _morph
            ? $"MORPH  {Presets[_morphFrom].Name} -> {Presets[_morphTo].Name}"
            : $"[{_preset + 1}/{Presets.Length}] {Presets[_preset].Name}";
        Win.SetWindowTextW(hwnd,
            $"Pseudo-Kleinian — {state}   |   " +
            "M morph · SPACE next · 1-6 pick · drag orbit · wheel zoom · E export");
    }

    // Linear blend of every preset parameter (iters rounded to the nearest int).
    static Preset LerpPreset(in Preset a, in Preset b, float t) => new(
        b.Name,
        Vector3.Lerp(a.CSize, b.CSize, t),
        Lerp(a.Size, b.Size, t),
        (int)MathF.Round(Lerp(a.Iters, b.Iters, t)),
        Lerp(a.Bound, b.Bound, t),
        Lerp(a.Thickness, b.Thickness, t),
        Lerp(a.Rot, b.Rot, t),
        Vector3.Lerp(a.JuliaC, b.JuliaC, t),
        Vector3.Lerp(a.ColShift, b.ColShift, t),
        Lerp(a.Bloom, b.Bloom, t),
        Lerp(a.Dist, b.Dist, t));

    static float Lerp(float a, float b, float t) => a + (b - a) * t;

    // The preset currently driving the shader: a static one, or a blend in morph mode.
    static Preset CurrentPreset()
    {
        if (!_morph) return Presets[_preset];
        // hold at the start of each segment, then ease across to the next variant
        float travel = Math.Clamp((_morphPhase - MorphHold) / MorphTravel, 0f, 1f);
        float s = travel * travel * (3f - 2f * travel);   // smoothstep easing
        return LerpPreset(Presets[_morphFrom], Presets[_morphTo], s);
    }

    // Advance the morph timeline by dt; step to the next variant when a segment ends.
    static void StepMorph(float dt)
    {
        _morphPhase += dt;
        if (_morphPhase >= MorphHold + MorphTravel)
        {
            _morphPhase -= MorphHold + MorphTravel;
            _morphFrom = _morphTo;
            _morphTo = (_morphTo + 1) % Presets.Length;
            _preset = _morphFrom;
            UpdateTitle(_hwnd);
        }
    }

    private static int Short(IntPtr lParam, int shift) => (short)((((long)lParam) >> shift) & 0xFFFF);

    private static uint MakeQuadVao()
    {
        float[] q = [-1f, -1f, 1f, -1f, -1f, 1f, 1f, 1f];
        uint vao = 0, vbo = 0;
        GL.glGenVertexArrays(1, ref vao);
        GL.glBindVertexArray(vao);
        GL.glGenBuffers(1, ref vbo);
        GL.glBindBuffer(GL.GL_ARRAY_BUFFER, vbo);
        GCHandle h = GCHandle.Alloc(q, GCHandleType.Pinned);
        try { GL.glBufferData(GL.GL_ARRAY_BUFFER, q.Length * sizeof(float), h.AddrOfPinnedObject(), GL.GL_STATIC_DRAW); }
        finally { h.Free(); }
        GL.glVertexAttribPointer(0, 2, GL.GL_FLOAT, 0, 2 * sizeof(float), 0);
        GL.glEnableVertexAttribArray(0);
        return vao;
    }

    private static uint BuildProgram(string vsSrc, string fsSrc)
    {
        uint vs = Compile(GL.GL_VERTEX_SHADER, vsSrc);
        uint fs = Compile(GL.GL_FRAGMENT_SHADER, fsSrc);
        uint p = GL.glCreateProgram();
        GL.glAttachShader(p, vs);
        GL.glAttachShader(p, fs);
        GL.glLinkProgram(p);
        int ok = 0;
        GL.glGetProgramiv(p, GL.GL_LINK_STATUS, ref ok);

        if (ok == 0)
        {
            var log = new byte[2048];
            int len = 0;
            GL.glGetProgramInfoLog(p, log.Length, ref len, log);
            throw new Exception($"Link error: {Encoding.ASCII.GetString(log, 0, len)}");
        }

        GL.glDeleteShader(vs);
        GL.glDeleteShader(fs);
        return p;
    }

    private static uint Compile(uint type, string src)
    {
        uint sh = GL.glCreateShader(type);
        IntPtr str = Marshal.StringToHGlobalAnsi(src);

        try
        {
            GL.glShaderSource(sh, 1, [str], IntPtr.Zero);
        }
        finally
        {
            Marshal.FreeHGlobal(str);
        }

        GL.glCompileShader(sh);
        int ok = 0;
        GL.glGetShaderiv(sh, GL.GL_COMPILE_STATUS, ref ok);

        if (ok == 0)
        {
            var log = new byte[2048];
            int len = 0;
            GL.glGetShaderInfoLog(sh, log.Length, ref len, log);
            throw new Exception($"Compile error: {Encoding.ASCII.GetString(log, 0, len)}");
        }

        return sh;
    }

    private static byte[] Ascii(string s)
    {
        var b = new byte[s.Length + 1];
        Encoding.ASCII.GetBytes(s, 0, s.Length, b, 0);
        return b;
    }

    private static IntPtr CreateGLContext(IntPtr hdc)
    {
        var pfd = new Win.PIXELFORMATDESCRIPTOR
        {
            nSize = (ushort)Marshal.SizeOf<Win.PIXELFORMATDESCRIPTOR>(),
            nVersion = 1,
            dwFlags = Win.PFD_DRAW_TO_WINDOW | Win.PFD_SUPPORT_OPENGL | Win.PFD_DOUBLEBUFFER,
            iPixelType = Win.PFD_TYPE_RGBA,
            cColorBits = 32,
            cDepthBits = 24,
            cStencilBits = 8,
            iLayerType = Win.PFD_MAIN_PLANE,
        };

        int fmt = Win.ChoosePixelFormat(hdc, ref pfd);

        if (fmt == 0)
        {
            throw new Exception("ChoosePixelFormat failed");
        }

        if (!Win.SetPixelFormat(hdc, fmt, ref pfd))
        {
            throw new Exception("SetPixelFormat failed");
        }

        IntPtr tmp = Win.wglCreateContext(hdc);
        Win.wglMakeCurrent(hdc, tmp);
        IntPtr proc = Win.wglGetProcAddress("wglCreateContextAttribsARB");

        if (proc != IntPtr.Zero)
        {
            var create = Marshal.GetDelegateForFunctionPointer<GL.WglCreateContextAttribsARB>(proc);
            int[] attributes = [0x2091, 3, 0x2092, 3, 0x9126, 0x0001, 0];
            IntPtr core = create(hdc, IntPtr.Zero, attributes);

            if (core != IntPtr.Zero)
            {
                Win.wglMakeCurrent(hdc, core);
                Win.wglDeleteContext(tmp);
                return core;
            }
        }

        return tmp;
    }
}
