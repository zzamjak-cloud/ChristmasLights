# Changelog

이 프로젝트의 주요 변경 사항을 기록합니다.

포맷은 [Keep a Changelog](https://keepachangelog.com/ko/1.1.0/)를 따르며,
버전은 [Semantic Versioning](https://semver.org/lang/ko/)을 따릅니다.

## [1.0.0] - 2026-09-13

기능을 독립 UPM 패키지로 분리한 최초 릴리스입니다.

### Added

- 흑백 마스크 한 장에서 전구 덩어리를 자동 분리해 ID 맵을 굽는 에디터 베이커 (8-연결 플러드 필)
- 전구별 난수 위상으로 색이 순환하는 URP 언릿 셰이더 (스프라이트 / UI `Image` 공용, UI Mask·RectMask2D 지원)
- Unity `Gradient` 를 256x1 LUT 로 구워 쓰는 팔레트 (색 개수 제한 없음, Fixed / Blend 전환)
- 모션 4종 `Random` / `Chase` / `Wave` / `Sync` 와 전구별 독립 트윙클
- Play 모드에 들어가지 않고 씬 뷰에서 재생하는 60초 미리보기 버튼
- Sprite Atlas UV 보정, 회전 패킹 및 `Image.type` 경고
- 데모 씬 샘플 (`Christmas Tree Sample`)
