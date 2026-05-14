using UnityEngine;
using DG.Tweening;
using System.Collections;

namespace SENTRY
{
    /// <summary>
    /// 모든 센트리(소환수)의 공통 기능을 담당하는 베이스 클래스.
    ///
    /// [필드별 동작 구분]
    ///
    /// ▶ 탐색 필드 (2D 사이드뷰)
    ///   - 플레이어 뒤를 포메이션 오프셋으로 따라다닙니다.
    ///   - _formationOffset은 2D 사이드뷰 기준 X/Y 오프셋입니다.
    ///   - Rigidbody2D Dynamic + linearVelocity 방식으로 이동합니다.
    ///   - 중력이 활성화되어 있어 점프·낙하가 정상 동작합니다.
    ///
    /// ▶ 배틀 필드 (페이크 쿼터뷰)
    ///   - EnterBattlePhysics() 호출로 Kinematic 전환 + 중력 OFF.
    ///   - 이동은 Rigidbody2D.MovePosition()으로 처리합니다.
    ///     (Kinematic 상태에서 linearVelocity는 동작하지 않음)
    ///   - Y축이 깊이(원근감) 역할 → Y가 클수록 화면 위 = 멀리 있는 것.
    ///   - SortingOrder를 Y 위치 기반으로 매 프레임 갱신하여 앞뒤 정렬.
    ///   - 배틀 종료 후 ExitBattlePhysics() 로 Dynamic + 원래 중력 복원.
    ///
    /// [히어라키 위치]
    /// Sentries
    ///   ├── StrikeSentry (SentryBase 상속)
    ///   ├── ShootSentry  (SentryBase 상속)
    ///   └── WallSentry   (SentryBase 상속)
    /// </summary>
    public class SentryBase : MonoBehaviour
    {
        // ─────────────────────────────────────────
        //  Inspector 노출 필드
        // ─────────────────────────────────────────

        [Header("센트리 기본 정보")]
        [Tooltip("인스펙터 확인용 이름 (코드 동작에 영향 없음)")]
        [SerializeField] private string _sentryName = "Sentry";

        [Header("체력 설정")]
        [Tooltip("센트리의 초기 최대 HP")]
        [SerializeField] private int _maxHp = 150;

        [Header("성장 데이터")]
        [Tooltip("레벨업 스탯 증가를 정의하는 ScriptableObject.\n" +
                 "Project 창 → Create → SENTRY → SentryGrowthDataSO 로 생성하세요.\n" +
                 "연결하지 않으면 인스펙터 기본값으로 동작합니다.")]
        [SerializeField] private SentryGrowthDataSO _growthData;

        [Header("경험치 / 레벨 (GrowthData 미연결 시 사용)")]
        [Tooltip("GrowthData가 없을 때 사용할 기본 레벨업 요구 경험치")]
        [SerializeField] private int _baseExpToLevelUp = 100;

        [Tooltip("GrowthData가 없을 때 사용할 최대 레벨")]
        [SerializeField] private int _maxLevel = 10;

        [Header("탐색 필드 추종 설정 (2D 사이드뷰)")]
        [Tooltip("플레이어를 따라다닐 이동 속도 (탐색 필드 전용)")]
        [SerializeField] private float _followSpeed = 4f;

        [Tooltip("포메이션 정지 반경.\n" +
                 "이 거리 안에 들어오면 이동을 멈춥니다.")]
        [SerializeField] private float _followStopDistance = 1.5f;

        [Tooltip("탐색 필드(2D 사이드뷰) 기준 플레이어 포메이션 오프셋.\n" +
                 "X: 좌우 간격 / Y: 높이 차이\n" +
                 "예) Strike(-1.5, 0) / Shoot(-3, 0) / Wall(-2.2, 0.5)")]
        [SerializeField] private Vector2 _formationOffset = new Vector2(-1f, 0f);

        [Header("연출 참조")]
        [Tooltip("피격 / 무적 / 과부화 / 회복 연출용 SpriteRenderer")]
        [SerializeField] private SpriteRenderer _spriteRenderer;

