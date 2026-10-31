#version 450

// A block at a binding Vulkan's shared layout doesn't have.

#include "sh_Custom.glsl"

layout(set = 0, binding = 9, std140) uniform FarBlock
{
    vec4 u_Color;
};

void main()
{
    writeFragment(u_Color);
}
