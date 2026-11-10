# Deformable Rig 2D

## 개요

Deformable Rig 2D는 일반 Unity `Sprite`에 사용자 정의 폴리곤 메시와 Bone
리깅을 적용하고, Bone 기반 2D 물리 시뮬레이션으로 Sprite를 변형하는
시스템이다.

리깅 데이터는 `SpriteRigAsset` ScriptableObject에 저장되며 특정 Sprite를
직접 참조하지 않는다. Scene의 `DeformableSpriteRenderer`가 Sprite와 Rig
Asset을 결합하여 런타임 메시를 생성하고 변형한다.

주요 용도:

- 머리카락, 치마, 리본, 망토와 같은 2차 움직임
- 관절형 캐릭터 파츠
- 물리 반응이 포함된 2D 장식물
- 폴리곤 기반 Sprite 굽힘과 비틀림

## 시스템 구성

### Sprite

일반 Unity `Sprite`를 사용한다. Sprite 자체에는 별도의 리깅 정보를
기록하지 않는다.

현재 권장 조건:

- Sprite Draw Mode: `Simple`
- 회전되지 않은 직사각형 Texture 영역
- Single 또는 Multiple Sprite
- 임의의 Pivot과 Pixels Per Unit

회전 Atlas Packing 및 Tight Packing은 지원하지 않는다.

### SpriteRigAsset

재사용 가능한 리깅 데이터를 저장하는 ScriptableObject다.

저장 정보:

- 정규화된 메시 정점
- UV
- 간선
- 삼각형 인덱스
- Stable ID를 사용하는 Bone 계층
- 정점당 최대 4개의 Bone Weight
- Bone별 기본 물리 설정

Sprite 참조는 저장하지 않는다. 따라서 같은 구조를 가진 여러 Sprite에
하나의 Rig Asset을 재사용할 수 있다.

### DeformableSpriteRenderer

Sprite와 SpriteRigAsset을 결합하는 Scene 컴포넌트다.

담당 기능:

- 런타임 Mesh 생성
- CPU Linear Blend Skinning
- Bone 물리 시뮬레이션
- Transform 애니메이션 바인딩
- Collider2D 환경 충돌
- SpriteRenderer 속성 동기화
- 런타임 리소스 생성과 해제

원본 GameObject에는 다음 두 컴포넌트만 있으면 된다.

```text
Character
├─ SpriteRenderer
└─ DeformableSpriteRenderer
```

생성 메시용 `MeshFilter`와 `MeshRenderer`는 숨겨진 비직렬화 자식
GameObject에 자동 생성된다.

리그 메시가 정상적으로 생성되면 원본 `SpriteRenderer`의 렌더링은
비활성화된다. 컴포넌트 비활성화, 삭제 또는 빌드 실패 시 원본
SpriteRenderer가 자동으로 복구된다.

### Sprite Rig Editor

다음 메뉴에서 전용 리깅 에디터를 연다.

```text
Window > 2D Rig > Sprite Rig Editor
```

에디터 모드:

- `Mesh`: 정점, 간선, 삼각형 및 자동 메시 생성
- `Bone`: Bone 생성, 이동, 회전, 길이와 계층 편집
- `Weight`: 자동 Weight, 수동 수치 편집, Paint 및 Mirror
- `Physics`: Bone별 물리 설정
- `Preview`: Scene 컴포넌트 연결 및 시뮬레이션 미리보기

Bone 모드 조작:

- Bone 몸통 드래그: Bone 시작점 이동
- 둥근 끝점 드래그: Bone 회전 및 길이 변경
- 자식 Bone 이동: 부모 Bone 좌표계를 기준으로 저장

## 좌표 체계

Rig 데이터는 정규화된 Rig 공간을 사용한다.

```text
왼쪽 아래: (0, 0)
오른쪽 위: (1, 1)
X+: 오른쪽
Y+: 위
회전+: 반시계 방향
```

Rig 좌표는 Sprite Pivot, PPU, Texture 크기 및 Transform Scale과 독립적이다.

Rig 좌표를 Sprite Local 좌표로 바꾸는 기본 공식:

```text
pixel = (rig.x * sprite.rect.width, rig.y * sprite.rect.height)
local = (pixel - sprite.pivot) / sprite.pixelsPerUnit
```

Flip X/Y도 이 변환 과정에서 반영된다.

## 기본 제작 과정

### 1. Rig Asset 생성

Project 창에서 다음 메뉴를 사용한다.

```text
Create > 2D Rig > Sprite Rig Asset
```

생성된 Asset Inspector에서 `Initialize Default Quad`를 실행하면 기본 메시,
Root Bone 및 Weight가 생성된다.

### 2. 메시 제작

Sprite Rig Editor의 `Mesh` 모드에서 다음 방법을 사용할 수 있다.

- `Generate Grid`: 균일한 Grid 메시 생성
- `Generate From Sprite Geometry`: Sprite Importer의 Geometry 재사용
- 정점 직접 이동
- 간선 추가와 삭제
- 반시계 방향 삼각형 추가

