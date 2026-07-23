#version 450

#include "sh_Utils.glsl"

layout(location = 0) in vec4 v_Color;
layout(location = 1) in vec2 v_TexCoords;
layout(location = 2) in vec2 v_FragPos;
layout(location = 3) in float v_TexIndex;
layout(location = 4) in vec4 v_ClipData;
layout(location = 5) in float v_ClipShearX;
layout(location = 6) in float v_ClipRadius;

layout(location = 0) out vec4 FragColor;

layout(set = 1, binding = 0) uniform sampler2D u_Textures[16];

// Masking + border state. Field order matches the std140 layout in MaskBlock in C#
//   vec4 u_BorderColor (offset 0)
//   vec2 u_MaskCenter (offset 16)
//   vec2 u_MaskHalfSize (offset 24)
//   float u_ShearX (offset 32)
//   float u_CornerRadius (offset 36)
//   float u_BorderThickness(offset 40)
//   int u_IsMasking (offset 44)
//   int u_IsBorder (offset 48)
//   int u_IsEdgeEffect (offset 52)
//   float u_EdgeRadius (offset 56)
//   vec2 u_EdgeOffset (offset 64)
//   int u_EdgeHollow (offset 72)
//   int u_EdgeGlow (offset 76)
layout(set = 0, binding = 1, std140) uniform MaskBlock
{
    vec4 u_BorderColor;
    vec2 u_MaskCenter;
    vec2 u_MaskHalfSize;
    float u_ShearX;
    float u_CornerRadius;
    float u_BorderThickness;
    int u_IsMasking;
    int u_IsBorder;
    int u_IsEdgeEffect;
    float u_EdgeRadius;
    vec2 u_EdgeOffset;
    int u_EdgeHollow;
    int u_EdgeGlow;
};

void main()
{
    if (u_IsEdgeEffect != 0)
    {
        // The edge effect is shaped like the container's rounded parallelogram, optionally offset.
        vec2 posInRect = v_FragPos - (u_MaskCenter + u_EdgeOffset);
        float sk = u_ShearX * u_MaskHalfSize.y;

        float dist = sdRoundParallelogram(posInRect, u_MaskHalfSize, sk, u_CornerRadius);

        // Soft falloff: alpha is 1 inside the shape and fades to 0 over u_EdgeRadius outside it.
        float r = max(u_EdgeRadius, 0.5);
        float alpha = 1.0 - smoothstep(0.0, r, dist);

        // Hollow effects cut out the interior, leaving only the surrounding ring.
        if (u_EdgeHollow != 0)
        {
            // Distance to the (un-offset) container shape; remove anything covered by the container.
            vec2 innerPos = v_FragPos - u_MaskCenter;
            float innerDist = sdRoundParallelogram(innerPos, u_MaskHalfSize, sk, u_CornerRadius);
            alpha *= smoothstep(-0.5, 0.5, innerDist);
        }

        vec4 finalColor = u_BorderColor;
        finalColor.a *= alpha;
        if (finalColor.a <= 0.0) discard;

        FragColor = finalColor;

        // Edge effects still honour any enclosing parent mask.
        if (!applyClipping(v_FragPos, v_ClipData, v_ClipShearX, v_ClipRadius, FragColor))
            discard;
        return;
    }

    if (u_IsBorder != 0)
    {
        vec2 posInRect = v_FragPos - u_MaskCenter;
        float sk = u_ShearX * u_MaskHalfSize.y;

        float dist = sdRoundParallelogram(posInRect, u_MaskHalfSize, sk, u_CornerRadius);

        float outerAlpha = 1.0 - smoothstep(-0.5, 0.5, dist);
        float innerAlpha = smoothstep(-u_BorderThickness - 0.5, -u_BorderThickness + 0.5, dist);

        vec4 finalColor = u_BorderColor;
        finalColor.a *= outerAlpha * innerAlpha;
        if (finalColor.a <= 0.0) discard;

        FragColor = finalColor;

        // Borders must honour any enclosing parent mask, exactly like the edge-effect path above.
        // Without this the border quad is drawn everywhere its own geometry reaches, so a bordered
        // masking child leaks its border past a rectangular ancestor mask.
        if (!applyClipping(v_FragPos, v_ClipData, v_ClipShearX, v_ClipRadius, FragColor))
            discard;
        return;
    }

    vec4 texColor;
    int index = int(v_TexIndex + 0.5);

    if (index == 0) texColor = texture(u_Textures[0], v_TexCoords);
    else if (index == 1) texColor = texture(u_Textures[1], v_TexCoords);
    else if (index == 2) texColor = texture(u_Textures[2], v_TexCoords);
    else if (index == 3) texColor = texture(u_Textures[3], v_TexCoords);
    else if (index == 4) texColor = texture(u_Textures[4], v_TexCoords);
    else if (index == 5) texColor = texture(u_Textures[5], v_TexCoords);
    else if (index == 6) texColor = texture(u_Textures[6], v_TexCoords);
    else if (index == 7) texColor = texture(u_Textures[7], v_TexCoords);
    else if (index == 8) texColor = texture(u_Textures[8], v_TexCoords);
    else if (index == 9) texColor = texture(u_Textures[9], v_TexCoords);
    else if (index == 10) texColor = texture(u_Textures[10], v_TexCoords);
    else if (index == 11) texColor = texture(u_Textures[11], v_TexCoords);
    else if (index == 12) texColor = texture(u_Textures[12], v_TexCoords);
    else if (index == 13) texColor = texture(u_Textures[13], v_TexCoords);
    else if (index == 14) texColor = texture(u_Textures[14], v_TexCoords);
    else if (index == 15) texColor = texture(u_Textures[15], v_TexCoords);
    else texColor = vec4(1.0);

    texColor *= v_Color;

    if (u_IsMasking != 0 && u_CornerRadius > 0.0)
    {
        vec2 posInRect = v_FragPos - u_MaskCenter;
        float sk = u_ShearX * u_MaskHalfSize.y;

        float dist = sdRoundParallelogram(posInRect, u_MaskHalfSize, sk, u_CornerRadius);

        // Discard fragments outside the sheared rounded rectangle
        if (dist > 0.0) discard;
    }

    FragColor = texColor;

    if (!applyClipping(v_FragPos, v_ClipData, v_ClipShearX, v_ClipRadius, FragColor))
        discard;
}
