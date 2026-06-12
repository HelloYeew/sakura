#version 330 core

// Grayscale pass for BufferedContainer. Pairs with the standard shader.vert.

in vec4 v_Color;
in vec2 v_TexCoords;

out vec4 FragColor;

uniform sampler2D u_Texture;

// 0.0 = original colors, 1.0 = fully grayscale.
uniform float u_Strength;

void main()
{
    vec4 colour = texture(u_Texture, v_TexCoords);

    float gray = dot(colour.rgb, vec3(0.299, 0.587, 0.114));

    FragColor = vec4(mix(colour.rgb, vec3(gray), u_Strength), colour.a);
}
