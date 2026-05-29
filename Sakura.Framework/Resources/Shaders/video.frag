#version 330 core

#include "sh_Utils.glsl"

in vec4 v_Color;
in vec2 v_TexCoords;
in vec2 v_FragPos;
in vec4 v_ClipRect;
in float v_ClipRadius;

out vec4 FragColor;

// Y, U (Cb), V (Cr) planes -- each is a single-channel R8 texture
uniform sampler2D u_TextureY;
uniform sampler2D u_TextureU;
uniform sampler2D u_TextureV;

// YUV -> RGB conversion matrix (column-major).
// Supports both Rec.601 (SD) and Rec.709 (HD) colour spaces.
uniform mat3 u_YuvCoeff;

void main()
{
    float y  = texture(u_TextureY, v_TexCoords).r;
    float cb = texture(u_TextureU, v_TexCoords).r;
    float cr = texture(u_TextureV, v_TexCoords).r;
    
    vec3 yuv = vec3(y - 0.0625, cb - 0.5, cr - 0.5);
    vec3 rgb = u_YuvCoeff * yuv;

    FragColor = vec4(clamp(rgb, 0.0, 1.0), 1.0) * v_Color;
    
    if (!applyClipping(v_FragPos, v_ClipRect, v_ClipRadius, FragColor))
        discard;
}
