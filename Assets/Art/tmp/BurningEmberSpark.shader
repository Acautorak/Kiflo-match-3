Shader "Custom/BurningEmberSpark"
{
    // Fully procedural hot-ember glow for the ignite spark (see BurningSystem.PlaySparkThenIgnite) -
    // no texture, just a soft radial core that flickers via animated value noise, warm-colored
    // (white-hot core fading to a cooler orange/red edge), additive-blended so it reads as a
    // glowing spark rather than a flat sprite. Assign a Material using this shader to the spark
    // prefab's SpriteRenderer (or MeshRenderer on a plain quad) - it ignores whatever texture the
    // renderer has, so the sprite/texture assignment itself doesn't matter, only that the mesh has
    // standard 0-1 UVs (true for any default Sprite/Quad).
    Properties
    {
        _CoreColor ("Core Color (hot)", Color) = (1, 0.95, 0.55, 1)
        _EdgeColor ("Edge Color (cool)", Color) = (1, 0.25, 0.05, 1)
        _Intensity ("Intensity", Range(0.1, 6)) = 2.2
        _Radius ("Radius", Range(0.05, 0.5)) = 0.35
        _Softness ("Edge Softness", Range(0.01, 0.5)) = 0.22
        _FlickerSpeed ("Flicker Speed", Range(0, 20)) = 8
        _FlickerAmount ("Flicker Amount", Range(0, 1)) = 0.35
        _NoiseScale ("Noise Scale", Range(1, 20)) = 6
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Blend One One // additive - glows over whatever's behind it instead of occluding it
        ZWrite Off
        Cull Off

        Pass
        {
            Name "ForwardUnlit"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _CoreColor;
                half4 _EdgeColor;
                float _Intensity;
                float _Radius;
                float _Softness;
                float _FlickerSpeed;
                float _FlickerAmount;
                float _NoiseScale;
            CBUFFER_END

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            // Cheap hash-based value noise - no texture lookup needed, just enough roughness to
            // read as roiling heat rather than a perfectly smooth pulse.
            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);

                float a = Hash21(i);
                float b = Hash21(i + float2(1.0, 0.0));
                float c = Hash21(i + float2(0.0, 1.0));
                float d = Hash21(i + float2(1.0, 1.0));

                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float2 centered = IN.uv - 0.5;
                float dist = length(centered);

                // Two octaves of noise, drifting over time in slightly different directions, so
                // the flicker doesn't repeat in an obviously periodic way.
                float t = _Time.y * _FlickerSpeed;
                float2 noiseUV = centered * _NoiseScale + float2(t * 0.3, t * 0.5);
                float n = ValueNoise(noiseUV) * 0.5 + ValueNoise(noiseUV * 2.13 + 7.0) * 0.5;

                float flicker = 1.0 - _FlickerAmount + _FlickerAmount * n;

                // Radius itself breathes with the flicker so the glow visibly pulses in size,
                // not just brightness.
                float radius = _Radius * flicker;
                float glow = saturate(1.0 - smoothstep(radius - _Softness, radius, dist));

                // White-hot at the center fading to the cooler edge color - also modulated by
                // flicker so the color shifts warmer/cooler as it pulses, not just dimmer.
                float coreMix = saturate(1.0 - dist / max(radius, 0.0001));
                half3 color = lerp(_EdgeColor.rgb, _CoreColor.rgb, coreMix * flicker);

                half alpha = glow * _Intensity;
                return half4(color * alpha, alpha); // premultiplied - correct for additive blend
            }
            ENDHLSL
        }
    }

    FallBack Off
}
