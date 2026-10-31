#version 450

// Reads the inputs at locations 0 and 1 and no further, as the framework's own effect shaders do: nothing to move.

layout(location = 0) in vec4 v_Color;
layout(location = 1) in vec2 v_TexCoords;

layout(location = 0) out vec4 FragColor;

void main()
{
    FragColor = v_Color * v_TexCoords.x;
}
