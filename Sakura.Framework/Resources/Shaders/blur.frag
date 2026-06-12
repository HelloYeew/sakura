#version 330 core

// One direction of a two-pass separable Gaussian blur, used by BufferedContainer.
// Pairs with the standard shader.vert; unused varyings are simply not declared.

in vec4 v_Color;
in vec2 v_TexCoords;

out vec4 FragColor;

uniform sampler2D u_Texture;

// 1.0 / texture size in pixels.
uniform vec2 u_TexelSize;

// (1,0) for the horizontal pass, (0,1) for the vertical pass.
uniform vec2 u_Direction;

// Gaussian sigma in texels, and the sampling radius (taps each side, max 64).
uniform float u_Sigma;
uniform int u_Radius;

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

    FragColor = sum / totalWeight;
}
