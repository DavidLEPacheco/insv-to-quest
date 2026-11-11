// SPDX-License-Identifier: MIT
Shader "Hidden/Gaussian Splatting/Composite"
{
    SubShader
    {
        CGINCLUDE
        #include "UnityCG.cginc"
        struct CompositeVertex { float4 pos : SV_POSITION; };
        CompositeVertex CompositeTriangle(uint id : SV_VertexID)
        {
            CompositeVertex o;
            o.pos=float4(float2(id&1,(id>>1)&1)*4-1,1,1);
            return o;
        }
        half4 CompositeColor(half4 splats)
        {
            return half4(GammaToLinearSpace(splats.rgb/max(splats.a,0.00001)),splats.a);
        }
        ENDCG
        Pass
        {
            ZWrite Off
            ZTest Always
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha

CGPROGRAM
#pragma vertex vert
#pragma fragment frag
#pragma require compute
#pragma use_dxc
#pragma require 2darray

// Enable proper multi-compile support for all stereo rendering modes
#pragma multi_compile_local _ UNITY_SINGLE_PASS_STEREO STEREO_INSTANCING_ON STEREO_MULTIVIEW_ON
#pragma multi_compile_local _ GS_EXPLICIT_ARRAY

#include "UnityCG.cginc"

struct v2f
{
    float4 vertex : SV_POSITION;
};

struct appdata
{
    float4 vertex : POSITION;
    uint vtxID : SV_VertexID;
};

v2f vert (uint vtxID : SV_VertexID)
{
    v2f o;

    float2 quadPos = float2(vtxID&1, (vtxID>>1)&1) * 4.0 - 1.0;
    o.vertex = float4(quadPos, 1, 1);
    return o;
}

// Separate textures for left and right eyes
#if defined(GS_EXPLICIT_ARRAY) || defined(UNITY_SINGLE_PASS_STEREO) || defined(STEREO_INSTANCING_ON) || defined(STEREO_MULTIVIEW_ON)
UNITY_DECLARE_TEX2DARRAY(_GaussianSplatRT);
#else
Texture2D _GaussianSplatRT;
SamplerState sampler_LinearClamp;
#endif

int _CustomStereoEyeIndex;
float4 _GaussianCompositeTargetSize;
half4 frag (v2f i) : SV_Target
{
    half4 col;
    #if defined(GS_EXPLICIT_ARRAY)
        float2 normalizedUV = i.vertex.xy / _GaussianCompositeTargetSize.xy;
        col = UNITY_SAMPLE_TEX2DARRAY(_GaussianSplatRT, float3(normalizedUV, _CustomStereoEyeIndex));
    #elif defined(UNITY_SINGLE_PASS_STEREO) || defined(STEREO_INSTANCING_ON) || defined(STEREO_MULTIVIEW_ON)
        float2 normalizedUV = float2(i.vertex.x / _ScreenParams.x, i.vertex.y / _ScreenParams.y);
        col = UNITY_SAMPLE_TEX2DARRAY(_GaussianSplatRT, float3(normalizedUV, _CustomStereoEyeIndex));
    #else
        float2 uv = float2(i.vertex.x / _ScreenParams.x, i.vertex.y / _ScreenParams.y);
        col = _GaussianSplatRT.SampleLevel(sampler_LinearClamp, uv, 0);
    #endif

    return float4(GammaToLinearSpace(col.rgb / max(col.a, 0.00001)), col.a);
}
ENDCG
        }
        // Mirror and other flat cameras always sample a 2D texture, even while
        // the headset is active. No XR material keyword selects the resource type.
        Pass
        {
            Name "Mono2D"
            ZWrite Off ZTest Always Cull Off
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma target 4.5
            #pragma vertex CompositeTriangle
            #pragma fragment MonoComposite
            Texture2D _BlitTexture;
            SamplerState sampler_LinearClamp;
            float4 _GaussianCompositeTargetSize;
            half4 MonoComposite(CompositeVertex i):SV_Target
            {
                return CompositeColor(_BlitTexture.SampleLevel(sampler_LinearClamp,i.pos.xy/_GaussianCompositeTargetSize.xy,0));
            }
            ENDCG
        }
        Pass
        {
            Name "PcEyeArray"
            ZWrite Off ZTest Always Cull Off
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma target 4.5
            #pragma require 2darray
            #pragma vertex CompositeTriangle
            #pragma fragment EyeComposite
            Texture2DArray _GaussianSplatRT;
            SamplerState sampler_LinearClamp;
            int _CustomStereoEyeIndex;
            float4 _GaussianCompositeTargetSize;
            half4 EyeComposite(CompositeVertex i):SV_Target
            {
                float2 uv=i.pos.xy/_GaussianCompositeTargetSize.xy;
                return CompositeColor(_GaussianSplatRT.SampleLevel(sampler_LinearClamp,float3(uv,_CustomStereoEyeIndex),0));
            }
            ENDCG
        }
    }
}
