#version 450

// Defined only when cross-compiling for Direct3D. Its compiler can't keep a texture sample that takes
// its derivatives implicitly inside a branch that can differ between neighbouring pixels, so it moves
// every sample in the texture chain below out of its branch, and each pixel pays for all 16. With the
// derivatives passed explicitly, and the branches marked as real ones, each pixel makes the one sample
// it needs. Every other backend compiles the plain version.
#ifdef SAKURA_EXPLICIT_GRADIENTS
#extension GL_EXT_control_flow_attributes : require
#define BRANCH [[dont_flatten]]
#define SAMPLE_SLOT(i) textureGrad(u_Textures[i], v_TexCoords, texCoordsDx, texCoordsDy)
#else
#define BRANCH
#define SAMPLE_SLOT(i) texture(u_Textures[i], v_TexCoords)
#endif

#include "sh_Utils.glsl"

layout(location = 0) in vec4 v_Color;
layout(location = 1) in vec2 v_TexCoords;
layout(location = 2) in vec2 v_FragPos;
layout(location = 3) in float v_TexIndex;
layout(location = 4) in vec4 v_ClipData;
layout(location = 5) in float v_ClipShearX;
layout(location = 6) in float v_ClipRadius;
layout(location = 7) in vec4 v_ClipFadeEdges;
layout(location = 8) in vec4 v_ClipFadeLength;

layout(location = 0) out vec4 FragColor;

layout(set = 1, binding = 0) uniform sampler2D u_Textures[16];

// Border + edge-effect state. Field order matches the std140 layout in MaskBlock in C#
//   vec4 u_BorderColor (offset 0)
//   vec2 u_MaskCenter (offset 16)
//   vec2 u_MaskHalfSize (offset 24)
//   float u_ShearX (offset 32)
//   float u_CornerRadius (offset 36)
//   float u_BorderThickness(offset 40)
//   int u_IsBorder (offset 44)
//   int u_IsEdgeEffect (offset 48)
//   float u_EdgeRadius (offset 52)
//   vec2 u_EdgeOffset (offset 56)
//   int u_EdgeHollow (offset 64)
//   int u_EdgeGlow (offset 68)
layout(set = 0, binding = 1, std140) uniform MaskBlock
{
    vec4 u_BorderColor;
    vec2 u_MaskCenter;
    vec2 u_MaskHalfSize;
    float u_ShearX;
    float u_CornerRadius;
    float u_BorderThickness;
    int u_IsBorder;
    int u_IsEdgeEffect;
    float u_EdgeRadius;
    vec2 u_EdgeOffset;
    int u_EdgeHollow;
    int u_EdgeGlow;
};

void main()
{
#ifdef SAKURA_EXPLICIT_GRADIENTS
    // Taken before any branch or discard, while every pixel of the quad is still running, so they are
    // defined for all of them.
    vec2 texCoordsDx = dFdx(v_TexCoords);
    vec2 texCoordsDy = dFdy(v_TexCoords);
#endif

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

        // Edge effects still honour any enclosing parent mask, and fade with it.
        FragColor.a *= clipFade(v_FragPos, v_ClipFadeEdges, v_ClipFadeLength);
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
        FragColor.a *= clipFade(v_FragPos, v_ClipFadeEdges, v_ClipFadeLength);
        if (!applyClipping(v_FragPos, v_ClipData, v_ClipShearX, v_ClipRadius, FragColor))
            discard;
        return;
    }

    vec4 texColor;
    int index = int(v_TexIndex + 0.5);

    BRANCH if (index == 0) texColor = SAMPLE_SLOT(0);
    else BRANCH if (index == 1) texColor = SAMPLE_SLOT(1);
    else BRANCH if (index == 2) texColor = SAMPLE_SLOT(2);
    else BRANCH if (index == 3) texColor = SAMPLE_SLOT(3);
    else BRANCH if (index == 4) texColor = SAMPLE_SLOT(4);
    else BRANCH if (index == 5) texColor = SAMPLE_SLOT(5);
    else BRANCH if (index == 6) texColor = SAMPLE_SLOT(6);
    else BRANCH if (index == 7) texColor = SAMPLE_SLOT(7);
    else BRANCH if (index == 8) texColor = SAMPLE_SLOT(8);
    else BRANCH if (index == 9) texColor = SAMPLE_SLOT(9);
    else BRANCH if (index == 10) texColor = SAMPLE_SLOT(10);
    else BRANCH if (index == 11) texColor = SAMPLE_SLOT(11);
    else BRANCH if (index == 12) texColor = SAMPLE_SLOT(12);
    else BRANCH if (index == 13) texColor = SAMPLE_SLOT(13);
    else BRANCH if (index == 14) texColor = SAMPLE_SLOT(14);
    else BRANCH if (index == 15) texColor = SAMPLE_SLOT(15);
    else texColor = vec4(1.0);

    texColor *= v_Color;

    FragColor = texColor;
    FragColor.a *= clipFade(v_FragPos, v_ClipFadeEdges, v_ClipFadeLength);

    if (!applyClipping(v_FragPos, v_ClipData, v_ClipShearX, v_ClipRadius, FragColor))
        discard;
}