        [Header("페이크 쿼터뷰 설정 (배틀 필드)")]
        [Tooltip("배틀 필드에서 Y 위치 기반 소팅 오더를 갱신할 배율.\n" +
                 "기본값 10 → Y가 1 낮아질수록 SortingOrder가 10 높아져 앞에 그려집니다.")]
        [SerializeField] private float _sortingOrderScale = 10f;

        // ─────────────────────────────────────────
        //  내부 상태 변수
        // ─────────────────────────────────────────

        /// <summary>현재 HP</summary>
        private int _currentHp;

        /// <summary>현재 경험치</summary>
        private int _currentExp = 0;

        /// <summary>현재 레벨</summary>
        private int _currentLevel = 1;

        /// <summary>플레이어 Transform (탐색 필드 추종 대상)</summary>
        private Transform _playerTransform;

        /// <summary>탐색 필드 추종 활성 여부</summary>
        private bool _isFollowing = false;

        /// <summary>무적 상태 여부 (콤보 연출 중 피격 방지)</summary>
        private bool _isInvincible = false;

        /// <summary>Rigidbody2D 캐시</summary>
        private Rigidbody2D _rb;

        /// <summary>
        /// 배틀 진입 전 원래 gravityScale 저장값.
        /// ExitBattlePhysics()에서 복원에 사용합니다.
        /// </summary>
        private float _savedGravityScale = 1f;

        /// <summary>
        /// 현재 배틀 물리 모드 여부.
        /// true이면 Kinematic + gravityScale=0 상태입니다.
        /// </summary>
        private bool _isBattlePhysics = false;

        // ─────────────────────────────────────────
        //  외부 공개 프로퍼티
        // ─────────────────────────────────────────

        /// <summary>현재 HP</summary>
        public int CurrentHp => _currentHp;

        /// <summary>최대 HP</summary>
        public int MaxHp => _maxHp;

        /// <summary>현재 레벨</summary>
        public int CurrentLevel => _currentLevel;

        /// <summary>현재 경험치</summary>
        public int CurrentExp => _currentExp;

        /// <summary>
        /// 현재 레벨 기준 레벨업 요구 경험치 (UI 표시용).
        /// GrowthData 연결 여부에 따라 자동 분기됩니다.
        /// </summary>
        public int RequiredExp => _growthData != null
            ? _growthData.GetRequiredExp(_currentLevel)
            : _baseExpToLevelUp * _currentLevel;

        /// <summary>최대 레벨 (UI 표시용)</summary>
        public int MaxLevel => _growthData != null ? _growthData.maxLevel : _maxLevel;

        /// <summary>기절(KO) 여부. true면 전투 AI가 멈추고 쉼터 부활 대기.</summary>
        public bool IsKnockedOut { get; private set; } = false;

        /// <summary>과부화 상태 여부 (자식 클래스 스탯 배율 적용용)</summary>
        public bool IsOverloaded { get; private set; } = false;

        /// <summary>과부화 공격력 배율</summary>
        public float OverloadDamageMultiplier { get; private set; } = 1f;

        /// <summary>과부화 이동 속도 배율</summary>
        public float OverloadSpeedMultiplier { get; private set; } = 1f;

        /// <summary>센트리 이름</summary>
        public string SentryName => _sentryName;

        /// <summary>
        /// 탐색 필드(2D 사이드뷰) 기준 포메이션 월드 좌표.
        /// 배틀 종료 후 탐색 필드로 복귀할 때의 목표 위치입니다.
        /// 배틀 필드 내 배치 위치는 BattleManager._sentryBattleStartPositions를 사용합니다.
        /// </summary>
        public Vector3 FormationWorldPosition
        {
            get
            {
                if (_playerTransform == null) return transform.position;
                return _playerTransform.position + (Vector3)(Vector2)_formationOffset;
            }
        }

        // ─────────────────────────────────────────
        //  초기화
        // ─────────────────────────────────────────

