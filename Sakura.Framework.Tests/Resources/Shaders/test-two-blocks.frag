#version 450

// Two blocks, left of x = 32 and right of it, declared out of order with one nothing reads between them: what a backend
// has to get right to find each by name.

#include "sh_Custom.glsl"

layout(set = 0, binding = 7, std140) uniform LeftBlock
{
    vec4 u_Left;
};

layout(set = 0, binding = 6, std140) uniform UnreadBlock
{
    vec4 u_Unread;
};

layout(set = 0, binding = 5, std140) uniform RightBlock
{
    vec4 u_Right;
};

void main()
{
    writeFragment(v_FragPos.x < 32.0 ? u_Left : u_Right);
}