### 3. Bone 제작

`Bone` 모드에서 Root Bone을 추가하고 Child Bone을 연결한다.

예:

```text
Root
└─ Middle
   └─ Tip
```

각 Bone은 배열 인덱스와 별도로 Stable ID를 가진다. 컴포넌트 Override와
Transform Binding은 Stable ID를 사용한다.

### 4. Weight 생성

`Weight` 모드에서 `Generate Distance Weights`를 실행하면 정점과 Bone
선분 사이의 거리를 기준으로 Weight를 생성한다.

규칙:

- 정점당 최대 4개 Bone 영향
- Weight 합계는 1
- 가까운 Bone의 영향이 더 큼

### 5. Scene 연결

1. GameObject에 `SpriteRenderer`를 추가한다.
2. Sprite를 지정한다.
3. `DeformableSpriteRenderer`를 추가한다.
4. `Rig Asset`을 지정한다.
5. `Rebuild`를 실행한다.

Inspector에 다음과 유사한 문구가 표시되면 빌드에 성공한 것이다.

```text
Rig mesh ready: N vertices, N bones.
```

## Bone 시뮬레이션 모드

### Fixed

항상 Rig Asset에 저장된 Bind Pose를 사용한다.

- 중력과 외력의 영향을 받지 않는다.
- Target Pose 또는 수동 포즈로 대체되지 않는다.
- Root 고정점에 적합하다.

### Animated

외부 Target 또는 런타임 Pose를 그대로 따라간다.

- 힘 적분을 수행하지 않는다.
- Transform Binding으로 위치와 회전을 구동할 수 있다.
- Dynamic 자식의 움직이는 기준점으로 사용할 수 있다.

회전 Binding은 가산 방식이다.

```text
최종 Local Rotation
= Bone의 authored Local Rotation
+ Target의 Local Z Rotation
```

따라서 Target 회전이 0도이면 Bone의 원래 방향을 유지한다.

### Dynamic

Verlet 기반 물리 시뮬레이션을 수행한다.

지원 항목:

- Gravity
- External Force
- Mass
- Stiffness
- Damping
- Inertia
- 최소/최대 상대 각도
- 최대 각속도
- Bone 길이 제약
- Collider2D 환경 충돌

## 주요 물리 설정

### 전역 설정

- `Physics Enabled`: 자동 FixedUpdate 시뮬레이션
- `Gravity`: Rig 공간 중력
- `External Force`: 지속 외력
- `Stiffness Multiplier`: 전체 Bone 강성 배율
- `Damping Multiplier`: 전체 감쇠 배율
- `Inertia Multiplier`: 기준 Transform 움직임 관성 배율
- `Constraint Iterations`: 길이와 각도 제약 반복 횟수
- `Simulate When Offscreen`: 화면 밖 물리 계산 여부

### Bone 설정

- `Simulation Mode`
- `Mass`
- `Gravity Scale`
- `Stiffness`
- `Damping`
- `Inertia`
- `Minimum Angle`
- `Maximum Angle`
- `Maximum Angular Speed`
- `Collision Radius`
- `External Force Multiplier`

`Damping`은 속도 유지 비율에 가깝다. 값이 높을수록 더 오래 흔들린다.

## 기준 Transform과 순간이동

컴포넌트 Transform의 이동, 회전 및 Scale 변경은 이전 Verlet 상태에
재투영된다. 따라서 캐릭터가 움직이면 Dynamic Bone이 관성을 가지고
뒤늦게 따라온다.

큰 변화는 순간이동으로 판정하여 시뮬레이션을 초기화한다.

- `Teleport Distance Threshold`
- `Teleport Rotation Threshold`
- `Teleport Scale Ratio Threshold`

순간이동 초기화는 이전 위치까지 Bone이 길게 늘어나거나 메시가 폭발하는
현상을 방지한다.

## Transform Animation Binding

`Animation Bindings` 배열에서 Stable Bone ID와 Target Transform을 연결한다.

항목:

- `Bone ID`
- `Target`
- `Drive Position`
- `Drive Rotation`

대상 Bone의 Simulation Mode가 `Animated`일 때 적용된다.

Position은 Target의 월드 위치를 Bone 부모 좌표계로 변환한다. Rotation은
Target의 Local Z Rotation을 Bone의 authored rotation에 더한다.

## 2D 충돌

충돌은 Dynamic Bone과 외부 `Collider2D` 사이에서만 처리된다.

### Endpoint

Bone 끝점에 원형 충돌 범위를 적용한다.

- 가벼운 계산
- 머리카락 끝이나 장식 끝에 적합
- 빠른 이동에는 CircleCast를 사용하여 관통을 줄임

### Capsule

Bone 시작점부터 끝점까지 Capsule 범위를 적용한다.

- Bone 중간 부분도 충돌
- 긴 머리카락, 끈 또는 천 조각에 적합

### 충돌 설정