        /// <summary>
        /// 센트리 최초 소환 시 1회 호출합니다.
        /// HP / EXP / 레벨을 초기값으로 설정합니다.
        ///
        /// [주의] 배틀 진입마다 호출하면 레벨·EXP가 리셋됩니다.
        ///        배틀 진입 시에는 SetupForBattle()을 사용하세요.
        /// </summary>
        public virtual void Init(Transform player)
        {
            _playerTransform = player;
            _currentHp = _maxHp;
            _currentExp = 0;
            _currentLevel = 1;
            IsKnockedOut = false;
            IsOverloaded = false;
            _isInvincible = false;
            _isBattlePhysics = false;
            _rb = GetComponent<Rigidbody2D>();

            if (_rb != null)
                _savedGravityScale = _rb.gravityScale;

            _isFollowing = true;

            Debug.Log($"<color=cyan>[{_sentryName}]</color> 초기화 완료 (Lv.{_currentLevel})");
        }

        /// <summary>
        /// 배틀 필드 진입 시 매번 호출합니다.
        /// 레벨·EXP는 유지하고, 전투 상태 플래그만 초기화합니다.
        /// BattleManager.BattleStartRoutine() 이전에 호출하세요.
        /// </summary>
        public void SetupForBattle(Transform player)
        {
            // 플레이어 Transform 갱신 (탐색 필드 복귀 시 참조용)
            if (player != null) _playerTransform = player;

            // 전투 상태 플래그만 초기화 — 레벨·EXP·HP는 그대로 유지
            IsOverloaded = false;
            _isInvincible = false;
            _isBattlePhysics = false;

            // Rigidbody2D 캐시 (씬 재로드 대비)
            if (_rb == null)
            {
                _rb = GetComponent<Rigidbody2D>();
                if (_rb != null) _savedGravityScale = _rb.gravityScale;
            }

            Debug.Log($"[{_sentryName}] 배틀 준비 완료 " +
                      $"— Lv.{_currentLevel} / HP:{_currentHp}/{_maxHp} / EXP:{_currentExp}");
        }

        // ─────────────────────────────────────────
        //  유니티 생명주기
        // ─────────────────────────────────────────

        private void Awake()
        {
            // Rigidbody2D + gravityScale 캐싱
            _rb = GetComponent<Rigidbody2D>();
            if (_rb != null)
                _savedGravityScale = _rb.gravityScale;

            // ★ _currentHp를 Awake에서 초기화
            // Init()이 호출되지 않아도 HP 바가 0으로 표시되는 문제를 방지합니다.
            _currentHp = _maxHp;
        }

        private void Update()
        {
            // ── 탐색 필드: 플레이어 추종 이동 ──
            // 배틀 필드에서는 StopFollowing() 상태이므로 이 블록이 실행되지 않음
            if (!IsKnockedOut && _playerTransform != null && _isFollowing)
                FollowPlayer();

            // ── 배틀 필드: Y 기반 SortingOrder 갱신 ──
            // Y가 낮을수록(화면 아래 = 가까이) 앞에 그려집니다.
            if (_isBattlePhysics && _spriteRenderer != null)
                _spriteRenderer.sortingOrder =
                    Mathf.RoundToInt(-transform.position.y * _sortingOrderScale);
        }

        // ─────────────────────────────────────────
        //  탐색 필드 추종 이동 (2D 사이드뷰)
        // ─────────────────────────────────────────

        /// <summary>
        /// 탐색 필드(2D 사이드뷰)에서 플레이어의 포메이션 위치로 이동합니다.
        /// 배틀 필드에서는 StopFollowing()으로 비활성화됩니다.
        /// </summary>
        private void FollowPlayer()
        {
            Vector2 targetPos = (Vector2)_playerTransform.position + _formationOffset;
            float dist = Vector2.Distance(transform.position, targetPos);

            if (dist <= _followStopDistance)
            {
                if (_rb != null) _rb.linearVelocity = Vector2.zero;
                return;
            }

            Vector2 dir = (targetPos - (Vector2)transform.position).normalized;
            if (_rb != null) _rb.linearVelocity = dir * _followSpeed;
        }

