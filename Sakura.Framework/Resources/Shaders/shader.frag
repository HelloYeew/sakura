#version 330 core

in vec2 v_TexCoords;
in vec4 v_Colour;

out vec4 FragColor;

uniform sampler2D u_Texture;

void main()
{
    FragColor = texture(u_Texture, v_TexCoords) * v_Colour;
}
