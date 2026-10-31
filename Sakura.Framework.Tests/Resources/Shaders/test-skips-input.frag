#version 450

// Reads the inputs at locations 0 and 2, but not 1: Direct3D would feed location 2 from the wrong output.

layout(location = 0) in vec4 v_Color;
layout(location = 1) in vec2 v_TexCoords;
layout(location = 2) in vec2 v_FragPos;

layout(location = 0) out vec4 FragColor;

void main()
{
    FragColor = v_Color * v_FragPos.x;
}