        /// <summary>
        /// 추종을 멈춥니다.
        /// 배틀 필드 진입 시 BattleManager / ComboManager가 호출합니다.
        /// </summary>
        public void StopFollowing()
        {
            _isFollowing = false;
            if (_rb != null) _rb.linearVelocity = Vector2.zero;
        }

        /// <summary>
        /// 추종을 재개합니다.
        /// 배틀 종료 후 탐색 필드 복귀 시 BattleManager / ComboManager가 호출합니다.
        /// </summary>
        public void StartFollowing() => _isFollowing = true;

        // ─────────────────────────────────────────
        //  페이크 쿼터뷰 물리 전환 (핵심)
        // ─────────────────────────────────────────

        /// <summary>
        /// 배틀 필드 진입 시 BattleManager.PlaceSentriesAtBattleStart()에서 호출합니다.
        ///
        /// [동작]
        ///   1. 현재 gravityScale을 저장합니다. (탐색 필드 복귀 시 복원)
        ///   2. gravityScale = 0 → 중력에 의한 낙하를 완전히 차단합니다.
        ///   3. bodyType = Kinematic → 물리 엔진 충돌 계산을 끊습니다.
        ///      이 상태에서 AI 이동은 MovePosition()으로 처리합니다.
        ///   4. linearVelocity = 0 → 기존 운동량을 초기화합니다.
        ///
        /// [주의]
        ///   반드시 DOMove 호출 이전에 실행해야 합니다.
        ///   그렇지 않으면 DOMove 도중 중력에 끌려 내려가는 현상이 발생합니다.
        /// </summary>
        public void EnterBattlePhysics()
        {
            if (_rb == null) return;

            _savedGravityScale = _rb.gravityScale;      // 탐색 복귀 시 복원용
            _rb.gravityScale = 0f;                    // 중력 OFF
            _rb.bodyType = RigidbodyType2D.Kinematic;
            _rb.linearVelocity = Vector2.zero;
            _isBattlePhysics = true;

            Debug.Log($"[{_sentryName}] EnterBattlePhysics — Kinematic 전환 완료");
        }

        /// <summary>
        /// 탐색 필드 복귀 시 BattleManager.EndBattle()에서 호출합니다.
        ///
        /// [동작]
        ///   1. bodyType = Dynamic → 물리 엔진 제어로 복귀합니다.
        ///   2. gravityScale을 저장했던 원래 값으로 복원합니다.
        ///      (Inspector에서 설정한 값이 그대로 유지됩니다.)
        ///
        /// [주의]
        ///   ExplorationFieldRoot가 활성화되기 전에 호출해야
        ///   복귀 위치에서 바닥으로 자연스럽게 착지합니다.
        /// </summary>
        public void ExitBattlePhysics()
        {
            if (_rb == null) return;

            _rb.bodyType = RigidbodyType2D.Dynamic;
            _rb.gravityScale = _savedGravityScale;    // 원래 중력값 복원
            _isBattlePhysics = false;

            // SortingOrder를 기본값으로 복원 (탐색 필드는 정적 정렬)
            if (_spriteRenderer != null)
                _spriteRenderer.sortingOrder = 0;

            Debug.Log($"[{_sentryName}] ExitBattlePhysics — Dynamic 복원 완료");
        }

        /// <summary>
        /// 배틀 필드(Kinematic 상태)에서 목표 방향으로 이동합니다.
        /// 자식 클래스의 HandleBattleAI()에서 linearVelocity 대신 이 메서드를 호출하세요.
        ///
        /// [설계 이유]
        ///   Kinematic 상태에서는 linearVelocity 할당이 무시됩니다.
        ///   MovePosition은 Kinematic에서도 정상 동작하며,
        ///   물리 보간이 적용되어 부드러운 이동이 보장됩니다.
        /// </summary>
        /// <param name="direction">이동 방향 벡터 (normalized 권장)</param>
        /// <param name="speed">이동 속도 (units/sec)</param>
        public void BattleMove(Vector2 direction, float speed)
        {
            if (_rb == null || !_isBattlePhysics) return;
            _rb.MovePosition(_rb.position + direction * speed * Time.fixedDeltaTime);
        }

