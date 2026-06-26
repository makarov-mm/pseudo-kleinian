namespace Kleinian;

public static class Shaders
{
    public const string QuadVS = @"#version 330 core
layout(location=0) in vec2 aPos;
void main(){ gl_Position = vec4(aPos, 0.0, 1.0); }";

    public const string FractalFS = @"#version 330 core
out vec4 FragColor;

uniform vec2  uRes;
uniform float uTime;
uniform vec3  uCamPos;
uniform vec3  uCamTarget;
uniform vec3  uCSize;
uniform float uSize;
uniform int   uIters;
uniform float uBound;
uniform float uThickness;   // cross-section width of the structure
uniform float uRot;         // per-iteration twist (radians) in the xz plane
uniform vec3  uJuliaC;      // constant added each iteration (zero = off)
uniform vec3  uColShift;    // palette phase shift

const int   STEPS = 200;
const float MAXD  = 18.0;

float fractal(vec3 p, out vec4 trap){
    float scale = 1.0;
    vec4 tr = vec4(1e10);
    float ca = cos(uRot), sa = sin(uRot);
    mat2 rot = mat2(ca, -sa, sa, ca);
    for(int i = 0; i < uIters; i++){
        p = 2.0 * clamp(p, -uCSize, uCSize) - p;
        float r2 = max(dot(p, p), 1e-6);
        tr = min(tr, vec4(abs(p), r2));
        float k = max(uSize / r2, 1.0);
        p *= k;
        scale *= k;
        p.xz = rot * p.xz;   // twist -> spirals / helices
        p += uJuliaC;        // constant offset -> organic (zero = crystalline)
    }
    trap = tr;
    float rxy = length(p.xy);
    return 0.7 * max(rxy - uThickness, abs(rxy * p.z) / length(p)) / scale;
}

float map(vec3 p, out vec4 trap){
    float d = fractal(p, trap);
    return max(d, length(p) - uBound);
}

float mapd(vec3 p){ vec4 t; return map(p, t); }

vec3 calcNormal(vec3 p, float px){
    vec2 e = vec2(1.0, -1.0) * max(px, 0.00012);
    return normalize(
        e.xyy * mapd(p + e.xyy) + e.yyx * mapd(p + e.yyx) +
        e.yxy * mapd(p + e.yxy) + e.xxx * mapd(p + e.xxx));
}

float calcAO(vec3 p, vec3 n){
    float occ = 0.0, sca = 1.0;
    for(int i = 0; i < 5; i++){
        float hr = 0.01 + 0.13 * float(i) / 4.0;
        float d  = mapd(p + n * hr);
        occ += (hr - d) * sca;
        sca *= 0.72;
    }
    return clamp(1.0 - 1.7 * occ, 0.0, 1.0);
}

float softShadow(vec3 ro, vec3 rd){
    float res = 1.0, t = 0.012;
    for(int i = 0; i < 40; i++){
        float h = mapd(ro + rd * t);
        if(h < 0.0006) return 0.0;
        res = min(res, 13.0 * h / t);
        t += clamp(h, 0.006, 0.2);
        if(t > 5.0) break;
    }
    return clamp(res, 0.0, 1.0);
}

vec3 pal(float t, vec3 a, vec3 b, vec3 c, vec3 d){
    return a + b * cos(6.28318530718 * (c * t + d));
}

vec3 skyColor(vec3 rd){
    float g = clamp(rd.y * 0.5 + 0.5, 0.0, 1.0);
    return mix(vec3(0.018, 0.022, 0.038), vec3(0.05, 0.075, 0.12), g);
}

vec3 render(vec2 uv, float px){
    vec3 ro  = uCamPos;
    vec3 fwd = normalize(uCamTarget - ro);
    vec3 rgt = normalize(cross(fwd, vec3(0.0, 1.0, 0.0)));
    vec3 up  = cross(rgt, fwd);
    vec3 rd  = normalize(uv.x * rgt + uv.y * up + 1.7 * fwd);

    vec3 sky = skyColor(rd);

    float t = 0.02;
    vec4 trap = vec4(0.0);
    bool hit = false;
    float glow = 0.0;
    vec4 gtrap = vec4(1e10);

    for(int i = 0; i < STEPS; i++){
        vec3 pos = ro + rd * t;
        vec4 tr;
        float d = map(pos, tr);
        if(length(pos) < uBound + 0.05){
            glow += exp(-d * 11.0);
            gtrap = min(gtrap, tr);
        }
        float eps = clamp(px * t, 5e-6, 0.0035);
        if(d < eps){ hit = true; trap = tr; break; }
        t += d;
        if(t > MAXD) break;
    }
    glow = min(glow, 6.0);

    vec3 glowCol = pal(0.45 + 0.5 * gtrap.y, vec3(0.5), vec3(0.5), vec3(1.0), vec3(0.0, 0.18, 0.42) + uColShift);

    vec3 pos = ro + rd * t;
    if(!hit || length(pos) > uBound + 0.02){
        return sky + glowCol * glow * 0.012;
    }

    vec3 n = calcNormal(pos, px * t);

    vec3 col = pal(0.10 + 0.95 * trap.x + 0.30 * trap.z,
                   vec3(0.5), vec3(0.5), vec3(1.0), vec3(0.50, 0.35, 0.18) + uColShift);
    vec3 col2 = pal(0.45 + 0.75 * trap.y,
                    vec3(0.5), vec3(0.5), vec3(1.0), vec3(0.08, 0.22, 0.48) + uColShift);
    col = mix(col, col2, 0.5);
    float lum = dot(col, vec3(0.299, 0.587, 0.114));
    col = mix(vec3(lum), col, 1.25);

    vec3 key  = normalize(vec3(0.7, 0.75, 0.5));
    vec3 fill = normalize(vec3(-0.6, 0.25, -0.5));

    float occ = calcAO(pos, n);
    float sh  = softShadow(pos + n * 0.002, key);
    float kd  = max(dot(n, key), 0.0) * sh;
    float fd  = max(dot(n, fill), 0.0);
    float sky2 = clamp(0.5 + 0.5 * n.y, 0.0, 1.0);

    vec3 lin = vec3(0.0);
    lin += kd  * vec3(1.45, 1.22, 0.95);
    lin += fd  * vec3(0.28, 0.40, 0.58) * occ;
    lin += sky2 * vec3(0.20, 0.26, 0.38) * occ;
    lin += occ * 0.10;

    vec3 shaded = col * lin;

    float crev = pow(1.0 - occ, 2.0);
    vec3 emis = pal(0.6 + 0.5 * trap.z, vec3(0.5), vec3(0.5), vec3(1.0), vec3(0.0, 0.33, 0.66) + uColShift);
    shaded += emis * crev * 0.9;

    vec3 V = -rd;
    vec3 H = normalize(key + V);
    float spec = pow(max(dot(n, H), 0.0), 30.0) * sh;
    shaded += spec * vec3(1.0, 0.95, 0.85) * 0.8;

    float fres = pow(1.0 - max(dot(n, V), 0.0), 4.0);
    shaded += fres * vec3(0.4, 0.55, 0.8) * occ * 0.7;

    shaded += glowCol * glow * 0.006;
    shaded = mix(shaded, sky, smoothstep(MAXD * 0.6, MAXD, t));
    return shaded;
}

void main(){
    float px = 1.7 / uRes.y;
    vec3 col = vec3(0.0);
    for(int sy = 0; sy < 2; sy++)
        for(int sx = 0; sx < 2; sx++){
            vec2 off = (vec2(float(sx), float(sy)) + 0.25) * 0.5;
            vec2 uv = (gl_FragCoord.xy + off - 0.5 * uRes) / uRes.y * 2.0;
            col += render(uv, px);
        }
    col *= 0.25;
    FragColor = vec4(col, 1.0);
}";

