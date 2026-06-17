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

    FragColor = vec4(mix(colour.rgb, vec3(gray), u_Strength), colour.a);
}
