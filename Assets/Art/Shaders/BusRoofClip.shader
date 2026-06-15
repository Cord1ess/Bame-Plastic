Shader "BamePlastic/BusRoofClip"
{
    // A URP Lit-ish surface that DISCARDS any fragment above a world-Y plane (_ClipY) — used to slice the roof
    // off the bus for the Conductor-2 interior view. Unlike a camera OBLIQUE clip (which cuts the WHOLE scene at
    // that plane — buildings, sky, everything), this is a PER-MATERIAL clip, so ONLY the bus meshes it's applied
    // to get cut. RoleController swaps the bus materials to instances of this while in C2 view and drives _ClipY
    // to the live bus roofline; restores the originals on exit. Keeps the base map + colour so the bus looks the
    // same below the cut. Cheap (one extra clip() ), WebGL-safe.
    Properties
    {
        _BaseMap   ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1,1,1,1)
        _ClipY     ("World Y clip height", Float) = 99999
        _Smoothness("Smoothness", Range(0,1)) = 0.1
        _Metallic  ("Metallic", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        LOD 200

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Off            // render interior back-faces too (so the cut-open cabin isn't see-through/black)

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float  _ClipY;
                float  _Smoothness;
                float  _Metallic;
            CBUFFER_END

            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; };
            struct Varyings
            {
                float4 positionHCS:SV_POSITION;
                float3 positionWS :TEXCOORD0;
                float3 normalWS   :TEXCOORD1;
                float2 uv         :TEXCOORD2;
            };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                OUT.positionWS  = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionHCS = TransformWorldToHClip(OUT.positionWS);
                OUT.normalWS    = TransformObjectToWorldNormal(IN.normalOS);
                OUT.uv          = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                clip(_ClipY - IN.positionWS.y);     // discard anything ABOVE the clip plane → cuts the roof only

                half4 baseTex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;

                // double-sided lighting: interior faces often point AWAY from the camera (back faces) → flip the
                // normal toward the viewer so they light up instead of reading black.
                float3 n = normalize(IN.normalWS);
                float3 v = GetWorldSpaceNormalizeViewDir(IN.positionWS);
                if (dot(n, v) < 0) n = -n;

                InputData lightData = (InputData)0;
                lightData.positionWS = IN.positionWS;
                lightData.normalWS   = n;
                lightData.viewDirectionWS = v;
                lightData.shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                lightData.bakedGI = SampleSH(n);          // ambient → no pure-black even with no direct light

                SurfaceData s = (SurfaceData)0;
                s.albedo = baseTex.rgb;
                s.alpha = 1;
                s.metallic = _Metallic;
                s.smoothness = _Smoothness;
                s.occlusion = 1;

                return UniversalFragmentPBR(lightData, s);
            }
            ENDHLSL
        }
    }
    Fallback "Universal Render Pipeline/Lit"
}
