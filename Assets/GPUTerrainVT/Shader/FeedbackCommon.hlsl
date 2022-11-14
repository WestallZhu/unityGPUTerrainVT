#ifndef VIRTUAL_TEXTURE_FEEDBACK_INCLUDED
#define VIRTUAL_TEXTURE_FEEDBACK_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "GPUTerrainVetex.hlsl"

struct Varyings
{
    float4 positionCS : SV_POSITION;
    float2 texcoord0 : TEXCOORD0;
};

float4 _VTRect;

// x: page size
// y: vertual texture size
// z: max mipmap level
// w: mipmap level bias
float4 _VTFeedbackParams;


Varyings FeedbackVert(Attributes In)
{
    Varyings o;

    GTerrainInput vinput;
    vinput = GetGTerrainInput(In);
    o.positionCS = mul(UNITY_MATRIX_VP, float4(vinput.positionWS, 1.0));
    o.texcoord0 = (vinput.positionWS.xz - _VTRect.xy) * rcp(_VTRect.zw);
    return o;
}

float4 FeedbackFrag(Varyings input) : SV_Target
{
    float2 uv = input.texcoord0 * _VTFeedbackParams.y;
    float2 dx = ddx(uv);
    float2 dy = ddy(uv);

    float mipLevel = clamp( 0.5 * log2(max(dot(dx,dx), dot(dy,dy))) + 0.5 + _VTFeedbackParams.w, 0, _VTFeedbackParams.z);
    float2 pageUV = floor(input.texcoord0 * _VTFeedbackParams.x);

    float2 hbit = floor(pageUV / 256);
    float2 lbit = pageUV - hbit * 256;

    return float4(lbit, hbit.x + hbit.y * 16, floor(mipLevel)) / 255;
}


#endif