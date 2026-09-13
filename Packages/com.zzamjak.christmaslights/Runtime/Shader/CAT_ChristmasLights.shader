// 전구의 정체성(ID)을 RGB 채널이 아니라 별도 ID 맵 텍스처에서 읽는다.
// ID 맵은 사용자가 그리지 않고 에디터 베이커(ChristmasLightsIDMapBaker)가 흑백 마스크에서 자동 생성한다.
//   R = 전구별 난수 ID, G/B = 전구 덩어리 중심 좌표, A = 마스크 강도
// 색은 Gradient 를 구운 256x1 LUT 를 샘플하므로 색 개수 제한이 없다.
//
// 패스는 LightMode 태그 없이 하나만 둔다. 태그 없는 패스는 URP Forward 에서 SRPDefaultUnlit 로,
// Canvas Overlay 에서는 레거시 경로로 그려져 UI/스프라이트 양쪽을 덮는다.
// 2D Renderer 를 쓰게 되면 "LightMode"="Universal2D" 패스를 추가해야 한다.
Shader "CAT/Effects/ChristmasLights"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _BaseTint ("Base Tint", Color) = (1, 1, 1, 1)

        [NoScaleOffset] _IDMap ("ID Map (자동 생성)", 2D) = "white" {}
        [NoScaleOffset] _PaletteLUT ("Palette LUT (자동 생성)", 2D) = "white" {}
        _IDMapRect ("ID Map Rect (아틀라스 UV 보정)", Vector) = (0, 0, 1, 1)

        _Speed ("Cycle Speed", Range(0.0, 5.0)) = 0.5
        _Brightness ("Brightness", Range(0.0, 8.0)) = 1.5

        // 0 = Random, 1 = Chase, 2 = Wave, 3 = Sync
        _MotionMode ("Motion Mode", Float) = 0
        _MotionAmount ("Motion Amount", Range(0.0, 1.0)) = 0
        _WaveDir ("Wave Direction", Vector) = (1, 0, 0, 0)

        _TwinkleAmount ("Twinkle Amount", Range(0.0, 1.0)) = 0
        _TwinkleSpeed ("Twinkle Speed", Range(0.0, 10.0)) = 2

        _Additive ("Additive Blend", Range(0.0, 1.0)) = 1
        _LightsOnly ("Lights Only", Range(0.0, 1.0)) = 0

        // --- UI(Mask/RectMask2D) 지원 ---
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
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
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
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "ChristmasLightsUnlit"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            // 에디트 모드 미리보기 전용. 이 키워드를 켜는 머티리얼이 없으므로 빌드에서 스트립된다.
            #pragma shader_feature_fragment _ CATLIGHTS_PREVIEW_ON

            #include "CAT_ChristmasLights.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
                // RectMask2D 는 캔버스 로컬 좌표로 클리핑하므로 오브젝트 좌표를 그대로 넘긴다.
                float4 positionOS : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert (Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                OUT.positionOS = IN.positionOS;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv         = TRANSFORM_TEX(IN.uv, _MainTex);
                OUT.color      = IN.color * _BaseTint;
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                half4 baseCol = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv) * IN.color;

                CATLightSample ls = CATSampleLights(IN.uv);
                half4 col = CATCompositeLights(baseCol, ls);

                #ifdef UNITY_UI_CLIP_RECT
                    col.a *= CATGet2DClipping(IN.positionOS.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                    clip(col.a - 0.001);
                #endif

                return col;
            }
            ENDHLSL
        }
    }
}
