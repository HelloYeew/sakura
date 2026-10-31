#version 450

// The color in its block, and nothing else: it reads none of what's on its vertices.

#include "sh_Custom.glsl"

layout(set = 0, binding = 5, std140) uniform SolidBlock
{
    vec4 u_Color;
};

void main()
{
    writeFragment(u_Color);
}
