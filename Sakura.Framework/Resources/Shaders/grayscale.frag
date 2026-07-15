#version 450

// Grayscale pass for BufferedContainer. Pairs with the standard shader.vert.

layout(location = 0) in vec4 v_Color;
layout(location = 1) in vec2 v_TexCoords;

layout(location = 0) out vec4 FragColor;

layout(set = 1, binding = 0) uniform sampler2D u_Texture;

layout(set = 0, binding = 2, std140) uniform GrayscaleBlock
{
    // 0.0 = original colors, 1.0 = fully grayscale.
    float u_Strength;
};

void main()
{
    vec4 colour = texture(u_Texture, v_TexCoords);

    float gray = dot(colour.rgb, vec3(0.299, 0.587, 0.114));

    // Modulate by v_Color (always 1,1,1,1 for the effect pass, so a no-op visually) so the varying
    // is not optimised out. Without it the fragment shader's input signature starts at location 1,
    // and a register-based VS->PS linkage (the Direct3D translation layer) then feeds
    // v_TexCoords from the vertex shader's location-0 output (v_Color) instead — sampling a constant
    // texcoord. Keeping location 0 present makes the interpolator registers line up. Matches the main
    // shader's `sampled * v_Color` convention.
    FragColor = vec4(mix(colour.rgb, vec3(gray), u_Strength), colour.a) * v_Color;
}
