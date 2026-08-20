Shader "Custom/SymbolBurning"
{
    // Applied directly to a Symbol's own SpriteRenderer material (not a separate overlay) so the
    // tile itself progressively chars and burns away rather than just having something glowing
    // sit on top of it. Driven by _BurnAmount (0 = untouched, 1 = fully consumed), which Symbol.cs
    // updates via a MaterialPropertyBlock as its burn countdown ticks down - see
    // Symbol.SetBurning/TickBurning/ApplyBurnAmount. Everything below _BurnAmount = 0 still shows
    // a subtle ever-present ember pulse (_PulseAmount) so a freshly-ignited tile immediately reads
    // as "on fire", not just once it starts visibly eroding.
    Properties
    {
        _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _BurnAmount ("Burn Amount", Range(0, 1)) = 0
        _BurnColor ("Ember Edge Color", Color) = (1, 0.45, 0.05, 1)
        _CharColor ("Charred Color", Color) = (0.12, 0.05, 0.03, 1)
        _EdgeWidth ("Erosion Edge Width", Range(0.01, 0.3)) = 0.08
        _NoiseScale ("Erosion Noise Scale", Range(1, 30)) = 12

        _PulseAmount ("Ambient Ember Pulse", Range(0, 1)) = 0.45
        _FlickerSpeed ("Flicker Speed", Range(0, 20)) = 6

        _Intensity ("Glow Intensity (push above 1 for Bloom to pick it up)", Range(1, 4)) = 1.6
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "CanUseSpriteAtlas" = "True"
        }

        Blend SrcAlpha OneMinusSrcAlpha // standard sprite alpha blend - preserves transparency
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
                half4 color       : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                half4 color        : COLOR;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
                float _BurnAmount;
                half4 _BurnColor;
                half4 _CharColor;
                float _EdgeWidth;
                float _NoiseScale;
                float _PulseAmount;
                float _FlickerSpeed;
                float _Intensity;
            CBUFFER_END

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = TRANSFORM_TEX(IN.uv, _MainTex);
                OUT.color = IN.color; // respects SpriteRenderer.color (e.g. flash/highlight tints elsewhere)
                return OUT;
            }

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
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv) * _Color * IN.color;
                if (tex.a <= 0.001) discard; // stay out of the sprite's own transparent padding

                float t = _Time.y * _FlickerSpeed;
                float2 noiseUV = IN.uv * _NoiseScale + float2(t * 0.2, t * 0.35);
                float n = ValueNoise(noiseUV) * 0.5 + ValueNoise(noiseUV * 2.3 + 3.1) * 0.5;

                // Always-on ember glow - zero at _BurnAmount = 0 (every unburnt tile shares this
                // Material's own default, so this MUST be exactly 0 there or every tile glows
                // constantly), but ramps up fast enough that Symbol's IgniteStartAmount floor
                // (0.05) already reads as a strong, immediate flare rather than a slow fade-in.
                float ambientPulse = 0.5 + 0.5 * sin(t * 1.7 + n * 6.2831);
                float ambientGlow = _PulseAmount * ambientPulse * saturate(_BurnAmount * 12.0);
                // _Intensity only multiplies the glow's CONTRIBUTION (via the lerp factor, not the
                // base sprite color) - pushes the blended-in ember tint above 1.0 so Bloom has
                // something to react to, without blowing out the sprite's own unburnt colors.
                half3 color = lerp(tex.rgb, _BurnColor.rgb * _Intensity, saturate(ambientGlow * _Intensity));

                // Overall charring darkens the whole sprite as burn progresses, independent of
                // the erosion pattern below.
                color = lerp(color, _CharColor.rgb, saturate(_BurnAmount) * 0.55);

                float alpha = tex.a;

                if (_BurnAmount > 0.001)
                {
                    // Positive = still solid at this pixel, negative = eroded away. Using the
                    // same noise field for the whole sprite means the burn front reads as one
                    // continuous, irregular char line sweeping across the tile as _BurnAmount
                    // rises, rather than a uniform fade.
                    float solidness = n - _BurnAmount;

                    if (solidness < 0.0)
                    {
                        alpha = 0.0; // fully burnt through at this pixel
                    }
                    else if (solidness < _EdgeWidth)
                    {
                        // Hot glowing rim right at the erosion boundary - the brightest, most
                        // bloom-worthy part of the effect, so it gets the full _Intensity kick.
                        float edgeFactor = 1.0 - saturate(solidness / _EdgeWidth);
                        color = lerp(color, _BurnColor.rgb * _Intensity, edgeFactor);
                        color += _BurnColor.rgb * _Intensity * edgeFactor * 0.6;
                    }
                }

                return half4(color, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
