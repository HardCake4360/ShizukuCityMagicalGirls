# 설정창 연결 방법

레이아웃과 UI 오브젝트는 직접 구성합니다. 설정창 루트에 `SettingsPanelController`를 추가한 뒤 아래 요소를 인스펙터에 연결합니다. 이 컴포넌트는 UI를 생성하거나 배치를 변경하지 않습니다.

## 화면 설정

- `Resolutions`: 순환할 해상도 목록입니다. 기본값은 1280×720, 1600×900, 1920×1080, 2560×1080, 2560×1440, 3840×2160입니다. 목록 순서와 값을 수정할 수 있습니다.
- `Previous Resolution Button`, `Next Resolution Button`, `Resolution Text`: 해상도의 좌우 버튼과 값 표시용 TMP 텍스트를 연결합니다.
- `Previous Window Mode Button`, `Next Window Mode Button`, `Window Mode Text`: 창 모드 좌우 버튼과 TMP 텍스트를 연결합니다. 창모드 → 전체화면 → 테두리없는 창모드 순서로 순환합니다.
- `Previous Quality Button`, `Next Quality Button`, `Quality Text`: 텍스처 품질 좌우 버튼과 TMP 텍스트를 연결합니다. 낮음 → 보통 → 높음 순서로 순환합니다.
- 각 `Label`은 화면에 표시할 문구입니다. 영문 UI가 필요하면 `WINDOWED`, `FULLSCREEN`, `BORDERLESS`, `LOW`, `NORMAL`, `HIGH` 등으로 변경합니다.

버튼 이벤트는 컴포넌트가 자동 등록합니다. 같은 메서드를 버튼의 `On Click`에도 추가하면 두 번 실행되므로 중복으로 연결하지 않습니다. 필요하면 공개된 `Previous/NextResolution`, `Previous/NextWindowMode`, `Previous/NextQuality` 메서드를 별도의 외부 로직에서 호출할 수 있습니다.

현재 해상도가 목록에 없으면 그대로 표시하다가 다음 버튼은 목록 첫 항목, 이전 버튼은 마지막 항목으로 전환합니다. 유효하지 않은 해상도와 중복 항목은 순환에서 제외합니다.

해상도와 창 모드는 빌드된 게임에서 실제 창에 적용됩니다. Unity Editor의 Game 뷰 크기는 변경하지 않습니다. 전체화면은 Windows의 `ExclusiveFullScreen`, 테두리없는 창모드는 `FullScreenWindow`를 사용하며, 디스플레이가 지원하지 않는 모드는 Unity/플랫폼에서 조정될 수 있습니다.

텍스처 품질만 변경하며 기존 Unity Quality Level과 URP 렌더링 설정은 바꾸지 않습니다. 낮음/보통/높음은 전역 밉맵 제한 2/1/0(일반적인 밉맵 텍스처 기준 1/4, 1/2, 원래 해상도)입니다. 밉맵이 없거나 밉맵 제한을 무시하도록 설정된 스프라이트에는 품질 차이가 나타나지 않을 수 있습니다.

## 오디오와 감도

- `Master Volume Slider/Text`: 마스터 슬라이더와 퍼센트 TMP 텍스트입니다.
- `Music Volume Slider/Text`: 음악 슬라이더와 퍼센트 TMP 텍스트입니다.
- `Effect Volume Slider/Text`: 효과음 슬라이더와 퍼센트 TMP 텍스트입니다.
- `Sensitivity Slider/Text`: 마우스 감도 슬라이더와 퍼센트 TMP 텍스트입니다.

슬라이더의 방향, 핸들, 배경, 길이 등은 직접 설정합니다. 오디오 슬라이더는 0~100, 감도는 1~100의 정수 범위를 자동 설정하므로 따로 `On Value Changed` 이벤트를 연결할 필요가 없습니다. 값을 드래그하면 즉시 적용됩니다. 선택적으로 텍스트를 할당하지 않아도 슬라이더는 작동합니다.

마스터·음악·효과음의 기본값은 모두 70%입니다. 이미 저장된 볼륨 값은 덮어쓰지 않고 그대로 불러옵니다. 감도 기본값은 50%를 유지합니다.

