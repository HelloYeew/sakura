#version 330 core

#include "sh_Utils.glsl"

in vec4 v_Color;
in vec2 v_TexCoords;
in vec2 v_FragPos;
in float v_TexIndex;
in vec4 v_ClipData;
in float v_ClipShearX;
in float v_ClipRadius;

out vec4 FragColor;

uniform sampler2D u_Textures[8];

// Masking uniforms
uniform bool u_IsMasking;
uniform vec2 u_MaskCenter;     // True center of the element in screen space
uniform vec2 u_MaskHalfSize;   // Un-sheared half size (Width/2, Height/2)
uniform float u_ShearX;        // Container.Shear.X
uniform float u_CornerRadius;

// Border uniforms
uniform bool u_IsBorder;
uniform float u_BorderThickness;
uniform vec4 u_BorderColor;

void main()
{
    if (u_IsBorder)
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

    if (u_IsMasking && u_CornerRadius > 0.0)
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
