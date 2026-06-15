Shader "BamePlastic/FogRingHaze"
{
    // Cheap horizon HAZE band for the endless road — fills the distance (incl. gaps between buildings) with a
    // soft, ground-hugging haze instead of a flat grey curtain. Colour comes from _Color (FogRing.cs feeds it the
    // so it matches DayNightController's smog exactly and the distance MELTS into it. Fades by WORLD HEIGHT
    // (thick low → clear up high, like haze settling on the ground) and by CAMERA DISTANCE (thin up close so the
    // near street stays readable, opaque far away). Unlit, transparent, no fog applied to itself, no extra pass —
    // a single cheap transparent quad-ring. WebGL-safe.
    Properties
    {
        _Color        ("Tint (multiplies fog colour)", Color) = (1,1,1,1)
        _SolidTopY    ("Solid-top world Y: fully opaque at/below this height (cover the horizon void)", Float) = 25
        _TopFade      ("Top fade (m): feathers from solid up to clear over this height", Float) = 35
        _NearClear    ("Near clear distance (m): fully transparent closer than this", Float) = 60
        _FarOpaque    ("Far opaque distance (m): fully opaque beyond this", Float) = 200
        _MaxAlpha     ("Max alpha", Range(0,1)) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        LOD 100

        Pass
        {
            Name "Haze"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off              // it's a ring/shell around the camera — show both faces

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // _Color is set per-frame by FogRing.cs to RenderSettings.fogColor (→ DayNightController atmosphere).
            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float  _SolidTopY;
                float  _TopFade;
                float  _NearClear;
                float  _FarOpaque;
                float  _MaxAlpha;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
            };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                OUT.positionWS  = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionHCS = TransformWorldToHClip(OUT.positionWS);
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                // HEIGHT profile: FULLY OPAQUE at/below _SolidTopY (so the horizon void/seam below the skyline is
                // completely covered — no 'edge of the world'), then feather to clear over _TopFade above it. This
                // is what makes it a solid haze SKIRT along the horizon, not a thin band.
                float aboveTop = IN.positionWS.y - _SolidTopY;
                float h = saturate(1.0 - aboveTop / max(0.01, _TopFade));   // 1 at/below solid top, →0 over TopFade

                // DISTANCE fade: transparent within _NearClear, ramping to opaque by _FarOpaque (near street stays
                // clear; only the distance is hazed — mirrors the exp² scene fog reach).
                float dist = distance(IN.positionWS, _WorldSpaceCameraPos);
                float d = saturate((dist - _NearClear) / max(0.01, _FarOpaque - _NearClear));

                float alpha = h * d * _MaxAlpha;

                // colour = _Color, which FogRing.cs sets to the LIVE scene fog colour each frame (so it tracks the
                // atmosphere/time-of-day). NOTE: we DON'T read unity_FogColor here — it isn't reliably populated
                // in a hand-written URP pass, which is why the ring read flat grey; the script feeds it instead.
                return half4(_Color.rgb, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
