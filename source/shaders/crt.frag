#ifdef GL_FRAGMENT_PRECISION_HIGH
precision highp float;
#else
precision mediump float;
#endif

varying vec2 uv;                  // Top-left (0,0), bottom-right (1,1).
uniform sampler2D frame;
uniform vec2 inputSize;            // Actual texture dimensions, in pixels.
uniform vec2 sourceSize;           // Original game dimensions; <=0 uses input picture size.
uniform vec4 contentRect;          // Picture x, y, width, height in normalized texture coords.

uniform bool curvatureEnabled;
uniform vec2 curvatureAmount;      // Direct coefficients, NOT the old denominator of 7.
uniform float curvatureScale;     // 1 = no extra shrink; >1 shrinks to expose corners.
uniform bool softeningEnabled;
uniform float softeningRadius;    // In original game pixels.
uniform float softeningStrength;  // Total contribution from both side samples.
uniform bool scanlinesEnabled;
uniform float scanlineCount;
uniform float scanlineStrength;
uniform float scanlineFadeStart;
uniform float scanlineFadeEnd;
uniform bool maskEnabled;
uniform float maskColumns;        // Individual R/G/B stripes across the picture.
uniform float maskRows;           // Number of staggered rows, independent of raster count.
uniform float maskStrength;       // 0 = neutral; 1 = prototype's 1.08/0.88 mask.
uniform bool bloomEnabled;
uniform vec2 bloomRadius;         // In original game pixels.
uniform float bloomStrength;

bool outside(vec2 p) {
    return p.x < 0.0 || p.x > 1.0 || p.y < 0.0 || p.y > 1.0;
}

// All effect samples stay inside the picture, rather than reading letterbox bars.
vec4 samplePicture(vec2 p) {
    vec2 halfTexel = min(0.5 / max(inputSize * contentRect.zw, vec2(1.0)), vec2(0.5));
    p = clamp(p, halfTexel, vec2(1.0) - halfTexel);
    return texture2D(frame, contentRect.xy + p * contentRect.zw);
}

void main() {
    // The host uses the same rectangle for input and output. Existing borders stay intact.
    vec2 screenUV = (uv - contentRect.xy) / contentRect.zw;
    if (outside(screenUV)) {
        gl_FragColor = texture2D(frame, uv);
        return;
    }

    vec2 pictureUV = screenUV;
    // No geometry or scale adjustment at all when curvature is disabled or zero.
    if (curvatureEnabled && max(curvatureAmount.x, curvatureAmount.y) > 0.0) {
        vec2 p = screenUV * 2.0 - 1.0;
        p = (p + p * p.yx * p.yx * max(curvatureAmount, vec2(0.0))) * curvatureScale;
        pictureUV = p * 0.5 + 0.5;
        if (outside(pictureUV)) {
            gl_FragColor = vec4(0.0, 0.0, 0.0, 1.0);
            return;
        }
    }

    vec2 logicalSize = sourceSize;
    if (logicalSize.x <= 0.0) logicalSize.x = inputSize.x * contentRect.z;
    if (logicalSize.y <= 0.0) logicalSize.y = inputSize.y * contentRect.w;
    logicalSize = max(logicalSize, vec2(1.0));

    vec4 original = samplePicture(pictureUV);
    vec3 color = original.rgb;

    if (softeningEnabled) {
        vec2 offset = vec2(softeningRadius / logicalSize.x, 0.0);
        vec3 left = samplePicture(pictureUV - offset).rgb;
        vec3 right = samplePicture(pictureUV + offset).rgb;
        float strength = clamp(softeningStrength, 0.0, 1.0);
        color = color * (1.0 - strength) + (left + right) * (strength * 0.5);
    }

    if (scanlinesEnabled) {
        float phase = fract(pictureUV.y * max(scanlineCount, 1.0));
        float distanceFromCenter = abs(phase - 0.5);
        float beginFade = clamp(scanlineFadeStart, 0.0, 0.499);
        float endFade = clamp(scanlineFadeEnd, beginFade + 0.001, 0.5);
        float fade = smoothstep(beginFade, endFade, distanceFromCenter);
        color *= 1.0 - clamp(scanlineStrength, 0.0, 1.0) * fade;
    }

    if (maskEnabled) {
        // Fixed to output picture coordinates, as in Ibrahim's prototype.
        float row = floor(screenUV.y * max(maskRows, 1.0));
        float phase = fract(screenUV.x * (max(maskColumns, 3.0) / 3.0) + row * 0.5);
        vec3 phosphor;
        if (phase < 1.0 / 3.0) phosphor = vec3(1.08, 0.88, 0.88);
        else if (phase < 2.0 / 3.0) phosphor = vec3(0.88, 1.08, 0.88);
        else phosphor = vec3(0.88, 0.88, 1.08);
        color *= mix(vec3(1.0), phosphor, clamp(maskStrength, 0.0, 1.0));
    }

    if (bloomEnabled) {
        // Retains the prototype's four diagonal samples and per-channel cube.
        vec2 spread = max(bloomRadius, vec2(0.0)) / logicalSize;
        vec3 blur = (
            samplePicture(pictureUV + vec2(-spread.x, -spread.y)).rgb +
            samplePicture(pictureUV + vec2( spread.x, -spread.y)).rgb +
            samplePicture(pictureUV + vec2(-spread.x,  spread.y)).rgb +
            samplePicture(pictureUV + vec2( spread.x,  spread.y)).rgb
        ) * 0.25;
        color += blur * blur * blur * max(bloomStrength, 0.0);
    }

    // Intentional SDR output: no tone mapping, no simulated glass reflections.
    gl_FragColor = vec4(clamp(color, 0.0, 1.0), original.a);
}
