# ChristmasLights

스프라이트/UI 이미지 한 장에서 전구마다 서로 다른 타이밍으로 색이 순환하는 크리스마스 조명 효과.

## 쓰는 법

1. **전구 자리를 흰색으로 칠한 흑백 마스크 한 장**을 준비한다. (채널 분리 불필요)
2. 대상 `Image` 또는 `SpriteRenderer` 에 `CAT/Effects/ChristmasLights` 컴포넌트를 붙인다.
3. `Mask Texture` 에 마스크를 넣고 **[ID 맵 굽기]** 를 누른다. → `전구 N개 감지됨`
4. `Palette` Gradient 로 색을, `Cycle Speed` 로 속도를 조절한다.

`SpriteRenderer` 를 쓸 때는 인스펙터의 **[머티리얼 에셋 생성 후 할당]** 버튼으로 머티리얼을 한 번 만들어 준다.
UI `Image` 는 컴포넌트가 인스턴스 머티리얼을 자동으로 물린다.

## 설계

전구의 **정체성(ID) / 팔레트 / 타이밍**을 분리한 것이 핵심이다.
이전 버전은 ID 를 텍스처의 R/G/B 채널로 표현해 그룹이 3개로 제한되고 채널마다 그림을 그려야 했다.

### ID 맵 (자동 생성)

`ChristmasLightsIDMapBaker` 가 마스크를 **8-연결 플러드 필**로 라벨링해 전구 덩어리를 분리하고,
덩어리별 정보를 RGBA 로 굽는다. 사용자는 이 텍스처를 직접 건드리지 않는다.

| 채널 | 내용 | 용도 |
|---|---|---|
| R | 전구별 난수 ID (0~1) | 위상 오프셋 |
| G | 덩어리 중심 X | Chase 모션 |
| B | 덩어리 중심 Y | Wave 모션 |
| A | 마스크 강도 | 알파·발광 세기 |

ID 는 `(i + 0.5) / n` 으로 고르게 펼친 뒤 셔플해 배정한다. 인접한 전구가 비슷한 ID 를 받아
같이 깜빡이는 것을 막기 위해서다.

셰이더는 ID 맵을 `sampler_linear_clamp` 로 **한 번만** 읽는다. 베이크 시 RGB 를 경계 밖으로
`Dilate Pixels`(최소 1) 만큼 팽창시켜 두므로 블롭 내부에서는 선형 보간을 해도 ID 가 상수이고,
팽창 영역 바깥은 알파가 0 이라 ID 가 섞여도 화면에 나오지 않는다.

> point/linear 샘플러를 나눠 두 번 읽던 초기 구현은 **GLES3 에서 무효**였다.
> GLES3 는 텍스처와 샘플러가 한 몸이라 인라인 샘플러 상태가 무시되고 임포터 필터가 둘 다에 적용된다.
> (Metal/Vulkan 에서만 분리가 동작했다.) 그래서 단일 선형 페치로 통일했고,
> 임포터 필터도 **Bilinear** 여야 한다.

서로 다른 전구의 팽창 영역이 맞닿으면 베이커가 경고한다. 전구 간격을 넓히거나 Dilate 를 줄이면 된다.

임포터는 자동 설정된다: **sRGB off / 무압축 / 밉맵 off / Bilinear / alphaIsTransparency off**.
`alphaIsTransparency` 를 켜면 Unity 의 알파 블리딩이 RGB 를 덮어써 ID 가 깨진다.

### 팔레트

`Gradient` 를 256×1 LUT(`RGBAHalf`, 리니어)로 구워 셰이더가 한 번만 샘플한다.
색 개수 제한이 없고, Gradient 의 **Fixed** 모드는 딱딱 끊기는 전환, **Blend** 모드는 부드러운 전환이 된다.
Linear 컬러스페이스에서는 `Color.linear` 로 변환해 저장한다.

> **Fixed 모드 주의**: Unity 의 `Gradient.Evaluate` 는 Fixed 모드에서 **다음 키**의 색을 돌려준다.
> 그래서 첫 키를 `t=0` 에 두면 그 색이 화면에 전혀 나오지 않고 마지막 색이 두 배로 나온다.
> N 색을 균등하게 돌리려면 키를 `0` 이 아니라 `1/N, 2/N, … , 1.0` 에 배치해야 한다.
> 기본 팔레트와 샘플 팔레트는 그렇게 맞춰져 있다.

