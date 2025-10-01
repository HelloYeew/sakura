#version 330 core

in vec2 v_TexCoords;

out vec4 FragColor;

uniform sampler2D u_Texture;
uniform vec4 u_Color;

void main()
{
    FragColor = texture(u_Texture, v_TexCoords) * u_Color;
}