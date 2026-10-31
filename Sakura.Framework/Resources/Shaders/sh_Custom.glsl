// What a game's own fragment shader starts from: it pairs with shader.vert, takes its inputs, and ends with
// writeFragment, which keeps it inside any mask, as the framework's own drawing is. Include this file via:
// #include "sh_Custom.glsl"
//
// Values that don't change across a drawable go in a uniform block of the shader's own, at set 0, bindings 5-7:
//     layout(set = 0, binding = 5, std140) uniform MyBlock { vec4 u_Color; float u_Time; ... };
// and the struct uploaded for it has to be a whole number of 16 bytes (CustomShaderDrawNode<TBlock> checks).

#include "sh_Utils.glsl"

layout(location = 0) in vec4 v_Color;
layout(location = 1) in vec2 v_TexCoords;
layout(location = 2) in vec2 v_FragPos;
layout(location = 3) in float v_TexIndex;
layout(location = 4) in vec4 v_ClipData;
layout(location = 5) in float v_ClipShearX;
layout(location = 6) in float v_ClipRadius;
layout(location = 7) in vec4 v_ClipFadeEdges;
layout(location = 8) in vec4 v_ClipFadeLength;

layout(location = 0) out vec4 FragColor;

// Writes a pixel, faded and clipped by any mask it's drawn in.
void writeFragment(vec4 color)
{
    // Direct3D links these inputs to the vertex shader's outputs by register, and the cross-compiler drops any the shader
    // never reads, which would move every later one onto the wrong output. So they're all read here, whichever of these
    // a shader had no use for. It's a read the compiler can't see through, and a pixel with a NaN has nothing to show.
    if (isnan(v_Color.x + v_TexCoords.x + v_TexIndex))
        discard;

    FragColor = color;
    FragColor.a *= clipFade(v_FragPos, v_ClipFadeEdges, v_ClipFadeLength);

    if (!applyClipping(v_FragPos, v_ClipData, v_ClipShearX, v_ClipRadius, FragColor))
        discard;
}
