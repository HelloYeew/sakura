// Shared GLSL utility functions for Sakura Framework shaders.
// Include this file via: #include "sh_Utils.glsl"
// Do NOT use as a standalone shader stage.

// Exact 2D distance to a Parallelogram with rounded corners.
// p  : fragment position relative to the center
// b  : half-extents (width/2, height/2) of the un-sheared rectangle
// sk : horizontal skew offset (Shear.X * half-height)
// r  : corner radius
float sdRoundParallelogram(in vec2 p, in vec2 b, in float sk, in float r) {
    // Prevent division by zero if height is extremely small
    float h = max(b.y, 0.0001);

    // Calculate the horizontal inflation needed to maintain perpendicular distance
    float l = length(vec2(sk, h));
    vec2 r_offset = vec2(r * (l / h), r);

    // Shrink the base bounds to account for the corner radius exactly
    vec2 b_inner = max(b - r_offset, vec2(0.0));

    // Proportionally scale the horizontal shear offset for the inner shape
    float sk_inner = sk * (b_inner.y / h);

    // Parallelogram SDF against the perfect inner bounds
    vec2 e_inner = vec2(sk_inner, max(b_inner.y, 0.0001));
    p = (p.y < 0.0) ? -p : p;
    vec2 w = p - e_inner;
    w.x -= clamp(w.x, -b_inner.x, b_inner.x);
    vec2 d = vec2(dot(w,w), -w.y);
    float s = p.x * e_inner.y - p.y * e_inner.x;
    p = (s < 0.0) ? -p : p;
    vec2 v = p - vec2(b_inner.x, 0.0);
    v -= e_inner * clamp(dot(v,e_inner) / dot(e_inner,e_inner), -1.0, 1.0);
    d = min(d, vec2(dot(v,v), b_inner.x * b_inner.y - abs(s)));

    // Subtracting r inflates the shape back to the exact outer bounds
    return sqrt(d.x) * sign(-d.y) - r;
}

// Applies the software clip rect and optional sheared rounded-corner alpha fade.
//
// fragPos    : screen-space position of the fragment (v_FragPos)
// clipData   : (CenterX, CenterY, HalfWidth, HalfHeight)
// clipShearX : horizontal shear multiplier 
// clipRadius : corner radius of the clip region
// color      : current fragment colour -- alpha may be reduced by the fade
//
// Returns true if the fragment should be kept, false if it should be discarded.
bool applyClipping(in vec2 fragPos, in vec4 clipData, in float clipShearX, in float clipRadius, inout vec4 color) {
    // if HalfWidth is <= 0, no mask is active.
    if (clipData.z <= 0.0) return true;

    vec2 center = clipData.xy;
    vec2 halfSize = clipData.zw;
    vec2 posInRect = fragPos - center;
    float sk = clipShearX * halfSize.y;

    float dist = sdRoundParallelogram(posInRect, halfSize, sk, clipRadius);

    float alpha = 1.0 - smoothstep(-0.5, 0.5, dist);
    color.a *= alpha;

    if (color.a <= 0.01) return false;

    return true;
}