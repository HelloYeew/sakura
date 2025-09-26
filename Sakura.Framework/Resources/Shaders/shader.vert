#version 330 core

layout (location = 0) in vec3 aPosition;
layout (location = 1) in vec2 aTexCoords;
layout (location = 2) in vec4 aColour;

out vec2 v_TexCoords;
out vec4 v_Colour;

uniform mat4 u_Projection;
uniform mat4 u_Model;

void main()
{
    gl_Position = u_Projection * u_Model * vec4(aPosition.xy, 0.0, 1.0);
    v_TexCoords = aTexCoords;
    v_Colour = aColour;
}
