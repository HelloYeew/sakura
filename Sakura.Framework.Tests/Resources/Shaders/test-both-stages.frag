#version 450

// The block's color as the vertex shader passed it on, times the block's color as read here: squared, if both stages have it.

#include "sh_Custom.glsl"

layout(set = 0, binding = 5, std140) uniform SharedBlock
{
    vec4 u_Shared;
};

void main()
{
    writeFragment(vec4(v_Color.rgb * u_Shared.rgb, 1.0));
}
