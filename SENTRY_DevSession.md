# SENTRY — 개발 세션 전체 기록

> **마지막 갱신:** 2026-05-14  
> **엔진:** Unity (C#) · DOTween · Cinemachine  
> **네임스페이스:** `SENTRY`

---

## 🎮 게임 프롬프트 (기획 원문)

> 소환수 키우기 게임 POC  
> DOTween을 적극 사용해서 모션 연출을 만들어도 된다.  
> 매트로 배니아 형식의 게임 기준으로 3가지 타입의 센트리를 키워 나아가야 한다.

### 센트리 종류
1. **타격 센트리** — 근접 물리 공격 담당
2. **사격 센트리** — 원거리 탄환 공격 담당
3. **벽 센트리** — 방어·압박·기절 담당

### 필드 구성
- **탐색 필드 (Exploration Field):** 2D 사이드뷰 횡스크롤 형식의 매트로배니아 맵
- **배틀 필드 (Battle Field):** 2.5D 탑다운 쿼터뷰 자동 전투 공간
- 레퍼런스: 포켓몬스터, 디지몬, 몬스터 생츄어리 등

### 기본 규칙
- 센트리는 영구 소환되어 플레이어를 따라다닌다
- 플레이어가 적에게 닿으면 배틀 필드로 전환 → 오토 배틀 시작 (소환수끼리의 싸움)
- 각 센트리에는 **체력(HP)** 과 **경험치(EXP)** 가 존재하며 레벨에 따라 강화된다
- 센트리 HP가 0이 되면 일부 쉼터 구간에서 체력 회복으로 부활 가능

### 플레이어 특수 능력 (초기 사용 대기시간 존재)
| # | 능력 | 설명 |
|---|------|------|
| 1 | 목표 지정 | 플레이어가 목표를 지정하면 해당 캐릭터를 우선 공격 |
| 2 | 긴급 수리 | 전투 중인 센트리 한 기의 HP 일부 회복 |
| 3 | 과부하 | 센트리 한 기를 폭주 상태로 만듦 (폭주 종료 후 기절 상태 돌입) |

### 배틀 필드 시스템
- 센트리 기본 공격으로 적과 싸운다
- **스킬 게이지:** 게이지가 차면 센트리 고유 스킬 발동 (연출 필수)
- **콤보 게이지:** 살아 있는 센트리끼리 연계 콤보 발동. 콤보 중 무적 상태

#### 개인 스킬 연출 예시
| 센트리 | 스킬 |
|--------|------|
| 타격 | 적을 2연타 공격 |
| 사격 | 적에게 3연발 사격 |
| 벽   | 강한 밀치기로 적을 2~3초 기절 |

#### 콤보 게이지 쿨타임
- 3 센트리 콤보: **1분** (우선권 가짐)
- 2 센트리 콤보: **20~30초**

#### 2 센트리 콤보 예시
| 조합 | 연출 |
|------|------|
| 타격 + 사격 | 타격 센트리가 사격 센트리의 실린더를 쳐 매우 빠른 탄을 발사 |
| 타격 + 벽   | 타격 센트리가 벽 센트리를 쳐내 배틀 필드 벽까지 밀어 압착 공격 |
| 사격 + 벽   | 적을 사격하고 벽 센트리가 하늘에서 낙하 공격 |

#### 3 센트리 콤보
> 타격 센트리가 적을 벽 센트리에게 날려 벽 센트리가 붙잡고,  
> 사격 센트리가 무수한 연발 공격을 퍼붓는다.

---

## 📁 프로젝트 파일 구조

```
Assets/HTH/
├── Manager/
│   ├── GameManager.cs          — 게임 전체 흐름 (씬 전환, 게임오버/클리어)
│   ├── FieldManager.cs         — 탐색↔배틀 필드 전환 + Cinemachine VCam 제어
│   ├── BattleManager.cs        — 배틀 시작/종료, 킬 카운트 판정
│   ├── BattleTrigger.cs        — 탐색 필드 적 배회 + 플레이어 접촉 시 배틀 트리거
│   ├── ComboManager.cs         — 2/3 센트리 콤보 연계 시스템
│   ├── EnemySpawner.cs         — BattleEncounterDataSO 기반 적 순차 소환
│   └── UIManager.cs            — HUD (센트리 HP/EXP, 콤보 게이지, 킬 카운트)
│
├── Sentry/
│   ├── SentryType/
│   │   ├── SentryBase.cs       — 모든 센트리의 공통 기반 클래스
│   │   ├── StrikeSentry.cs     — 타격 센트리
│   │   ├── ShootSentry.cs      — 사격 센트리
│   │   └── WallSentry.cs       — 벽 센트리
│   └── Effect/
│       ├── SkillEffectStrike.cs  — 타격 스킬 2연타 연출
│       ├── SkillEffectShoot.cs   — 사격 스킬 3연발 + 레이저 조준선 연출
│       └── SkillEffectWall.cs    — 벽 스킬 강한 밀치기 + 기절 연출
│
├── Player/
│   └── PlayerAbility.cs        — 플레이어 특수 능력 (목표 지정·긴급 수리·과부하)
│
└── DATA/
    ├── BattleEncounterDataSO.cs — 배틀 인카운터 구성 ScriptableObject
    └── SentryGrowthDataSO.cs   — 센트리 레벨업 성장 데이터 ScriptableObject
```

---

## 🗺️ 씬 히어라키 구성

```
[Scene Root]
├── --- Managers ---
│   ├── GameManager
│   ├── FieldManager        ← 탐색↔배틀 전환 총괄
│   ├── BattleManager
│   ├── ComboManager
│   └── UIManager
│
├── Main Camera             ← CinemachineBrain 부착
├── ExplorationVirtualCamera  ← 탐색용 VCam (Follow/LookAt: Player)
└── BattleVirtualCamera       ← 배틀용 VCam (고정 쿼터뷰 앵글)
│
├── ExplorationField        ← 2D 사이드뷰 루트 (평소 활성)
│   ├── Player
│   ├── Sentries
│   │   ├── StrikeSentry
│   │   ├── ShootSentry
│   │   └── WallSentry
│   └── EncounterEnemy_N   ← BattleTrigger 부착
│
└── BattleField             ← 2.5D 쿼터뷰 루트 (평소 비활성)
    ├── EnemySpawner
    │   └── SpawnPoint_1~N
    └── SentryBattleSpawnPoints
        ├── StrikeSpawn
        ├── ShootSpawn
        └── WallSpawn
```

---

## 🏗️ 핵심 아키텍처

### 필드 전환 흐름 (FieldManager)

```
[탐색 필드 — 2D 사이드뷰]
        │
        │  BattleTrigger.OnTriggerEnter2D (Player 접촉)
        │    1. FieldManager.SaveReturnPositions()   ← 현재 위치 저장
        │    2. FieldManager.EnterBattle()
        │    3. BattleManager.StartBattle()
        ▼
    EnterBattleRoutine()
        1. 페이드 아웃 (DOTween)
        2. ExplorationFieldRoot → SetActive(false)
           BattleFieldRoot      → SetActive(true)
        3. SetCameraToBattle()  ← BattleVCam Priority UP
        4. Player → BattlePlayerSpawnPoint 이동
        5. 페이드 인 (DOTween)
        │
        ▼
[배틀 필드 — 2.5D 쿼터뷰]
        │
        │  BattleManager.EndBattle(isVictory: true)
        │    → FieldManager.ReturnToField()
        ▼
    ReturnToFieldRoutine()
        1. 페이드 아웃
        2. BattleFieldRoot      → SetActive(false)
           ExplorationFieldRoot → SetActive(true)
        3. SetCameraToExploration() ← ExplorationVCam Priority UP
        4. Player / 센트리 → SavedPosition 복귀
        5. 페이드 인
        │
        ▼
[탐색 필드 복귀]
```

### 카메라 전환 방식 (Cinemachine Priority)

| 상태 | ExplorationVCam Priority | BattleVCam Priority | 활성 카메라 |
|------|-------------------------|---------------------|-------------|
| 탐색 중 | **10** (높음) | 9 (낮음) | ExplorationVCam |
| 배틀 중 | 19 (낮음) | **20** (높음) | BattleVCam |

> CinemachineBrain이 Priority가 높은 VCam을 자동으로 추종하며 블렌드 전환을 처리한다.

### 센트리 모드별 동작

| 항목 | 탐색 필드 (2D 사이드뷰) | 배틀 필드 (2.5D 쿼터뷰) |
|------|------------------------|------------------------|
| 이동 방식 | `Rigidbody2D.linearVelocity` | 전투 AI 자율 이동 |
| 추종 대상 | 플레이어 포메이션 오프셋 | `SentryBattleSpawnPoints[i]` |
| 제어 주체 | `SentryBase.FollowPlayer()` | `StopFollowing()` 후 AI |
| 좌표계 | 2D XY (횡스크롤) | 2.5D XZ+Y (쿼터뷰 원근) |

---

## 🧩 주요 클래스 설명

### SentryBase.cs
모든 센트리의 공통 기반. 상속 구조:

```
SentryBase (MonoBehaviour)
  ├── StrikeSentry
  ├── ShootSentry
  └── WallSentry
```

주요 공개 메서드:
- `StartFollowing()` / `StopFollowing()` — 플레이어 추종 ON/OFF
- `SetInvincible(bool)` — 콤보 중 무적 상태 제어
- `TakeDamage(int)` — 데미지 수신
- `LevelUp()` — 레벨업 처리 (override 가능)
- `AddExp(int)` — 경험치 추가

### FieldManager.cs (싱글턴)
```csharp
// 위치 저장 (배틀 트리거 접촉 시점)
FieldManager.Instance.SaveReturnPositions(player, strike, shoot, wall);

// 배틀 진입
FieldManager.Instance.EnterBattle(player);

// 탐색 복귀
FieldManager.Instance.ReturnToField(strike, shoot, wall);
```

Inspector 연결 필수 항목:
- `_explorationFieldRoot` — ExplorationField 루트 오브젝트
- `_battleFieldRoot` — BattleField 루트 오브젝트
- `_explorationVCam` — ExplorationVirtualCamera
- `_battleVCam` — BattleVirtualCamera
- `_battlePlayerSpawnPoint` — 배틀 진입 시 플레이어 스폰 위치
- `_fadePanel` — 전환 연출용 CanvasGroup

### ComboManager.cs (싱글턴)
콤보 종류:

| ID | 조합 | 연출 |
|----|------|------|
| 2콤보 A | 타격 + 사격 | 타격 → 사격 실린더 타격 → 관통탄 발사 |
| 2콤보 B | 타격 + 벽 | 타격 → 벽 센트리 날림 → 고속 돌진 압착 |
| 2콤보 C | 사격 + 벽 | 연속 사격 → 벽 센트리 하늘에서 낙하 압착 |
| 3콤보 | 전원 | 타격이 적을 날림 → 벽이 붙잡음 → 사격 연발 → 최종 밀치기 |

콤보 공통 흐름:
```
1. 콤보 전 위치 저장 (복귀용)
2. SetInvincible(true) + StopFollowing()
3. CalcComboPositions(target) → 동적 포지션 계산
4. 각 센트리 DOMove → 집결
5. 연출 실행 (DOTween Sequence)
6. ReturnFromCombo() → 콤보 전 위치 복귀
7. SetInvincible(false) + StartFollowing()
```

### BattleEncounterDataSO.cs (ScriptableObject)
```
Project 우클릭 → Create → SENTRY → BattleEncounterData
```
- `encounterName` — 인카운터 식별 이름
- `killCountToWin` — 클리어 처치 수 (0이면 전체 적 수)
- `spawnEntries` — `EnemySpawnEntry[]` (프리팹, 수량, 딜레이, 간격)

---

## 🐛 트러블슈팅 / 확인 사항

### 배틀 필드 진입 후 여전히 2D 사이드뷰로 보이는 문제

**원인 후보:**

1. **Inspector VCam 미연결** (가장 흔한 원인)
   - `FieldManager` Inspector에서 `_explorationVCam`, `_battleVCam` 슬롯이 비어있으면 null 체크로 그냥 넘어감
   - `SetCameraToBattle()`이 호출돼도 실제 Priority 변경이 일어나지 않음

2. **BattleVCam 물리적 위치가 사이드뷰 앵글**
   - Priority는 전환됐어도 BattleVCam 자체가 옆에서 찍는 위치에 있으면 동일하게 보임
   - BattleVCam을 쿼터뷰 앵글 (위에서 비스듬히 내려다보는 위치)에 배치해야 함

3. **Priority 값 역전**
   - `_explorationCamPriority = 10`, `_battleCamPriority = 20` 이어야 함
   - 배틀 Priority가 탐색보다 높아야 BattleVCam이 활성화됨

**체크리스트:**
```
□ FieldManager._explorationVCam → ExplorationVirtualCamera 드래그 연결
□ FieldManager._battleVCam      → BattleVirtualCamera 드래그 연결
□ BattleVirtualCamera 위치: 쿼터뷰 앵글로 배치 (Y높이 + 약간 기울임)
□ _explorationCamPriority = 10, _battleCamPriority = 20 확인
□ BattleFieldRoot 연결 및 하위에 쿼터뷰 배틀 씬 오브젝트 배치
□ Console 로그: "[FieldManager] 배틀 필드 전환 완료 (2D → 2.5D 쿼터뷰)" 확인
```

---

## 📋 세션별 작업 내역

### Session 1 — 프로젝트 기초 설계
- 게임 컨셉 확정 (소환수 키우기 POC)
- 센트리 3종 타입 정의
- 탐색 필드 / 배틀 필드 이원 구조 설계
- `SentryBase`, `StrikeSentry`, `ShootSentry`, `WallSentry` 기본 골격 작성
- `GameManager` 싱글턴 구조 확립

### Session 2 — 필드 전환 시스템
- `FieldManager` 작성 (탐색 ↔ 배틀 전환, 페이드 연출)
- `BattleTrigger` 작성 (배회 AI, 플레이어 접촉 감지)
- Cinemachine Priority 방식 VCam 전환 구현
- `SaveReturnPositions()` 로직 (배틀 종료 후 원위치 복귀)

### Session 3 — 배틀 시스템 기초
- `BattleManager` 작성 (배틀 시작/종료, 킬 카운트)
- `EnemySpawner` 작성 (순차 소환 루틴)
- `BattleEncounterDataSO` ScriptableObject 구조 설계
- `Enemy.Stun()` 기절 메서드 추가 (WallSentry 스킬 TODO 완성)
- `SentryGrowthDataSO` 레벨업 성장 데이터 분리

### Session 4 — 스킬 연출 & 콤보 시스템
- `SkillEffect_Strike` — 2연타 연출 (DOTween Sequence)
- `SkillEffect_Shoot` — 3연발 + 레이저 조준선 + 총구 플래시 (LineRenderer)
- `SkillEffect_Wall` — 강한 밀치기 + 기절 (Enemy.Stun() 연동)
- `ComboManager` 작성
  - 2콤보 A (타격+사격): 관통탄 발사
  - 2콤보 B (타격+벽): 고속 돌진 압착
  - 2콤보 C (사격+벽): 하늘 낙하 압착
  - 3콤보 (전원 연계): 날리기 → 붙잡기 → 연발 → 최종 밀치기
- 콤보 포지션 동적 계산 (`CalcComboPositions`) — Inspector 고정 Transform 제거
- `PlayerAbility` 특수 능력 3종 구현 (목표 지정, 긴급 수리, 과부하)
- `UIManager` 전체 HUD 연동 (HP바, EXP바, 스킬 게이지, 콤보 게이지)

### Session 5 (현재) — 필드 카메라 구조 검토
- 3종 센트리의 2D / 2.5D 모드 분리 구조 재확인
- 배틀 진입 시 2D 사이드뷰로 보이는 문제 원인 분석
- `FieldManager` VCam 연결 및 Priority 체크리스트 정리
- **ExplorationVCam / BattleVCam 2개 분리 필수** 결론 확정
  - ExplorationVCam: Follow/LookAt Player, 횡스크롤 추종 카메라
  - BattleVCam: 고정 쿼터뷰 앵글, 배틀 필드 전체 촬영

---

## 📌 현재 미완성 / 다음 세션 TODO

- [ ] BattleVCam Inspector 연결 확인 및 쿼터뷰 앵글 배치
- [ ] 쉼터 구간 센트리 HP 회복 / 부활 시스템 구현
- [ ] 과부하 폭주 상태 연출 (`PlayerAbility` Ability3)
- [ ] 센트리 기절(KO) 후 배틀 필드 내 비활성 처리
- [ ] 레벨업 연출 강화 (UIManager.PlayLevelUpEffect 확장)
- [ ] 적 AI 다양화 (현재 기본 추적 AI만 존재)
- [ ] 배틀 결과 화면 (Victory / Defeat 연출)

---

## 🔧 코딩 컨벤션

| 항목 | 규칙 |
|------|------|
| 변수명 | `_camelCase` (언더스코어 접두사) |
| 접근 제한자 | `[SerializeField] private` / `public` 명시 |
| 주석 | 모든 함수·변수에 `/// <summary>` XML 문서 주석 |
| 네임스페이스 | `SENTRY` 통일 |
| 싱글턴 | `public static T Instance { get; private set; }` 패턴 |
| DOTween | 모션 연출 전반 (이동, 페이드, 펀치, 쉐이크) |
| Cinemachine | `CinemachineCamera` (Unity.Cinemachine) Priority 방식 |
