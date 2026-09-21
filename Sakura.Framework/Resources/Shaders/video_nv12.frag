#version 450

#include "sh_Utils.glsl"

layout(location = 0) in vec4 v_Color;
layout(location = 1) in vec2 v_TexCoords;
layout(location = 2) in vec2 v_FragPos;
layout(location = 3) in vec4 v_ClipData;
layout(location = 4) in float v_ClipShearX;
layout(location = 5) in float v_ClipRadius;

layout(location = 0) out vec4 FragColor;

// NV12: a full-resolution single-channel luma plane, and a half-resolution two-channel plane holding
// Cb and Cr interleaved. Both are sampled with the same normalised coordinates -- the half-resolution
// chroma plane is what gives 4:2:0 subsampling, not a different coordinate.
layout(set = 1, binding = 0) uniform sampler2D u_TextureY;
layout(set = 1, binding = 1) uniform sampler2D u_TextureCbCr;

// Affine YUV -> RGB. See video.frag; identical in every respect but the fetch.
layout(set = 0, binding = 4, std140) uniform VideoBlock
{
    mat4 u_YuvCoeff;
};

void main()
{
    float y = texture(u_TextureY, v_TexCoords).r;
    vec2 cbcr = texture(u_TextureCbCr, v_TexCoords).rg;

    vec3 rgb = clamp((u_YuvCoeff * vec4(y, cbcr, 1.0)).rgb, 0.0, 1.0);

    // See video.frag for why this linearises: the framebuffer is sRGB and expects linear input.
    rgb = pow(rgb, vec3(2.2));

    FragColor = vec4(rgb, 1.0) * v_Color;

    if (!applyClipping(v_FragPos, v_ClipData, v_ClipShearX, v_ClipRadius, FragColor))
        discard;
}