### 모션

`phase = lerp(전구 ID, 정렬 위상, MotionAmount)` 한 줄로 네 모드를 모두 표현한다.

| 모드 | 정렬 위상 | MotionAmount |
|---|---|---|
| Random | — | 0 (고정) |
| Chase | 덩어리 중심 X | 사용자 지정 |
| Wave | 중심을 `Wave Direction` 에 투영 | 사용자 지정 |
| Sync | 0 | 1 (고정) |

`Twinkle` 은 ID 와 독립된 두 번째 해시축(`hash11(id + 7.77)`)으로 밝기를 흔든다.

### 갱신 정책

시간 진행은 셰이더의 `_Time` 이 담당하므로 **C# `Update()` 가 없다.**
값이 바뀌는 `OnEnable` / `OnValidate` 시점에만 머티리얼에 쓴다.

- `Renderer` → `MaterialPropertyBlock`. 오브젝트마다 머티리얼 인스턴스를 만들지 않아도 되지만,
  MPB 를 쓰는 렌더러는 SRP Batcher 대상에서 빠진다. 조명 오브젝트 몇 개 수준에서는 문제되지 않는다.
- `Graphic` → `CanvasRenderer` 가 MPB 를 지원하지 않아 `HideAndDontSave` 인스턴스 머티리얼 사용.
  이 머티리얼은 저장되지 않으므로 도메인 리로드마다 다시 만들어진다.

## 모바일 주의사항

| 항목 | 내용 |
|---|---|
| **ID 맵은 압축 불가** | 블록 압축이 ID 값을 뭉개므로 무압축 RGBA32 고정. 해상도가 곧 메모리다. |
| **ID Map Downscale** | ID 맵은 아트 해상도를 따라갈 이유가 없다. 전구가 아주 작지 않다면 2배 축소로 메모리 1/4. 전구가 사라지면 베이커가 경고한다. |
| **밉맵 없음** | 밉맵을 켜면 ID 가 섞이므로 끌 수밖에 없다. 따라서 **화면에서 크게 축소되는 오브젝트에는 부적합**하다. |
| **마스크는 빌드에 포함되지 않음** | `maskAssetGuid`(string)로만 저장한다. `Texture2D` 참조로 두면 씬 의존성으로 끌려가 에디터 전용 데이터가 빌드에 들어간다. |
| **미리보기는 빌드에서 스트립** | `CATLIGHTS_PREVIEW_ON` 은 `shader_feature` 라 이 키워드를 켜는 머티리얼이 없으면 빌드에서 사라진다. |
| **SRP Batcher 제외** | 셰이더 자체는 호환이지만 런타임이 `MaterialPropertyBlock` 을 쓰므로 해당 렌더러는 배처 대상에서 빠진다. 조명 오브젝트가 수십 개면 드로우콜이 선형 증가한다. |

측정값 (GLES3 / Android, 프래그먼트):

| | 최적화 전 | 최적화 후 |
|---|---|---|
| 텍스처 페치 | 4 | **3** |
| ALU | 약 49 | **약 38** |
| 샘플 텍스처 메모리 | 2,211 KB | **280 KB** |

`Motion` 과 `Twinkle` 은 유니폼 분기라 컴파일 시 플래튼된다(꺼져 있어도 `sin` 1회가 실행됨).
키워드로 빼면 더 줄일 수 있지만, 키워드는 머티리얼 단위인데 이 컴포넌트는 오브젝트별 값을
`MaterialPropertyBlock` 으로 넘기므로 오브젝트마다 머티리얼이 따로 필요해진다. 지금은 공유 머티리얼을 택했다.

## 에디트 모드 미리보기

인스펙터 맨 위의 **[▶ 60초 미리보기]** 를 누르면 Play 모드에 들어가지 않고 씬/게임 뷰에서
전구 순환이 재생된다. 진행바와 **[■ 중지]** 가 함께 뜨고, 60초가 지나면 자동으로 멈춘다.
재생 중에 Gradient·Speed·Motion 을 바꾸면 그대로 반영된다.

에디트 모드에서는 셰이더의 `_Time.y` 가 **멈춰 있다**(측정값: 48.84 고정). 그래서 미리보기는
경과 시간을 직접 셰이더에 밀어 넣는다.

```hlsl
#if defined(CATLIGHTS_PREVIEW_ON)
    float timeSeconds = _CATLightsPreviewTime;
#else
    float timeSeconds = _Time.y;
#endif
```

