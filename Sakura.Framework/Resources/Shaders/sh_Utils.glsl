// Shared GLSL utility functions for Sakura Framework shaders.
// Include this file via: #include "sh_Utils.glsl"
// Do NOT use as a standalone shader stage.

// Signed-distance function for a rounded rectangle.
// p  : fragment position relative to rectangle centre
// b  : half-extents of the rectangle
// r  : corner radius
// Returns negative inside, positive outside, 0 on the edge.
// Reference: https://iquilezles.org/articles/distfunctions2d/
float sdRoundBox(in vec2 p, in vec2 b, in float r) {
    vec2 q = abs(p) - b + r;
    return min(max(q.x, q.y), 0.0) + length(max(q, 0.0)) - r;
}

// Applies the software clip rect and optional rounded-corner alpha fade.
//
// fragPos : screen-space position of the fragment (v_FragPos)
// clipRect : (left, top, right, bottom) -- inactive when clipRect.z <= clipRect.x
// clipRadius : corner radius of the clip region; 0 for hard rectangular clipping
// color : current fragment colour -- alpha may be reduced by the fade
//
// Returns true if the fragment should be kept, false if it should be discarded.
bool applyClipping(in vec2 fragPos, in vec4 clipRect, in float clipRadius, inout vec4 color) {
    // No clip rect active (sentinel: right <= left)
    if (clipRect.z <= clipRect.x)
        return true;

    // Hard rectangular discard
    if (fragPos.x < clipRect.x || fragPos.x > clipRect.z ||
        fragPos.y < clipRect.y || fragPos.y > clipRect.w)
        return false;

    // Soft rounded-corner fade
    if (clipRadius > 0.0) {
        vec2 halfSize = (clipRect.zw - clipRect.xy) / 2.0;
        vec2 center   = clipRect.xy + halfSize;
        float dist    = sdRoundBox(fragPos - center, halfSize, clipRadius);
        float alpha   = 1.0 - smoothstep(-0.5, 0.5, dist);
        color.a      *= alpha;
        if (color.a <= 0.01)
            return false;
    }

    return true;
}