        /// <summary>
        /// 배틀 필드에서 이동을 즉시 멈춥니다.
        /// Kinematic 상태에서 linearVelocity=0은 무의미하므로 이 메서드를 사용하세요.
        /// (DOTween이 위치를 제어 중일 때는 호출 불필요)
        /// </summary>
        public void BattleStop()
        {
            if (_rb == null) return;
            if (_isBattlePhysics)
                _rb.linearVelocity = Vector2.zero; // Kinematic에서도 안전 처리
        }

        // ─────────────────────────────────────────
        //  체력 처리
        // ─────────────────────────────────────────

        /// <summary>
        /// 데미지를 입힙니다. 무적 또는 기절 상태면 무시합니다.
        /// 배틀 필드(쿼터뷰)에서 Enemy.TakeDamage 연계로 호출됩니다.
        /// </summary>
        public virtual void TakeDamage(int damage)
        {
            if (_isInvincible || IsKnockedOut) return;

            _currentHp -= damage;
            _currentHp = Mathf.Max(_currentHp, 0);

            Debug.Log($"<color=orange>[{_sentryName} 피격]</color> " +
                      $"-{damage} / 남은 HP: {_currentHp}");

            transform.DOShakePosition(0.2f, 0.15f, 10, 90f);

            if (_spriteRenderer != null)
                _spriteRenderer.DOColor(Color.red, 0.05f)
                    .SetLoops(4, LoopType.Yoyo)
                    .OnComplete(() =>
                    {
                        if (_spriteRenderer != null)
                            _spriteRenderer.color = IsOverloaded ? Color.red : Color.white;
                    });

            if (_currentHp <= 0) KnockOut();
        }

        /// <summary>
        /// HP를 회복합니다.
        /// 탐색 필드 ShelterZone 또는 PlayerAbility 긴급 수리에서 호출합니다.
        /// KO 상태에서는 회복 불가 (ShelterZone의 Revive()로만 부활).
        /// </summary>
        public void Heal(int amount)
        {
            if (IsKnockedOut) return;

            _currentHp = Mathf.Min(_currentHp + amount, _maxHp);
            Debug.Log($"<color=lime>[{_sentryName} 회복]</color> " +
                      $"+{amount} HP / 현재: {_currentHp}");

            if (_spriteRenderer != null)
                _spriteRenderer.DOColor(Color.green, 0.1f)
                    .SetLoops(4, LoopType.Yoyo)
                    .OnComplete(() =>
                    {
                        if (_spriteRenderer != null)
                            _spriteRenderer.color = IsOverloaded ? Color.red : Color.white;
                    });
        }

        /// <summary>
        /// 탐색 필드 쉼터(ShelterZone)에서 KO 상태 센트리를 부활시킵니다.
        /// 배틀 필드에서는 호출되지 않습니다.
        /// </summary>
        public void Revive(int healAmount = 0)
        {
            if (!IsKnockedOut) return;

            IsKnockedOut = false;
            _currentHp = (healAmount <= 0) ? _maxHp : Mathf.Min(healAmount, _maxHp);
            _isFollowing = true;

            transform.localScale = Vector3.zero;
            transform.DOScale(Vector3.one, 0.4f).SetEase(Ease.OutBack);
            if (_spriteRenderer != null) _spriteRenderer.color = Color.white;

            Debug.Log($"<color=lime>[{_sentryName} 부활]</color> HP: {_currentHp}");
        }

        // ─────────────────────────────────────────
        //  기절 (KO)
        // ─────────────────────────────────────────

