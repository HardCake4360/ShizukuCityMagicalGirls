# 캐릭터별 transformCutinCircle 시작 위치

기존 `LobbyStandingCharacterController`의 캐릭터 설정 목록에 컷인 시작 위치를 추가했습니다. 캐릭터/스탠딩 이미지/변신 클립을 설정한 동일한 목록에서 관리합니다.

## 설정 방법

1. 스탠딩 오브젝트의 `LobbyStandingCharacterController`를 선택합니다.
2. `Transformation Cut-in > Transformation Cutin Circle`에 `SceneAnimator > Canvas > transformCutinCircle`의 RectTransform을 할당합니다.
3. 컷인 오브젝트를 원하는 시작 위치로 옮깁니다.
4. 스탠딩 컨트롤러의 `Characters` 목록에서 해당 캐릭터 항목을 펼치고 **SetPositon** 버튼을 누릅니다.
5. 다른 캐릭터도 위치를 옮긴 뒤 해당 항목의 버튼을 눌러 각각 저장합니다. 씬 또는 프리팹을 저장합니다.

버튼은 연결된 컷인 오브젝트의 현재 `anchoredPosition3D`를 `Cutin Start Position`에 저장하며 `Use Cutin Start Position`도 자동으로 켭니다. 직접 숫자를 수정할 수도 있습니다. 위치만 저장/적용하며 회전, 배율, 크기, 알파는 변경하지 않습니다. 캡처는 Undo와 프리팹 오버라이드를 지원합니다. 컷인 오브젝트를 할당하기 전에는 버튼이 비활성화됩니다.

`Use Cutin Start Position`의 기본값은 꺼짐으로, 기존 캐릭터/씬 설정을 변경하지 않습니다. 꺼져 있는 캐릭터는 현재 컷인 오브젝트 위치를 그대로 사용합니다. Play Mode에서 저장한 설정은 Unity의 일반적인 런타임 편집과 같이 플레이 종료 시 복원되므로, 영구 설정은 Edit Mode에서 저장합니다.

## 재생 동작

출격 캐릭터를 확정하고 변신 전 클립을 재생한 뒤, 설정된 지연 시간 후 `transformCutin`을 재생하기 직전에 해당 캐릭터의 컷인 위치를 적용합니다. 출격 UI가 닫혀 커서 포커스가 사라져도 이미 확정된 캐릭터의 위치를 사용합니다. 변신/컷인/화이트아웃/전투 씬 로딩 순서는 바꾸지 않습니다.

현재 컷인 클립은 원의 크기/알파와 내부 효과만 애니메이션하고 원 자체의 위치에는 키가 없습니다. 향후 클립에 `Canvas/transformCutinCircle`의 위치 키를 추가하면 설정 위치를 덮어쓸 수 있으므로 루트 위치는 코드로 유지하고 내부 효과만 애니메이션하는 구성을 유지합니다.