마스터는 `AudioListener.volume`을 조절합니다. 음악은 기존 `BgmManager`와 같은 공통 설정을 사용하므로 로비 BGM 패널의 볼륨과 양방향으로 동기화됩니다. BGM 전환/페이드 기능은 그대로 유지합니다.

현재 프로젝트에는 BGM 외에 효과음 재생 시스템이 없습니다. 새 효과음을 만들 때 **효과음 AudioSource 오브젝트에 `SoundEffectVolumeBinding`을 추가**합니다. 이 컴포넌트가 개별 소스의 기본 볼륨 × 공통 효과음 볼륨을 적용합니다. 기본값은 원래 AudioSource의 볼륨을 사용하며, `Use Initial Source Volume`을 끄면 `Base Volume`을 직접 지정할 수 있습니다. 음악 AudioSource에는 이 컴포넌트를 붙이지 않습니다. 최종 출력에는 마스터 볼륨도 함께 적용됩니다.

감도 기본값 50%는 1배로 기존 조준과 동일합니다. 1%는 0.02배, 100%는 2배의 마우스 이동량을 전투 조준에 적용합니다. 일반 UI 커서, 운영체제의 커서 설정, 패드 입력은 변경하지 않습니다. 보정된 조준 위치는 실제 마우스 커서 위치와 다를 수 있습니다. 입력 시스템의 마우스 델타로 가상 조준 포인터를 이동시키며, 한 프레임에 여러 번 조준 좌표를 요청해도 델타가 중복 적용되지 않습니다. 포커스를 잃었다 돌아오거나 일시정지를 해제하면 조준 포인터를 다시 기준화합니다.

## 저장과 확인

공통 설정은 `GameSettings`가 관리하고 `Shizuku.Settings.` 접두사의 PlayerPrefs로 저장합니다. 씬을 이동해도 유지되며 설정창 비활성화 또는 게임 종료 시 디스크에 저장합니다. 별도 저장 버튼은 필요하지 않습니다. 설정창 활성화 시 저장된 값을 UI에 다시 반영합니다.

## 설정 버튼과 ESC로 여닫기

1. Canvas 또는 UI 관리 오브젝트 등 **항상 활성화된 오브젝트**에 `SettingsWindowController`를 추가합니다. 설정창 자체나 설정창의 자식에는 붙이지 않습니다.
2. `Settings Button`에 기존 UI의 설정 버튼을 할당합니다.
3. `Settings Panel`에 `SettingsPanelController`가 붙어 있는 설정창 루트 GameObject를 할당합니다.
4. `Start Open`은 기본 꺼짐입니다. 실행 시 설정창은 닫혀 있고, 체크하면 열린 상태로 시작합니다.
5. `Toggle With Escape`는 기본 켜짐입니다. 버튼 클릭 또는 ESC를 누를 때마다 열림/닫힘이 전환됩니다. ESC를 계속 누르고 있어도 반복 토글하지 않습니다.

설정 버튼은 설정창 밖에 배치해야 닫힌 상태에서도 클릭할 수 있습니다. 버튼의 `On Click`에 `Toggle`을 별도로 추가하지 않습니다. 컴포넌트가 자동으로 연결하므로 수동 연결하면 중복 토글됩니다. 다른 코드에서는 공개된 `Toggle()`, `Open()`, `Close()`, `SetOpen(bool)`을 호출할 수 있습니다.

이 컨트롤러는 설정창 루트의 `SetActive`를 조절합니다. 설정창이 꺼져도 컨트롤러는 살아 있으므로 ESC로 다시 열 수 있으며, 여닫기 컨트롤러가 비활성화되면 입력을 해제하고 설정창도 닫습니다. 닫을 때 설정은 저장됩니다. UAE/UAM/UA 애니메이션 연동과 게임 일시정지는 자동 적용하지 않습니다.

`SettingsPanelTests`에 기본값/저장/범위 제한, UI 동기화, 버튼 순환과 중복 이벤트 방지, 효과음 연동, BGM 설정 공유, 마우스 델타 및 중복 읽기, 설정 버튼/ESC 토글과 입력 컨트롤러 배치 검증 테스트를 추가했습니다. 전체 C# 컴파일은 확인했지만 Unity Test Runner 실행과 빌드 창/실제 조작 검증은 별도로 필요합니다.
