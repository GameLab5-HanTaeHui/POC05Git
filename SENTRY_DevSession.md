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
- 2 센트리 콤보: **20~30초** (A/B/C 개별 쿨타임)

#### 2 센트리 콤보 예시
| 조합 | 연출 |
|------|------|
| 타격 + 사격 (A) | 타격 센트리가 사격 센트리의 실린더를 쳐 매우 빠른 탄을 발사 |
| 타격 + 벽 (B)   | 타격 센트리가 벽 센트리를 쳐내 배틀 필드 벽까지 밀어 압착 공격 |
| 사격 + 벽 (C)   | 적을 사격하고 벽 센트리가 하늘에서 낙하 공격 |

#### 3 센트리 콤보
> 타격 센트리가 적을 벽 센트리에게 날려 벽 센트리가 붙잡고,
> 사격 센트리가 무수한 연발 공격을 퍼붓는다.

---

## 📁 프로젝트 파일 구조

```
Assets/HTH/
├── Manager/
│   ├── GameManager.cs              — 게임 전체 흐름 (씬 전환, 게임오버/클리어)
│   ├── FieldManager.cs             — 탐색↔배틀 필드 전환 + Cinemachine VCam 제어
│   ├── BattleManager.cs            — 배틀 시작/종료, 킬 카운트 판정
│   ├── BattleTrigger.cs            — 탐색 필드 적 배회 + 플레이어 접촉 시 배틀 트리거
│   ├── SentryComboManager.cs       — 2/3 센트리 콤보 연계 시스템 (구 ComboManager)
│   ├── EnemyComboManager.cs        — 적 콤보 순번 AI 제어 (구 EnemyComboGroup)
│   ├── EnemySpawner.cs             — BattleEncounterDataSO 기반 적 순차 소환
│   ├── BattleUIManager.cs          — UI 조율자 (구 UIManager)
│   ├── PlayerBattleUIManager.cs    — 센트리 HUD / 능력 HUD / 콤보 HUD 전담
│   └── EnemyBattleUIManager.cs     — 적 캐릭터 HUD 전담 (3슬롯 고정)
│
├── UI/
│   └── BattleUIButton.cs           — IPointer 호버/클릭 연출 버튼 컴포넌트
│
├── Sentry/
│   ├── SentryType/
│   │   ├── SentryBase.cs           — 모든 센트리의 공통 기반 클래스
│   │   ├── StrikeSentry.cs         — 타격 센트리
│   │   ├── ShootSentry.cs          — 사격 센트리
│   │   └── WallSentry.cs           — 벽 센트리
│   └── Effect/
│       ├── SkillEffectStrike.cs    — 타격 스킬 2연타 연출
│       ├── SkillEffectShoot.cs     — 사격 스킬 3연발 + 레이저 조준선 연출
│       └── SkillEffectWall.cs      — 벽 스킬 강한 밀치기 + 기절 연출
│
├── Enemy/
│   └── Enemy.cs                    — 적 캐릭터 (Level 프로퍼티 추가, HP바 UI 제거)
│
├── Player/
│   └── PlayerAbility.cs            — 플레이어 특수 능력 (목표 지정·긴급 수리·과부하)
│
└── DATA/
    ├── BattleEncounterDataSO.cs    — 배틀 인카운터 구성 SO (comboCount 필드 추가)
    └── SentryGrowthDataSO.cs       — 센트리 레벨업 성장 데이터 ScriptableObject
```

---

## 🗺️ 씬 히어라키 구성

