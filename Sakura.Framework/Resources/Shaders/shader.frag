#version 330 core

in vec4 v_Color;
in vec2 v_TexCoords;
in vec2 v_FragPos;

out vec4 FragColor;

uniform sampler2D u_Texture;

// Masking uniforms
uniform bool u_IsMasking;
uniform vec4 u_MaskRect; // x, y, width, height in screen space
uniform float u_CornerRadius;

// Circle rendering uniforms
uniform bool u_IsCircle;
uniform vec4 u_CircleRect; // x, y, width, height in screen space

// SDF for rounded box
// https://www.iquilezles.org/www/articles/distfunctions2d/distfunctions2d.htm
float sdRoundBox(in vec2 p, in vec2 b, in float r) {
    vec2 q = abs(p) - b + r;
    return min(max(q.x,q.y),0.0) + length(max(q,0.0)) - r;
}

// SDF for circle
float sdCircle(in vec2 p, in float r) {
    return length(p) - r;
}

void main()
{
    vec4 texColor = texture(u_Texture, v_TexCoords) * v_Color;

    if (u_IsCircle)
    {
        vec2 center = u_CircleRect.xy + u_CircleRect.zw / 2.0;
        vec2 posInCircle = v_FragPos - center;

        // Use the smaller dimension as the radius to handle non-square sizes
        float radius = min(u_CircleRect.z, u_CircleRect.w) / 2.0;

        float dist = sdCircle(posInCircle, radius);

        // Smooth anti-aliasing
        float alpha = 1.0 - smoothstep(-0.5, 0.5, dist);
        texColor.a *= alpha;

        // Early discard for fully transparent pixels (optimization)
        if (texColor.a < 0.01)
        discard;
    }

    if (u_IsMasking && u_CornerRadius > 0.0)
    {
        // Apply rounded corner clipping
        vec2 halfSize = u_MaskRect.zw / 2.0;
        vec2 center = u_MaskRect.xy + u_MaskRect.zw / 2.0;
        vec2 posInRect = v_FragPos - center;

        float dist = sdRoundBox(posInRect, halfSize, u_CornerRadius);

        // Discard fragments outside the rounded rectangle
        if (dist > 0.0)
        discard;
    }

    FragColor = texColor;
}
