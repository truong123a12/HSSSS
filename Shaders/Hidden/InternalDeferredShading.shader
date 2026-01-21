// Upgrade NOTE: replaced 'mul(UNITY_MATRIX_MVP,*)' with 'UnityObjectToClipPos(*)'
Shader "Hidden/HSSSS/Deferred Shading"
{
    Properties
    {
        _LightTexture0 ("", any) = "" {}
        _LightTextureB0 ("", 2D) = "" {}
        _ShadowMapTexture ("", any) = "" {}
        _SrcBlend ("", Float) = 1
        _DstBlend ("", Float) = 1
    }

    SubShader
    {
        // Pass 1: Lighting pass
        Pass
        {
            ZWrite Off
            Blend [_SrcBlend] [_DstBlend]

            CGPROGRAM
            #pragma target 5.0
            #pragma only_renderers d3d11
            #pragma vertex vert_deferred
            #pragma fragment frag

            // Di chuyển các pragma về đúng vị trí trong CGPROGRAM
            #pragma multi_compile_lightpass
            #pragma multi_compile ___ UNITY_HDR_ON
            #pragma multi_compile ___ _FACEWORKS_TYPE1 _FACEWORKS_TYPE2 _SCREENSPACE_SSS
            #pragma multi_compile ___ _BAKED_THICKNESS
            #pragma multi_compile ___ _PCF_ON

            #include "Assets/HSSSS/Lighting/StandardSkin.cginc"
            #include "Assets/HSSSS/Framework/Deferred.cginc"

            #ifdef _SCREENSPACE_SSS
                uniform RWTexture2D<float> _SpecularBufferR : register(u1);
                uniform RWTexture2D<float> _SpecularBufferG : register(u2);
                uniform RWTexture2D<float> _SpecularBufferB : register(u3);
            #endif

            uniform float4 _CameraGBufferTexture3_TexelSize;
            #define _TexelSize _CameraGBufferTexture3_TexelSize

            half4 CalculateLight (unity_v2f_deferred i)
            {
                ASurface s = aDeferredSurface(i);
                ADirect d = aDeferredDirect(s);

                half3 diffuse = 0.0h;
                half3 specular = 0.0h;

                aDirect(d, s, diffuse, specular);

                diffuse = aHdrClamp(diffuse);
                specular = aHdrClamp(specular);

                #ifdef _SCREENSPACE_SSS
                    if (s.scatteringMask != SHADING_MODEL_SKIN)
                    {
                        return half4(diffuse + specular, 0.0h);
                    }
                    else
                    {
                        
                        // Force coded type to uint2 to avoid warnings in some compilers    
                        uint2 coord = uint2(round((s.screenUv - 0.5f * _TexelSize.xy) * _TexelSize.zw));

                        // Use temporary variables to separate the Read and Write commands for the AMD card
                        float currentSpecR = _SpecularBufferR[coord];
                        _SpecularBufferR[coord] = currentSpecR + float(specular.r);

                        float currentSpecG = _SpecularBufferG[coord];
                        _SpecularBufferG[coord] = currentSpecG + float(specular.g);

                        float currentSpecB = _SpecularBufferB[coord];
                        _SpecularBufferB[coord] = currentSpecB + float(specular.b);

                        return half4(diffuse, 0.0h);
                    }
                #else
                    return half4(diffuse + specular, 1.0h);
                #endif
            }

            #ifdef UNITY_HDR_ON
            half4 frag (unity_v2f_deferred i) : SV_Target
            #else
            fixed4 frag (unity_v2f_deferred i) : SV_Target
            #endif
            {
                half4 c = CalculateLight(i);
                #ifdef UNITY_HDR_ON
                    return c;
                #else
                    return exp2(-c);
                #endif
            }
            ENDCG
        }

        // Pass 2: Final decode pass
        Pass
        {
            ZTest Always Cull Off ZWrite Off
            Stencil
            {
                ref [_StencilNonBackground]
                readmask [_StencilNonBackground]
                compback equal
                compfront equal
            }

            CGPROGRAM
            #pragma target 5.0
            #pragma only_renderers d3d11
            #pragma vertex vert
            #pragma fragment frag

            sampler2D _LightBuffer;
            struct v2f {
                float4 vertex : SV_POSITION;
                float2 texcoord : TEXCOORD0;
            };

            v2f vert (float4 vertex : POSITION, float2 texcoord : TEXCOORD0)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(vertex);
                o.texcoord = texcoord.xy;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                return -log2(tex2D(_LightBuffer, i.texcoord));
            }
            ENDCG 
        }
    }
    Fallback Off
}