```
[Scene Root]
├── --- Managers ---
│   ├── GameManager
│   ├── FieldManager            ← 탐색↔배틀 전환 총괄
│   ├── BattleManager
│   ├── SentryComboManager      ← 구 ComboManager
│   ├── EnemyComboManager       ← 적 콤보 순번 AI
│   ├── BattleUIManager         ← 구 UIManager (조율자)
│   ├── PlayerBattleUIManager   ← 센트리 HUD 전담
│   └── EnemyBattleUIManager    ← 적 HUD 전담
│
├── Main Camera                 ← CinemachineBrain 부착
├── ExplorationVirtualCamera    ← 탐색용 VCam (Follow/LookAt: Player)
└── BattleVirtualCamera         ← 배틀용 VCam (고정 쿼터뷰 앵글)
│
├── ExplorationField            ← 2D 사이드뷰 루트 (평소 활성)
│   ├── Player
│   ├── Sentries
│   │   ├── StrikeSentry
│   │   ├── ShootSentry
│   │   └── WallSentry
│   └── EncounterEnemy_N        ← BattleTrigger 부착
│
└── BattleField                 ← 2.5D 쿼터뷰 루트 (평소 비활성)
    ├── EnemySpawner
    │   └── SpawnPoint_1~N
    ├── EnemyComboManager
    └── SentryBattleSpawnPoints
        ├── StrikeSpawn
        ├── ShootSpawn
        └── WallSpawn

Canvas (Screen Space - Overlay)
  ├── SentryHUDPanel            ← PlayerBattleUIManager 관할
  ├── AbilityHUDPanel           ← PlayerBattleUIManager 관할
  ├── EnemyHUDPanel             ← EnemyBattleUIManager 관할
  │     ├── EnemySlot1
  │     ├── EnemySlot2
  │     └── EnemySlot3
  ├── EncounterPanel            ← BattleUIManager 관할 (기본 비활성)
  ├── VictoryPanel              ← BattleUIManager 관할 (기본 비활성)
  └── DefeatPanel               ← BattleUIManager 관할 (기본 비활성)
```

---

## 🏗️ 핵심 아키텍처

### 필드 전환 흐름 (FieldManager)

```
[탐색 필드 — 2D 사이드뷰]
        │
        │  BattleTrigger.OnTriggerEnter2D (Player 접촉)
        │    1. EncounterPanel 팝업 → 전투/도망 선택
        │    2. [전투] 선택 시 FieldManager.SaveReturnPositions()
        │    3. FieldManager.EnterBattle()
        │    4. BattleManager.StartBattle()
        ▼
    EnterBattleRoutine()
        1. 페이드 아웃 (DOTween)
        2. ExplorationFieldRoot → SetActive(false)
           BattleFieldRoot      → SetActive(true)
        3. SetCameraToBattle()  ← BattleVCam Priority UP
        4. Player → BattlePlayerSpawnPoint 이동
        5. _blackoutHoldDuration 유지
        6. BattleUIManager.SetBattleHudActive(true) → SlideIn
        7. 페이드 인 (DOTween)
        │
        ▼
[배틀 필드 — 2.5D 쿼터뷰]
        │
        │  BattleManager.EndBattle(isVictory)
        │    → isVictory=true  : BattleUIManager.ShowVictoryPanel()
        │    → isVictory=false : BattleUIManager.ShowDefeatPanel()
        │    → 버튼 클릭 시 ReturnToFieldFromResult()
        ▼
    ReturnToFieldRoutine()
        1. BattleUIManager.SetBattleHudActive(false) → SlideOut
        2. 페이드 아웃
        3. BattleFieldRoot      → SetActive(false)
           ExplorationFieldRoot → SetActive(true)
        4. SetCameraToExploration() ← ExplorationVCam Priority UP
        5. Player / 센트리 → SavedPosition 복귀
        6. 페이드 인
        │
        ▼
[탐색 필드 복귀]
```

### UI 매니저 분리 구조

```
BattleUIManager (조율자)
  │  SetBattleHudActive(true/false)
  ├──→ PlayerBattleUIManager.SlideIn() / SlideOut()
  └──→ EnemyBattleUIManager.SlideIn() / SlideOut()

PlayerBattleUIManager
  ├── 센트리 3종 HP바 / 스킬 게이지 / 레벨 / EXP바 / KO아이콘
  ├── 플레이어 능력 쿨타임 3개
  └── 콤보 게이지 / 2콤보 아이콘(슬롯별 3개) / 3콤보 아이콘(슬롯별 3개)

EnemyBattleUIManager
  ├── 적 슬롯 1~3 (소환 수에 따라 활성/비활성)
  │     각 슬롯: 이름 텍스트 / 레벨 텍스트 / HP바 / KO아이콘
  └── RegisterEnemy() / OnEnemyDied() / ClearAllSlots()
```

### SentryComboManager 큐 시스템

```
게이지 만참 → TryEnqueueCombo()
  ├── 3기 생존 + 3콤보 쿨타임 OK → AllThree 큐 추가
  └── 그 외 → 가능한 2콤보 조합 수집 후 랜덤 1개 선택
        candidates: StrikeShoot / StrikeWall / ShootWall (쿨타임 OK인 것만)
        → Random.Range로 1개 Enqueue

콤보 재생 중이 아니면 즉시 TryDequeueAndPlay()
콤보 재생 중이면 큐 대기 → OnComboFinished() 후 자동 실행
```

