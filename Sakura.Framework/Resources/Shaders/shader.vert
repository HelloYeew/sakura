#version 330 core

layout (location = 0) in vec2 aPosition;
layout (location = 1) in vec4 aColor;
layout (location = 2) in vec2 aTexCoords;

out vec4 v_Color;
out vec2 v_TexCoords;

uniform mat4 u_Projection;

void main()
{
    gl_Position = u_Projection * vec4(aPosition, 0.0, 1.0);
    v_Color = aColor;
    v_TexCoords = aTexCoords;
}