- `Collision Enabled`
- `Collision Shape`
- `Collision Layers`
- `Include Trigger Colliders`
- `Collision Radius Multiplier`
- `Collision Friction`
- `Collision Bounciness`
- `Collision Skin Width`
- `Collision Iterations`

### 충돌의 설계 경계

Bone 충돌은 시각적 2차 물리에만 영향을 준다.

```text
Bone collision
→ Dynamic Bone 상태 보정
→ 변형 메시 갱신
```

다음 대상으로는 역전파하지 않는다.

- Rigidbody2D
- 리그 GameObject Transform
- 부모 GameObject

따라서 Bone이 바닥에 충돌하더라도 캐릭터 본체의 Rigidbody2D를 멈추거나
밀지 않는다. 캐릭터 본체의 이동과 바닥 충돌은 별도의 본체 Collider2D가
담당해야 한다.

Self Collision도 현재 지원하지 않는다.

## 렌더링

생성 Mesh는 전용 투명 Unlit 셰이더를 기본 사용한다.

```text
ShizukuCity/Sprite Physics Rig Unlit
```

지원 내용:

- Sprite Texture
- Sprite Color
- Sprite Flip X/Y
- Sorting Layer와 Order
- 양면 렌더링
- 알파 블렌딩

별도 Material이 필요하면 `Generated Material Override`를 사용한다.

완전한 URP 2D Lit, Normal Map, SpriteMask 호환은 현재 범위 밖이다.

## 런타임 API

주요 API:

```csharp
renderer.Rebuild();
renderer.ResetToBindPose();
renderer.ResetSimulation();
renderer.SimulatePhysics(deltaTime);
renderer.RefreshCollisionIgnoreList();
```

Bone 포즈:

```csharp
renderer.SetBoneLocalPose(boneIndex, localPosition, localRotation);
renderer.SetBoneLocalPose(stableBoneId, localPosition, localRotation);
```

Bone 물리:

```csharp
renderer.SetBonePhysicsSettings(boneIndex, settings);
renderer.SetBonePhysicsSettings(stableBoneId, settings);
renderer.TryGetBonePhysicsSettings(boneIndex, out settings);
```

진단:

```csharp
DeformableSpriteRenderer.RuntimeDiagnostics diagnostics = renderer.Diagnostics;
```

제공 정보:

- 누적 시뮬레이션 Step
- 최근 순간이동 초기화 여부
- 최근 충돌 Query 수
- 최근 충돌 보정 수

## 오류 및 복구 정책

다음 상황에서는 리깅 메시 빌드를 중단하고 원본 SpriteRenderer를 복구한다.

- Sprite가 없음
- Rig Asset이 없음
- Rig 검증 실패
- 지원하지 않는 Sprite Draw Mode
- 회전 Atlas Packing
- Tight Packed Atlas
- UV 생성 실패

컴포넌트 비활성화 또는 삭제 시에도 원본 SpriteRenderer가 활성화되고
런타임 Mesh, Material, Collision Probe 및 숨겨진 렌더러가 정리된다.

## 검증 상태

Unity 6000.0.32f1 환경에서 다음 핵심 기능을 수동 검증했다.

- Rig 데이터와 자동 Weight
- 런타임 메시 생성
- Scene 및 Game 뷰 Sprite 출력
- 단일/다중 Bone 폴리곤 변형
- Fixed, Animated, Dynamic 모드
- 가산형 Animated Target 회전
- 이동, 회전, Scale 관성
- 순간이동 초기화
- Endpoint 충돌
- Capsule 충돌
- Flip X/Y
- 컴포넌트 비활성화 시 원본 복구
- Rig Asset 제거 시 안전한 실패와 원본 복구

자동 EditMode 테스트는 현재 26개가 통과한다.

```text
ShizukuCity.SpritePhysicsRig.Tests
26 passed
0 failed
```

낮은 우선순위로 남겨둔 수동 검증:

- 다양한 Pivot 조합
- 다양한 Pixels Per Unit 조합
- Weight Paint와 Mirror의 세부 UX
- 장시간 Play Mode 성능 프로파일링

## 현재 지원하지 않는 기능

- Bone Self Collision
- Bone 충돌의 Rigidbody2D 역전파
- GPU Skinning
- IK
- 에디터 내 Animation 제작
- Runtime Topology 변경
- 메시 절단과 찢기
- Network Deterministic Physics
- 완전한 SpriteMask 지원
- 완전한 URP 2D Lighting 및 Secondary Texture 지원

## 주요 구현 경로

```text
Assets/SpritePhysicsRig/
├─ Runtime/
│  ├─ Data/
│  ├─ Physics/
│  ├─ Rendering/
│  └─ Skinning/
├─ Editor/
├─ Tests/Editor/
└─ DESIGN.md
```

핵심 클래스:

- `SpriteRigAsset`
- `CpuSpriteSkinner`
- `BonePhysicsSolver`
- `RigCollisionSolver2D`
- `DeformableSpriteRenderer`
- `SpriteRigEditorWindow`

