#version 450

layout(location = 0) in vec2 aPosition;
layout(location = 1) in vec2 aTexCoords;
layout(location = 2) in vec4 aColor;
layout(location = 3) in float aTexIndex;
layout(location = 4) in vec4 aClipData;
layout(location = 5) in float aClipShearX;
layout(location = 6) in float aClipRadius;

layout(location = 0) out vec4 v_Color;
layout(location = 1) out vec2 v_TexCoords;
layout(location = 2) out vec2 v_FragPos;
layout(location = 3) out float v_TexIndex;
layout(location = 4) out vec4 v_ClipData;
layout(location = 5) out float v_ClipShearX;
layout(location = 6) out float v_ClipRadius;

layout(set = 0, binding = 0, std140) uniform ProjectionBlock
{
    mat4 u_Projection;
};

void main()
{
    gl_Position = u_Projection * vec4(aPosition, 0.0, 1.0);
    v_Color = aColor;
    v_TexCoords = aTexCoords;
    v_FragPos = aPosition;
    v_TexIndex = aTexIndex;
    v_ClipData = aClipData;
    v_ClipShearX = aClipShearX;
    v_ClipRadius = aClipRadius;
}
