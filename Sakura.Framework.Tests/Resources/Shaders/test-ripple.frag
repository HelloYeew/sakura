#version 450

// Rings running out from the middle of a disc, for TestCustomShader: what a game's shader looks like. v_TexCoords says
// where in the disc, 0-1 across it.

#include "sh_Custom.glsl"

layout(set = 0, binding = 5, std140) uniform RippleBlock
{
    vec4 u_Color;
    float u_Time;
    float u_Rings;
    float u_Alpha;
    float u_Padding;
};

void main()
{
    float r = length(v_TexCoords * 2.0 - 1.0);
    float wave = 0.5 + 0.5 * sin(r * u_Rings * 6.2831853 - u_Time * 4.0);
    float inside = 1.0 - smoothstep(0.96, 1.0, r);

    writeFragment(vec4(u_Color.rgb * mix(0.25, 1.0, wave), u_Alpha * inside));
}