`_CATLightsPreviewTime` 은 머티리얼 프로퍼티가 아니라 `Shader.SetGlobalFloat` 로만 쓰는
전역 유니폼이라 `UnityPerMaterial` CBUFFER **밖**에 선언돼 있다. SRP Batcher 호환을 깨지 않으면서
MPB 갱신 없이 씬 전체 전구를 한꺼번에 돌리기 위해서다.

`CATLIGHTS_PREVIEW_ON` 은 `shader_feature` 라 이 키워드를 켜는 머티리얼이 없는 빌드에서는
분기째 스트립된다. 에디터 전용 기능이 런타임 ALU 를 먹지 않도록 하기 위한 것이다.
Play 모드에 들어가면 미리보기는 자동으로 꺼지고 엔진 시간으로 돌아간다.

## 샘플 씬

Package Manager → ChristmasLights → Samples → `Christmas Tree Sample` → Import

| 오브젝트 | 경로 | 설정 |
|---|---|---|
| `Tree_Random` | SpriteRenderer | Motion `Random`, Speed 0.45 |
| `Tree_Chase` | SpriteRenderer | Motion `Chase`, Amount 1, Speed 0.35 |
| `Tree_Twinkle` | SpriteRenderer | Motion `Random` + Twinkle 0.7 |
| `UI_Tree (Image)` | UI `Image` (Overlay Canvas) | Motion `Wave`, 방향 (0,1) |
| `Global Volume` | URP Bloom | Threshold 0.85 / Intensity 1.1 |

트리 아트(`Art/Tree.png`)와 전구 마스크(`Art/TreeLights_Mask.png`)도 전부 코드로 그리므로 외부 에셋이 없다.
마스크에는 전구 32개가 흰 원으로 찍혀 있고, 베이커가 그대로 32개를 검출한다.

씬과 아트를 다시 만들려면 메뉴 **CAT > Effects > ChristmasLights 샘플 씬 생성** 을 실행한다.
열려 있는 씬은 건드리지 않고 additive 로 만들어 저장한 뒤 닫는다.
Unity CLI 로는 `com.unity.pipeline` 이 설치돼 있을 때 다음처럼 바로 실행할 수 있다.

```bash
unity command --detach eval 'CAT.Effects.EditorTools.ChristmasLightsSampleBuilder.Build(); return "ok";'
```

## 주의

- **Sprite Atlas**: 패킹되면 `_MainTex` UV 가 아틀라스 좌표가 되므로 컴포넌트가
  `sprite.textureRect` 로 `_IDMapRect` 보정값을 만든다. 다만 **회전 패킹은 보정할 수 없어** 에러를 띄운다.
- **Image Type** 은 `Simple` 이어야 한다. Sliced/Tiled 는 UV 가 늘어나 ID 맵이 어긋난다.
- 셰이더 패스는 LightMode 태그가 없어 URP Forward(`SRPDefaultUnlit`) 와 Canvas Overlay 양쪽에서 그려진다.
  **2D Renderer** 로 전환한다면 `"LightMode"="Universal2D"` 패스를 추가해야 한다.
- `Brightness` 를 1 이상으로 올리면 HDR 발광이 되어 URP Bloom 에 반응한다.

## 파일

```
Runtime/ChristmasLights.cs                      런타임 컴포넌트
Runtime/Shader/CAT_ChristmasLights.shader       URP 언릿 (UI Stencil/ClipRect 포함)
Runtime/Shader/CAT_ChristmasLights.hlsl         CBUFFER, 샘플링·합성 함수
Editor/ChristmasLightsEditor.cs                 커스텀 인스펙터 (베이크 버튼, 미리보기, 경고)
Editor/ChristmasLightsIDMapBaker.cs             플러드 필 라벨링 + ID 맵 인코딩 + 임포터 설정
Editor/ChristmasLightsPreview.cs                에디트 모드 미리보기 드라이버
Editor/ChristmasLightsSampleBuilder.cs          샘플 씬·아트 생성기
Samples~/ChristmasLightsSample/                 데모 씬
```

## 라이선스

GNU General Public License v3.0 only (`GPL-3.0-only`). 저작권자 zzamjak.
재배포·수정본·파생 저작물은 이 고지를 유지하고 같은 라이선스로 배포해야 한다. [LICENSE](LICENSE.md), [NOTICE](NOTICE.md) 참고.