        /// <summary>
        /// HP가 0이 되면 내부에서 호출됩니다.
        /// 배틀 AI를 멈추고 쉼터 부활 대기 상태로 전환합니다.
        /// </summary>
        private void KnockOut()
        {
            IsKnockedOut = true;
            IsOverloaded = false;
            _isFollowing = false;
            if (_rb != null) _rb.linearVelocity = Vector2.zero;

            if (_spriteRenderer != null)
                _spriteRenderer.DOColor(Color.gray, 0.3f);

            transform.DOScale(new Vector3(1f, 0.3f, 1f), 0.3f).SetEase(Ease.OutBounce);

            Debug.Log($"<color=red>[{_sentryName} KO]</color> " +
                      "기절 — 탐색 필드 쉼터에서 부활 가능");

            // 배틀 매니저에 KO 통보 → 전원 KO 시 패배 처리
            BattleManager.Instance?.OnSentryKnockedOut();
        }

        /// <summary>과부화 종료 후 PlayerAbility가 강제 기절 시 호출합니다.</summary>
        public void ForceKnockOut()
        {
            if (!IsKnockedOut) KnockOut();
        }

        // ─────────────────────────────────────────
        //  무적 (콤보 시스템 — 배틀 필드 전용)
        // ─────────────────────────────────────────

        /// <summary>
        /// 무적 상태를 설정합니다.
        /// 배틀 필드(쿼터뷰) 콤보 연출 중 ComboManager가 호출합니다.
        /// </summary>
        public void SetInvincible(bool on)
        {
            _isInvincible = on;
            if (_spriteRenderer == null) return;

            if (on)
            {
                _spriteRenderer.DOKill();
                _spriteRenderer.DOFade(0.5f, 0.15f).SetLoops(-1, LoopType.Yoyo);
            }
            else
            {
                _spriteRenderer.DOKill();
                _spriteRenderer.color = IsOverloaded ? Color.red : Color.white;
            }
        }

        // ─────────────────────────────────────────
        //  과부화 (PlayerAbility — 배틀 필드 전용)
        // ─────────────────────────────────────────

        /// <summary>
        /// 과부화 상태를 설정합니다.
        /// 배틀 필드에서 PlayerAbility.OverloadRoutine()이 호출합니다.
        /// </summary>
        public void SetOverload(bool on, float damageMultiplier, float speedMultiplier)
        {
            IsOverloaded = on;
            OverloadDamageMultiplier = on ? damageMultiplier : 1f;
            OverloadSpeedMultiplier = on ? speedMultiplier : 1f;

            if (_spriteRenderer != null)
                _spriteRenderer.color = on ? Color.red : Color.white;
        }

        // ─────────────────────────────────────────
        //  경험치 / 레벨업
        // ─────────────────────────────────────────

        /// <summary>
        /// 경험치를 추가합니다. 레벨업 조건 충족 시 LevelUp()을 호출합니다.
        /// Enemy.Die()에서 GainExp()를 통해 간접 호출됩니다.
        /// </summary>
        public void AddExp(int amount)
        {
            if (IsKnockedOut) return;

            _currentExp += amount;
            Debug.Log($"[{_sentryName}] EXP +{amount} ({_currentExp}/{RequiredExp})");

            while (_currentExp >= RequiredExp && _currentLevel < MaxLevel)
            {
                _currentExp -= RequiredExp;
                LevelUp();
            }
        }

        /// <summary>
        /// 레벨업 처리. 자식 클래스에서 override하여 추가 스탯 상승 로직을 작성합니다.
        /// </summary>
        protected virtual void LevelUp()
        {
            _currentLevel++;
            _maxHp = Mathf.RoundToInt(_maxHp * 1.1f);
            _currentHp = _maxHp;

            transform.DOPunchScale(Vector3.one * 0.4f, 0.4f, 5, 0.5f);
            if (_spriteRenderer != null)
                _spriteRenderer.DOColor(Color.yellow, 0.1f)
                    .SetLoops(6, LoopType.Yoyo)
                    .OnComplete(() =>
                    {
                        if (_spriteRenderer != null)
                            _spriteRenderer.color = Color.white;
                    });

            UIManager.Instance?.PlayLevelUpEffect(_sentryName, _currentLevel);
            Debug.Log($"<color=yellow>[{_sentryName} 레벨업!]</color> Lv.{_currentLevel}");
        }
    }
}