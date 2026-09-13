#ifndef CAT_CHRISTMAS_LIGHTS_INCLUDED
#define CAT_CHRISTMAS_LIGHTS_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

#define CAT_TWO_PI 6.28318530718

// SRP Batcher 호환을 위해 모든 머티리얼 프로퍼티는 이 CBUFFER 안에만 선언한다.
CBUFFER_START(UnityPerMaterial)
    float4 _MainTex_ST;
    // 스프라이트가 아틀라스에 패킹된 경우의 UV 보정. xy = 오프셋, zw = 스케일.
    float4 _IDMapRect;
    float4 _WaveDir;
    float4 _ClipRect;
    half4  _BaseTint;
    float  _Speed;
    half   _Brightness;
    float  _MotionMode;
    float  _MotionAmount;
    half   _TwinkleAmount;
    float  _TwinkleSpeed;
    half   _Additive;
    half   _LightsOnly;
CBUFFER_END

TEXTURE2D(_MainTex);      SAMPLER(sampler_MainTex);
TEXTURE2D(_IDMap);
TEXTURE2D(_PaletteLUT);

// ID 맵은 선형 샘플러로 한 번만 읽는다.
// 베이커가 ID 를 경계 밖으로 팽창시켜 두므로 블롭 내부에서는 보간해도 ID 가 상수이고,
// 팽창 영역 바깥은 알파가 0 이라 ID 가 섞여도 화면에 나오지 않는다.
// point/linear 를 나눠 두 번 읽으면 GLES3 에서는 샘플러가 하나로 합쳐져(텍스처와 한 몸이라)
// 분리가 무효가 되고 페치만 한 번 더 나간다.
SAMPLER(sampler_linear_clamp);

// 에디트 모드 미리보기용 전역 유니폼.
// 머티리얼 프로퍼티가 아니라 Shader.SetGlobalFloat 로만 쓰므로 CBUFFER 밖에 둔다.
// (SRP Batcher 는 Properties 블록에 선언된 값만 UnityPerMaterial 안에 있기를 요구한다.)
// 에디트 모드에서는 _Time.y 가 멈춰 있어 이 값이 없으면 미리보기가 정지 화면이 된다.
// shader_feature 라 이 키워드를 켜는 머티리얼이 없는 빌드에서는 통째로 스트립된다.
#if defined(CATLIGHTS_PREVIEW_ON)
    float _CATLightsPreviewTime;
#endif

// RectMask2D 클리핑 (UnityUI.cginc 는 URP HLSL 에서 쓸 수 없어 직접 구현)
float CATGet2DClipping(float2 position, float4 clipRect)
{
    float2 inside = step(clipRect.xy, position) * step(position, clipRect.zw);
    return inside.x * inside.y;
}

struct CATLightSample
{
    half3 color;
    half  mask;
};

CATLightSample CATSampleLights(float2 baseUV)
{
    float2 idUV = (baseUV - _IDMapRect.xy) / max(_IDMapRect.zw, 1e-5);

    float4 idTex = SAMPLE_TEXTURE2D(_IDMap, sampler_linear_clamp, idUV);

    float  id     = idTex.r;   // 전구별 난수 ID
    float2 center = idTex.gb;  // 전구 덩어리 중심 (0~1)
    half   mask   = (half)idTex.a;

    // 정렬 위상: Chase 는 X 좌표, Wave 는 지정 방향 투영.
    // Random(0) / Sync(3) 은 여기서 0 이고, C# 이 _MotionAmount 를 각각 0 / 1 로 고정한다.
    float ordered = 0.0;
    if (_MotionMode == 1.0)
    {
        ordered = center.x;
    }
    else if (_MotionMode == 2.0)
    {
        // _WaveDir 은 C# 에서 정규화해 넘긴다. 여기서 다시 normalize 하면
        // 분기가 플래튼되면서 sqrt/나눗셈이 모든 픽셀에 항상 얹힌다.
        ordered = saturate(dot(center - 0.5, _WaveDir.xy) + 0.5);
    }

    // 미리보기 중에는 에디터가 주입한 시간을, 평소에는 엔진 시간을 쓴다.
#if defined(CATLIGHTS_PREVIEW_ON)
    float timeSeconds = _CATLightsPreviewTime;
#else
    float timeSeconds = _Time.y;
#endif

    float phase = lerp(id, ordered, _MotionAmount);
    float t     = frac(timeSeconds * _Speed + phase);

    half4 pal = SAMPLE_TEXTURE2D(_PaletteLUT, sampler_linear_clamp, float2(t, 0.5));

    half twinkle = 1.0h;
    if (_TwinkleAmount > 0.0h)
    {
        // 색 위상과만 어긋나면 되므로 해시 체인 대신 frac 한 번으로 충분하다.
        float seed = frac(id * 97.31);
        half  s    = (half)(sin((timeSeconds * _TwinkleSpeed + seed) * CAT_TWO_PI) * 0.5 + 0.5);
        twinkle = lerp(1.0h, s, _TwinkleAmount);
    }

    CATLightSample o;
    o.color = pal.rgb * _Brightness * twinkle;
    o.mask  = mask * (half)pal.a;
    return o;
}

half4 CATCompositeLights(half4 baseCol, CATLightSample ls)
{
    half3 replaced = lerp(baseCol.rgb, ls.color, ls.mask);
    half3 added    = baseCol.rgb + ls.color * ls.mask;
    half3 rgb      = lerp(replaced, added, _Additive);

    // LightsOnly 는 원본 그림을 숨기고 전구 마스크만 알파로 남긴다.
    half a = lerp(baseCol.a, baseCol.a * ls.mask, _LightsOnly);
    return half4(lerp(rgb, ls.color, _LightsOnly), a);
}

#endif