    public const string BrightFS = @"#version 330 core
out vec4 FragColor;
uniform sampler2D uScene;
uniform vec2 uTexSize;
void main(){
    vec2 uv = gl_FragCoord.xy / uTexSize;
    vec3 c = texture(uScene, uv).rgb;
    float l = dot(c, vec3(0.2126, 0.7152, 0.0722));
    float t = smoothstep(0.55, 1.25, l);
    FragColor = vec4(c * t, 1.0);
}";

    public const string BlurFS = @"#version 330 core
out vec4 FragColor;
uniform sampler2D uTex;
uniform vec2 uTexSize;
uniform vec2 uDir;
void main(){
    vec2 uv = gl_FragCoord.xy / uTexSize;
    vec2 px = uDir / uTexSize;
    vec3 c = texture(uTex, uv).rgb * 0.227027;
    c += texture(uTex, uv + px * 1.0).rgb * 0.1945946;
    c += texture(uTex, uv - px * 1.0).rgb * 0.1945946;
    c += texture(uTex, uv + px * 2.0).rgb * 0.1216216;
    c += texture(uTex, uv - px * 2.0).rgb * 0.1216216;
    c += texture(uTex, uv + px * 3.0).rgb * 0.0540540;
    c += texture(uTex, uv - px * 3.0).rgb * 0.0540540;
    c += texture(uTex, uv + px * 4.0).rgb * 0.0162162;
    c += texture(uTex, uv - px * 4.0).rgb * 0.0162162;
    FragColor = vec4(c, 1.0);
}";

    public const string CompositeFS = @"#version 330 core
out vec4 FragColor;
uniform sampler2D uScene;
uniform sampler2D uBloom;
uniform vec2 uTexSize;
uniform float uBloomI;

vec3 aces(vec3 x){
    return clamp((x * (2.51 * x + 0.03)) / (x * (2.43 * x + 0.59) + 0.14), 0.0, 1.0);
}

void main(){
    vec2 uv = gl_FragCoord.xy / uTexSize;
    vec3 c = texture(uScene, uv).rgb;
    vec3 b = texture(uBloom, uv).rgb;
    c += b * uBloomI;

    vec2 q = uv - 0.5;
    c *= 1.0 - 0.35 * dot(q, q) * 2.0;

    c = aces(c * 1.12);
    c = pow(c, vec3(0.4545));
    FragColor = vec4(c, 1.0);
}";

    public const string CopyFS = @"#version 330 core
out vec4 FragColor;
uniform sampler2D uTex;
uniform vec2 uTexSize;
void main(){
    vec2 uv = gl_FragCoord.xy / uTexSize;
    FragColor = vec4(texture(uTex, uv).rgb, 1.0);
}";
}