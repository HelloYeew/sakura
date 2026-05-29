#version 330 core

in vec4 v_Color;
in vec2 v_TexCoords;
in vec2 v_FragPos;
in vec4 v_ClipRect;
in float v_ClipRadius;

out vec4 FragColor;

// Y, U (Cb), V (Cr) planes -- each is a single-channel (R8) texture
uniform sampler2D u_TextureY;
uniform sampler2D u_TextureU;
uniform sampler2D u_TextureV;

// YUV -> RGB conversion matrix (column-major, set from GetConversionMatrix())
// Supports both Rec.601 (SD) and Rec.709 (HD) colour spaces.
uniform mat3 u_YuvCoeff;

// Software clipping (matches the main shader)
float sdRoundBox(in vec2 p, in vec2 b, in float r) {
    vec2 q = abs(p) - b + r;
    return min(max(q.x, q.y), 0.0) + length(max(q, 0.0)) - r;
}

void main()
{
    // Sample the three planes
    float y  = texture(u_TextureY, v_TexCoords).r;
    float cb = texture(u_TextureU, v_TexCoords).r;
    float cr = texture(u_TextureV, v_TexCoords).r;

    // Offset: Y is studio-swing (subtract 16/255 = 0.0625), chroma is centred (subtract 0.5)
    vec3 yuv = vec3(y - 0.0625, cb - 0.5, cr - 0.5);

    // Matrix multiply: rgb = M * yuv
    vec3 rgb = u_YuvCoeff * yuv;

    vec4 color = vec4(clamp(rgb, 0.0, 1.0), 1.0) * v_Color;

    // Software clip rect (matches the main fragment shader)
    if (v_ClipRect.z > v_ClipRect.x)
    {
        if (v_FragPos.x < v_ClipRect.x || v_FragPos.x > v_ClipRect.z ||
            v_FragPos.y < v_ClipRect.y || v_FragPos.y > v_ClipRect.w)
        {
            discard;
        }

        if (v_ClipRadius > 0.0)
        {
            vec2 halfSize = (v_ClipRect.zw - v_ClipRect.xy) / 2.0;
            vec2 center   = v_ClipRect.xy + halfSize;
            float dist    = sdRoundBox(v_FragPos - center, halfSize, v_ClipRadius);
            float alpha   = 1.0 - smoothstep(-0.5, 0.5, dist);
            color.a      *= alpha;
            if (color.a <= 0.01) discard;
        }
    }

    FragColor = color;
}
