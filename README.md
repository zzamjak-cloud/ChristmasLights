# ChristmasLights

[![openupm](https://img.shields.io/npm/v/com.zzamjak.christmaslights?label=openupm&registry_uri=https://package.openupm.com)](https://openupm.com/packages/com.zzamjak.christmaslights/)
[![license](https://img.shields.io/badge/license-GPL--3.0--only-blue.svg)](Packages/com.zzamjak.christmaslights/LICENSE.md)

스프라이트/UI 이미지 한 장에서 전구마다 서로 다른 타이밍으로 색이 순환하는 크리스마스 조명 효과입니다.
RGBA 채널을 따로 그릴 필요 없이 **전구 자리를 흰색으로 칠한 흑백 마스크 한 장**만 준비하면,
에디터 베이커가 플러드 필로 전구 덩어리를 자동 분리해 ID 맵을 굽습니다.

이 레포지토리는 **개발용 Unity 프로젝트**이며, 패키지 본체는
[`Packages/com.zzamjak.christmaslights`](Packages/com.zzamjak.christmaslights) 에 임베디드되어 있습니다.
설계·구현 상세와 모바일 주의사항은 [패키지 README](Packages/com.zzamjak.christmaslights/README.md),
버전별 변경 사항은 [CHANGELOG](Packages/com.zzamjak.christmaslights/CHANGELOG.md) 를 참고하세요.

## 요구 사항

- Unity 6000.0 (Unity 6) 이상
- **Universal RP (URP)** — 셰이더가 URP 셰이더 라이브러리를 인클루드하고 샘플이 `Volume`·`Bloom` 을 쓰므로 필수입니다. `com.unity.render-pipelines.universal` 이 의존성으로 선언되어 자동 설치됩니다.
- uGUI (`com.unity.ugui`) — UI `Image` 지원용

## 설치 방법

### 1. OpenUPM (권장)

    openupm add com.zzamjak.christmaslights

또는 `Packages/manifest.json` 에 스코프 레지스트리를 직접 추가합니다.

    {
      "scopedRegistries": [
        {
          "name": "zzamjak",
          "url": "https://package.openupm.com",
          "scopes": ["com.zzamjak"]
        }
      ],
      "dependencies": {
        "com.zzamjak.christmaslights": "1.0.0"
      }
    }

### 2. Git URL

Package Manager → `Install package from git URL...`

    https://github.com/zzamjak-cloud/ChristmasLights.git?path=/Packages/com.zzamjak.christmaslights#v1.0.0

## 사용법

| 컴포넌트 | 대상 | 메뉴 |
|----------|------|------|
| `ChristmasLights` | `SpriteRenderer` 또는 UI `Image` | `Add Component > CAT > Effects > ChristmasLights` |

1. **전구 자리를 흰색으로 칠한 흑백 마스크 한 장**을 준비합니다. (채널 분리 불필요)
2. 대상 오브젝트에 `ChristmasLights` 컴포넌트를 붙입니다.
3. `Mask Texture` 에 마스크를 넣고 **[ID 맵 굽기]** 를 누릅니다. → `전구 N개 감지됨`
4. `Palette` Gradient 로 색을, `Cycle Speed` 로 속도를 조절합니다.
5. **[▶ 60초 미리보기]** 로 Play 모드에 들어가지 않고 씬 뷰에서 확인합니다.

주요 인스펙터 항목

| 항목 | 설명 |
|---|---|
| `Palette` | Fixed 모드 = 딱딱 끊기는 전환, Blend 모드 = 부드러운 전환. 색 개수 제한 없음 |
| `Motion` | `Random` / `Chase` / `Wave` / `Sync` |
| `Twinkle` | 전구별 독립 깜빡임 |
| `Brightness` | 1 이상이면 HDR 발광이 되어 URP Bloom 에 반응 |
| `ID Map Downscale` | ID 맵은 압축할 수 없으므로 메모리가 부담되면 낮춥니다 |

## 샘플

Package Manager → ChristmasLights → Samples → `Christmas Tree Sample` → Import

트리 3그루(`Random` / `Chase` / `Random + Twinkle`)와 UI `Image`(`Wave`) 데모 씬입니다.
트리 아트와 전구 마스크도 코드로 생성되므로 외부 에셋이 없습니다.
메뉴 `CAT > Effects > ChristmasLights 샘플 씬 생성` 으로 다시 만들 수 있습니다.

## 라이선스

GNU General Public License v3.0 only. [LICENSE](Packages/com.zzamjak.christmaslights/LICENSE.md) 참고.
