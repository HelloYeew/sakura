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

layout(set = 1, binding = 0) uniform sampler2D u_Textures[8];

// Masking + border state. Field order matches the std140 layout in MaskBlock in C#
//   vec4 u_BorderColor (offset 0)
//   vec2 u_MaskCenter (offset 16)
//   vec2 u_MaskHalfSize (offset 24)
//   float u_ShearX (offset 32)
//   float u_CornerRadius (offset 36)
//   float u_BorderThickness(offset 40)
//   int u_IsMasking (offset 44)
//   int u_IsBorder (offset 48)
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
};

void main()
{
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
