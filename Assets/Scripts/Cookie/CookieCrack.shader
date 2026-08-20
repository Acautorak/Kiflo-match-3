Shader "Match3/UI/CookieCrack"
{
    // Drives a spreading-crack look on a UI Image via one extra grayscale texture (_CrackTex)
    // used as a per-pixel THRESHOLD, not a literal crack sprite to composite: as _CrackAmount
    // rises from 0 to 1, any pixel whose _CrackTex value is BELOW the current amount gets tinted
    // toward _CrackColor - so cracks "spread" outward from wherever _CrackTex is darkest first,
    // same trick a dissolve shader uses, just blending a color instead of cutting alpha to 0.
    //
    // Setup:
    // 1. _MainTex: the intact cookie sprite/texture.
    // 2. _CrackTex: a grayscale texture the same size (or any size, it's sampled independently) -
    //    paint/generate a noisy pattern radiating from wherever you want the first cracks to
    //    appear (e.g. darker near the center, brighter near the edges = cracks start center-out).
    //    A cheap way to get one: any "cracked earth" / voronoi noise texture from a texture pack,
    //    or generate one in an image editor with a cell-noise filter.
    // 3. Assign a Material using this shader to the cookie's Image.material (CookieSmashManager
    //    instances it at runtime so per-session _CrackAmount writes don't touch the shared asset).
    // 4. CookieSmashManager sets _CrackAmount via material.SetFloat("_CrackAmount", progress) once
    //    per tap - nothing else needs to touch this shader at runtime.
    Properties
    {
        [PerRendererData] _MainTex ("Cookie Texture", 2D) = "white" {}
        _CrackTex ("Crack Threshold Texture (grayscale)", 2D) = "white" {}
        _CrackColor ("Crack Color", Color) = (0.25, 0.12, 0.05, 1)
        _CrackAmount ("Crack Amount", Range(0,1)) = 0
        _CrackEdgeSoftness ("Crack Edge Softness", Range(0.001, 0.3)) = 0.06
        _Color ("Tint", Color) = (1,1,1,1)

        // Standard uGUI masking boilerplate - lets this shader work correctly inside a
        // RectMask2D/nested-canvas setup exactly like the built-in UI/Default shader does.
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
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

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "CookieCrack"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local UNITY_UI_ALPHACLIP

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            TEXTURE2D(_MainTex);   SAMPLER(sampler_MainTex);
            TEXTURE2D(_CrackTex);  SAMPLER(sampler_CrackTex);
            float4 _MainTex_ST;

            float4 _Color;
            float4 _CrackColor;
            float _CrackAmount;
            float _CrackEdgeSoftness;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                OUT.vertex = TransformObjectToHClip(v.vertex.xyz);
                OUT.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                OUT.color = v.color * _Color;
                return OUT;
            }

            half4 frag(v2f IN) : SV_Target
            {
                half4 baseColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.texcoord) * IN.color;
                half threshold = SAMPLE_TEXTURE2D(_CrackTex, sampler_CrackTex, IN.texcoord).r;

                // smoothstep gives cracked pixels a soft blended edge instead of a hard on/off
                // line as _CrackAmount sweeps past a given pixel's threshold value.
                half crackMask = smoothstep(threshold - _CrackEdgeSoftness, threshold + _CrackEdgeSoftness, _CrackAmount);

                half3 finalRgb = lerp(baseColor.rgb, _CrackColor.rgb, crackMask * _CrackColor.a);
                half4 finalColor = half4(finalRgb, baseColor.a);

                #ifdef UNITY_UI_ALPHACLIP
                clip(finalColor.a - 0.001);
                #endif

                return finalColor;
            }
            ENDHLSL
        }
    }
}
