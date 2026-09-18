Shader "Match3/Sprite/OutlineGlow"
{
    // Draws a solid-color outline around the sprite's silhouette, faded in/out via _GlowIntensity
    // (0 = invisible, unchanged from a plain sprite; 1 = full outline). Driven by Symbol.
    // PlayHintPulse, which fades this alongside the existing scale pulse - see that method for
    // exactly how it's tweened; nothing here needs to know about hints specifically.
    //
    // Technique: for each pixel, sample the texture at N points offset by _OutlineWidth pixels.
    // If THIS pixel is (mostly) transparent but a nearby sample isn't, this pixel is just outside
    // the sprite's silhouette - paint it with _OutlineColor at _GlowIntensity alpha. This is the
    // standard "8-direction neighbor sample" sprite outline technique - no second texture needed,
    // works with any sprite that has real alpha edges (i.e. isn't a fully opaque square).
    //
    // Setup: create a Material using this shader, assign it to a symbol prefab's SpriteRenderer
    // (or let Symbol.cs instance one at runtime - see its outlineGlowMaterialTemplate field).
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _OutlineColor ("Outline/Glow Color", Color) = (1, 0.9, 0.3, 1)
        _OutlineWidth ("Outline Width (texels)", Range(0, 8)) = 2
        _GlowIntensity ("Glow Intensity", Range(0,1)) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
            "RenderPipeline" = "UniversalPipeline"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "OutlineGlow"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color  : COLOR;
                float2 uv     : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float4 color  : COLOR;
                float2 uv     : TEXCOORD0;
            };

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            float4 _MainTex_ST;
            float4 _MainTex_TexelSize;
            float4 _Color;
            float4 _OutlineColor;
            float _OutlineWidth;
            float _GlowIntensity;

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = TransformObjectToHClip(v.vertex.xyz);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color * _Color;
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                half4 baseColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv) * i.color;

                if (_GlowIntensity <= 0.001 || baseColor.a > 0.1)
                    return baseColor; // fully inside the sprite, or glow off - no outline work needed

                float2 texel = _MainTex_TexelSize.xy * max(_OutlineWidth, 0.0001);
                float neighborAlpha = 0;
                neighborAlpha += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv + float2(texel.x, 0)).a;
                neighborAlpha += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv - float2(texel.x, 0)).a;
                neighborAlpha += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv + float2(0, texel.y)).a;
                neighborAlpha += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv - float2(0, texel.y)).a;
                neighborAlpha += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv + texel).a;
                neighborAlpha += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv - texel).a;
                neighborAlpha += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv + float2(texel.x, -texel.y)).a;
                neighborAlpha += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv + float2(-texel.x, texel.y)).a;

                if (neighborAlpha <= 0.1)
                    return baseColor; // no nearby opaque pixel - not an edge, stays fully transparent

                half4 outline = _OutlineColor;
                outline.a *= _GlowIntensity;
                return outline;
            }
            ENDHLSL
        }
    }
}
