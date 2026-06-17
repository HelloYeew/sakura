#version 450

#include "sh_Utils.glsl"

layout(location = 0) in vec4 v_Color;
layout(location = 1) in vec2 v_TexCoords;
layout(location = 2) in vec2 v_FragPos;
layout(location = 3) in vec4 v_ClipData;
layout(location = 4) in float v_ClipShearX;
layout(location = 5) in float v_ClipRadius;

layout(location = 0) out vec4 FragColor;

// Y, U (Cb), V (Cr) planes -- each is a single-channel R8 texture
layout(set = 1, binding = 0) uniform sampler2D u_TextureY;
layout(set = 1, binding = 1) uniform sampler2D u_TextureU;
layout(set = 1, binding = 2) uniform sampler2D u_TextureV;

// YUV -> RGB conversion. Carried as a mat4 for a clean std140 layout (a mat3 would pad each
// column to 16 bytes anyway); only the upper-left 3x3 is used. Matches VideoBlock in C#.
layout(set = 0, binding = 4, std140) uniform VideoBlock
{
    mat4 u_YuvCoeff;
};

void main()
{
    float y  = texture(u_TextureY, v_TexCoords).r;
    float cb = texture(u_TextureU, v_TexCoords).r;
    float cr = texture(u_TextureV, v_TexCoords).r;

    vec3 yuv = vec3(y - 0.0625, cb - 0.5, cr - 0.5);
    vec3 rgb = clamp(mat3(u_YuvCoeff) * yuv, 0.0, 1.0);

    // The YUV matrix produces gamma-encoded (non-linear) RGB values matching the
    // video stream's transfer function (approx. BT.709 gamma ~2.2).
    // The framebuffer has GL_FRAMEBUFFER_SRGB enabled, which expects LINEAR input
    // and applies sRGB encoding (~gamma 2.2) on write.
    // Convert gamma-encoded -> linear here so the framebuffer encoding round-trips
    // correctly and the displayed result matches the original video brightness.
    // Approximation: linearise with gamma 2.2 (close enough for BT.709 / BT.601).
    rgb = pow(rgb, vec3(2.2));

    FragColor = vec4(rgb, 1.0) * v_Color;

    if (!applyClipping(v_FragPos, v_ClipData, v_ClipShearX, v_ClipRadius, FragColor))
        discard;
}