### 적 콤보 순번 AI (EnemyComboManager)

```
BattleEncounterDataSO.comboCount 기준:
  1 = 단독 공격 (모두 자유)
  2 = EnemyA → EnemyB → EnemyA ... 순환
  3 = EnemyA → EnemyB → EnemyC → ... 순환

Enemy.TryAttack() → _isMyComboTurn 체크
  → 내 순번이면 공격 후 EnemyComboManager.AdvanceTurn()
  → 내 순번이 아니면 공격 건너뜀
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
- `SkillEffectStrike` — 2연타 연출 (DOTween Sequence)
- `SkillEffectShoot` — 3연발 + 레이저 조준선 + 총구 플래시 (LineRenderer)
- `SkillEffectWall` — 강한 밀치기 + 기절 (Enemy.Stun() 연동)
- `ComboManager` 작성
  - 2콤보 A (타격+사격): 관통탄 발사
  - 2콤보 B (타격+벽): 고속 돌진 압착
  - 2콤보 C (사격+벽): 하늘 낙하 압착
  - 3콤보 (전원 연계): 날리기 → 붙잡기 → 연발 → 최종 밀치기
- 콤보 포지션 동적 계산 (`CalcComboPositions`) — Inspector 고정 Transform 제거
- `PlayerAbility` 특수 능력 3종 구현 (목표 지정, 긴급 수리, 과부하)
- `UIManager` 전체 HUD 연동 (HP바, EXP바, 스킬 게이지, 콤보 게이지)

### Session 5 — 필드 카메라 구조 검토
- 3종 센트리의 2D / 2.5D 모드 분리 구조 재확인
- 배틀 진입 시 2D 사이드뷰로 보이는 문제 원인 분석
- `FieldManager` VCam 연결 및 Priority 체크리스트 정리
- **ExplorationVCam / BattleVCam 2개 분리 필수** 결론 확정

### Session 6 — UI 분리 & 적 배틀 UI 설계 & 콤보 시스템 재설계
#### 이름 변경
| 구 이름 | 신 이름 | 비고 |
|---------|---------|------|
| `UIManager` | `BattleUIManager` | 조율자 역할로 경량화 |
| `ComboManager` | `SentryComboManager` | 큐 시스템 재설계 포함 |
| `EnemyComboGroup` | `EnemyComboManager` | 싱글턴 구조 유지 |

#### 신규 파일
| 파일 | 역할 |
|------|------|
| `PlayerBattleUIManager.cs` | 센트리 HUD / 능력 HUD / 콤보 HUD 전담 |
| `EnemyBattleUIManager.cs` | 적 캐릭터 HUD 전담 (3슬롯 고정) |
| `BattleUIButton.cs` | IPointer 호버/클릭 연출 버튼 컴포넌트 |

#### 주요 변경 내용

**BattleUIManager (구 UIManager)**
- `_killCountText` / `_battleHudRoot` / `UpdateKillCount()` 제거
  - 킬 카운트는 BattleManager 내부 승리 판정에서만 사용, UI 표시 불필요
- 버튼 이벤트를 Inspector OnClick → `AddListener()` / `RemoveListener()` 방식으로 변경
  - `_fightButton`, `_fleeButton`, `_victoryConfirmButton`, `_defeatReturnButton` Inspector 연결
  - `Start()`에서 구독, `OnDestroy()`에서 해제

**PlayerBattleUIManager**
- 콤보 HUD 구조: 2콤보 아이콘 슬롯별 3개 + 3콤보 아이콘 슬롯별 3개
  - 3개 아이콘이 각각 하나의 Ratio 값을 공유해서 동시에 갱신
  - `Combo2CooldownRatio` → 생존 조합 중 가장 준비된 조합 대표값
  - `Combo3CooldownRatio` → 전원 공용 1개

**EnemyBattleUIManager**
- 3슬롯 고정 구조 (동적 생성 방식 폐기)
- 슬롯 내용: 이름 텍스트 / 레벨 텍스트 / HP바 / KO아이콘
- `RegisterEnemy()` — EnemySpawner 소환 직후 호출
- `OnEnemyDied()` — Enemy.Die()에서 직접 호출

**SentryComboManager (구 ComboManager)**
- 콤보 큐(`Queue<ComboType>`) 시스템 도입
  - 게이지 만참 시 큐에 추가, 재생 중이면 대기 후 자동 실행
- 2콤보 쿨타임 4개 개별 분리
  - `_combo3CooldownTimer` / `_comboACooldownTimer` / `_comboBCooldownTimer` / `_comboCCooldownTimer`
- 2콤보 랜덤 선택: 가능한 조합(쿨타임 OK + 생존) 후보 수집 후 `Random.Range`로 1개 선택

**EnemyComboManager (구 EnemyComboGroup)**
- `UnregisterEnemy()` → `OnEnemyDied()` 메서드명 변경
- `EnemyBattleUIManager` 새 API에 맞게 호출부 수정

**BattleEncounterDataSO**
- `comboCount` 필드 추가 (`Range(1, 3)`)
  - 1 = 단독 공격 / 2 = 2명 번갈아 / 3 = 3명 순환

**Enemy.cs**
- `_level` 필드 + `Level` 프로퍼티 추가
- `_hpFillSprite` / `_hpBarGroup` 제거 (HP 표시는 EnemyBattleUIManager 전담)
- `UpdateHpBar()` 메서드 제거
- `Die()`에 `EnemyBattleUIManager.Instance?.OnEnemyDied(this)` 추가
- `TryAttack()` 공격 완료 후 `EnemyComboManager.Instance?.AdvanceTurn()` 추가
- `using UnityEngine.UI` 제거

**BattleUIButton.cs (신규)**
- `[RequireComponent(typeof(Button))]`
- `IPointerEnterHandler` — 스케일 확대 + 색상 강조 (DOTween)
- `IPointerExitHandler` — 스케일/색상 원상 복구 (DOTween)
- `IPointerClickHandler` — 펀치 스케일 눌림 효과 (DOTween)
- `OnDisable()`에서 Tween 중단 및 기본 상태 복구

---

## 📌 현재 미완성 / 다음 세션 TODO

- [ ] Unity Inspector 연결 작업
  - [ ] PlayerBattleUIManager / EnemyBattleUIManager 오브젝트 추가 및 연결
  - [ ] EnemyComboManager BattleField 하위 배치
  - [ ] Canvas 패널 구성 (EnemyHUDPanel 슬롯 3개, EncounterPanel, VictoryPanel, DefeatPanel)
  - [ ] 각 버튼에 BattleUIButton 컴포넌트 추가
  - [ ] BattleUIManager Inspector 버튼 4개 연결
  - [ ] BattleEncounterDataSO Inspector에서 comboCount 설정
- [ ] BattleManager.cs 수정
  - [ ] `UIManager` → `BattleUIManager` 참조 변경
  - [ ] `UpdateKillCount()` 호출 제거
  - [ ] `ComboManager` → `SentryComboManager` 참조 변경
- [ ] Enemy.cs 프리팹 수정 — HpBarGroup 오브젝트 제거
- [ ] EnemySpawner.cs 수정
  - [ ] `EnemyComboManager.Initialize()` 호출 추가
  - [ ] `EnemyBattleUIManager.RegisterEnemy()` 호출 추가
- [ ] 쉼터 구간 센트리 HP 회복 / 부활 시스템 구현
- [ ] 과부하 폭주 상태 연출 (`PlayerAbility` Ability3)
- [ ] 배틀 결과 화면 연출 강화

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
| 버튼 이벤트 | `Button.onClick.AddListener()` / `RemoveListener()` — Inspector OnClick 지양 |

---

## 🐛 트러블슈팅 / 확인 사항

### 배틀 필드 진입 후 여전히 2D 사이드뷰로 보이는 문제

**원인 후보:**

1. **Inspector VCam 미연결** (가장 흔한 원인)
   - `FieldManager` Inspector에서 `_explorationVCam`, `_battleVCam` 슬롯이 비어있으면 null 체크로 그냥 넘어감

2. **BattleVCam 물리적 위치가 사이드뷰 앵글**
   - BattleVCam을 쿼터뷰 앵글 (위에서 비스듬히 내려다보는 위치)에 배치해야 함

3. **Priority 값 역전**
   - `_explorationCamPriority = 10`, `_battleCamPriority = 20` 이어야 함

**체크리스트:**
```
□ FieldManager._explorationVCam → ExplorationVirtualCamera 드래그 연결
□ FieldManager._battleVCam      → BattleVirtualCamera 드래그 연결
□ BattleVirtualCamera 위치: 쿼터뷰 앵글로 배치 (Y높이 + 약간 기울임)
□ _explorationCamPriority = 10, _battleCamPriority = 20 확인
□ BattleFieldRoot 연결 및 하위에 쿼터뷰 배틀 씬 오브젝트 배치
```
