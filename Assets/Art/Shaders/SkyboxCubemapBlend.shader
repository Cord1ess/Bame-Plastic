// Two-cubemap blending skybox — a crossfade-able version of Unity's built-in Skybox/Cubemap. Set _Tex /
// _Tex2 to the two skyboxes to blend between and _Blend 0..1 to crossfade. _Rotation spins both around Y so
// the baked sun can be aligned with the scene's directional light. Used by SkyboxBlender for the smooth
// time-of-day sky transitions.
Shader "Skybox/CubemapBlend"
{
    Properties
    {
        _Tint ("Tint Color", Color) = (.5, .5, .5, .5)
        [Gamma] _Exposure ("Exposure", Range(0, 8)) = 1.0
        _Rotation ("Rotation", Range(0, 360)) = 0
        [NoScaleOffset] _Tex ("Cubemap A (HDR)", Cube) = "grey" {}
        [NoScaleOffset] _Tex2 ("Cubemap B (HDR)", Cube) = "grey" {}
        _Blend ("Blend A→B", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "UnityCG.cginc"

            samplerCUBE _Tex;  half4 _Tex_HDR;
            samplerCUBE _Tex2; half4 _Tex2_HDR;
            half4 _Tint;
            half _Exposure;
            float _Rotation;
            half _Blend;

            float3 RotateAroundYInDegrees(float3 vertex, float degrees)
            {
                float alpha = degrees * UNITY_PI / 180.0;
                float sina, cosa;
                sincos(alpha, sina, cosa);
                float2x2 m = float2x2(cosa, -sina, sina, cosa);
                return float3(mul(m, vertex.xz), vertex.y).xzy;
            }

            struct appdata { float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 vertex : SV_POSITION; float3 texcoord : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 rotated = RotateAroundYInDegrees(v.vertex.xyz, _Rotation);
                o.vertex = UnityObjectToClipPos(rotated);
                o.texcoord = v.vertex.xyz;
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                half4 ca = texCUBE(_Tex,  i.texcoord);
                half4 cb = texCUBE(_Tex2, i.texcoord);
                half3 a = DecodeHDR(ca, _Tex_HDR);
                half3 b = DecodeHDR(cb, _Tex2_HDR);
                half3 col = lerp(a, b, saturate(_Blend));
                col *= _Tint.rgb * unity_ColorSpaceDouble.rgb;
                col *= _Exposure;
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
