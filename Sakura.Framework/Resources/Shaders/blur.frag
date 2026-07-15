#version 450

layout(location = 0) in vec4 v_Color;
layout(location = 1) in vec2 v_TexCoords;

layout(location = 0) out vec4 FragColor;

layout(set = 1, binding = 0) uniform sampler2D u_Texture;

// Field order matches the std140 layout in BlurBlock (C#):
//   vec2  u_TexelSize  (offset 0)   // 1.0 / texture size in pixels
//   vec2  u_Direction  (offset 8)   // (1,0) horizontal pass, (0,1) vertical pass
//   float u_Sigma      (offset 16)  // Gaussian sigma in texels
//   int   u_Radius     (offset 20)  // sampling radius (taps each side, max 64)
layout(set = 0, binding = 3, std140) uniform BlurBlock
{
    vec2 u_TexelSize;
    vec2 u_Direction;
    float u_Sigma;
    int u_Radius;
};

void main()
{
    vec4 sum = texture(u_Texture, v_TexCoords);
    float totalWeight = 1.0;

    float sigma = max(u_Sigma, 0.0001);
    float denominator = 2.0 * sigma * sigma;

    for (int i = 1; i <= 64; i++)
    {
        if (i > u_Radius)
            break;

        float weight = exp(-float(i * i) / denominator);
        vec2 offset = u_Direction * u_TexelSize * float(i);

        // ClampToEdge wrapping handles sampling beyond the buffer bounds.
        sum += (texture(u_Texture, v_TexCoords + offset) + texture(u_Texture, v_TexCoords - offset)) * weight;
        totalWeight += 2.0 * weight;
    }

    // Modulate by v_Color (always 1,1,1,1 for the effect pass, so a no-op visually) so the varying is
    // not optimised out. Without it the fragment shader's input signature starts at location 1, and a
    // register-based VS->PS linkage (the Direct3D translation layer) then feeds
    // v_TexCoords from the vertex shader's location-0 output (v_Color) instead — sampling a constant
    // texcoord. Keeping location 0 present makes the interpolator registers line up.
    FragColor = (sum / totalWeight) * v_Color;
}
