#version 330 core

in vec4 v_Color;
in vec2 v_TexCoords;
in vec2 v_FragPos;
in float v_TexIndex;
in vec4 v_ClipRect;
in float v_ClipRadius;

out vec4 FragColor;

uniform sampler2D u_Textures[8];

// Masking uniforms
uniform bool u_IsMasking;
uniform vec4 u_MaskRect; // x, y, width, height in screen space
uniform float u_CornerRadius;

// Border uniforms
uniform bool u_IsBorder;
uniform float u_BorderThickness;
uniform vec4 u_BorderColor;

// Circle rendering uniforms
uniform bool u_IsCircle;
uniform vec4 u_CircleRect; // x, y, width, height in screen space

// SDF for rounded box
// https://iquilezles.org/articles/distfunctions2d/
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
    if (u_IsBorder)
    {
        vec2 halfSize = u_MaskRect.zw / 2.0;
        vec2 center = u_MaskRect.xy + u_MaskRect.zw / 2.0;
        vec2 posInRect = v_FragPos - center;

        float dist = sdRoundBox(posInRect, halfSize, u_CornerRadius);

        // Outer edge anti-aliasing (outside the shape, dist > 0)
        // smoothstep returns 0 at -0.5, 1 at 0.5. We want alpha 1 inside (negative dist), 0 outside.
        float outerAlpha = 1.0 - smoothstep(-0.5, 0.5, dist);

        // Inner edge anti-aliasing (inside the border, dist > -thickness)
        // The border is between -thickness and 0.
        // We want alpha 0 when dist < -thickness (inside the hollow center).
        float innerAlpha = smoothstep(-u_BorderThickness - 0.5, -u_BorderThickness + 0.5, dist);

        vec4 finalColor = u_BorderColor;
        finalColor.a *= outerAlpha * innerAlpha;

        if (finalColor.a <= 0.0) discard;

        FragColor = finalColor;
        return;
    }

    vec4 texColor;
    int index = int(v_TexIndex + 0.5);

    if (index == 0) texColor = texture(u_Textures[0], v_TexCoords);
    else if(index == 1) texColor = texture(u_Textures[1], v_TexCoords);
    else if(index == 2) texColor = texture(u_Textures[2], v_TexCoords);
    else if(index == 3) texColor = texture(u_Textures[3], v_TexCoords);
    else if(index == 4) texColor = texture(u_Textures[4], v_TexCoords);
    else if(index == 5) texColor = texture(u_Textures[5], v_TexCoords);
    else if(index == 6) texColor = texture(u_Textures[6], v_TexCoords);
    else if(index == 7) texColor = texture(u_Textures[7], v_TexCoords);
    else texColor = vec4(1.0); // Fallback

    texColor *= v_Color;

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
    
    // Software clipping
    // If Z >= X, a valid clip rect was passed into the vertex
    if (v_ClipRect.z > v_ClipRect.x)
    {
        // Hard clipping (Replaces glScissor)
        if (v_FragPos.x < v_ClipRect.x || v_FragPos.x > v_ClipRect.z ||
        v_FragPos.y < v_ClipRect.y || v_FragPos.y > v_ClipRect.w)
        {
            discard;
        }

        // Soft clipping (Replaces glStencil for rounded corners)
        if (v_ClipRadius > 0.0)
        {
            vec2 halfSize = (v_ClipRect.zw - v_ClipRect.xy) / 2.0;
            vec2 center = v_ClipRect.xy + halfSize;
            vec2 posInRect = v_FragPos - center;

            float dist = sdRoundBox(posInRect, halfSize, v_ClipRadius);
            // Smoothly fade the edge pixels for perfect anti-aliasing
            float clipAlpha = 1.0 - smoothstep(-0.5, 0.5, dist);
            texColor.a *= clipAlpha;

            if (texColor.a <= 0.01) discard;
        }
    }
}
