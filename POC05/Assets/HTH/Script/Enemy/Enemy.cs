using UnityEngine;
using DG.Tweening;
using System.Collections;

namespace SENTRY
{
    /// <summary>
    /// 배틀 필드에서 등장하는 적 캐릭터.
    ///
    /// [버그 수정 — 선택창 전 AI 선행 실행]
    /// _isBattleStarted 플래그를 추가했습니다.
    /// SpawnWithDropEffect()로 소환된 직후에는 false 상태로 AI가 완전히 정지합니다.
    /// BattleManager.StartBattle() → BattleStartRoutine() 내부에서
    /// ActivateBattleAI()를 호출해야 비로소 이동·공격이 시작됩니다.
    ///
    /// [히어라키 위치]
    /// BattleField
    ///   └── Enemy (프리팹 — EnemySpawner가 동적 생성)
    ///         ├── Enemy (이 스크립트)
    ///         ├── Rigidbody2D  (gravityScale=0, Kinematic)
    ///         ├── Collider2D
    ///         └── SpriteRenderer
    /// </summary>

    /// <summary>피격 타입 열거형</summary>
    public enum HitType { Strike, Shoot }

    public class Enemy : MonoBehaviour
    {
        // ─────────────────────────────────────────
        //  Inspector 노출 필드
        // ─────────────────────────────────────────

        [Header("기본 스탯")]
        [Tooltip("최대 체력")]
        [SerializeField] private int _maxHp = 100;

        [Tooltip("적 레벨. EnemyBattleUIManager 레벨 텍스트에 표시됩니다.")]
        [SerializeField] private int _level = 1;

        [Tooltip("이동 속도 (MovePosition 기반 — units/sec)")]
        [SerializeField] private float _moveSpeed = 2.5f;

        [Tooltip("센트리 탐지 최대 반경")]
        [SerializeField] private float _detectionRange = 30f;

        [Tooltip("근접 공격이 닿는 거리")]
        [SerializeField] private float _attackRange = 1.2f;

        [Header("공격 설정")]
        [Tooltip("센트리에게 가하는 기본 데미지")]
        [SerializeField] private int _attackDamage = 15;

        [Tooltip("공격 쿨타임 (초)")]
        [SerializeField] private float _attackCooldown = 1.2f;

        [Header("스킬 게이지")]
        [Tooltip("최대 스킬 게이지")]
        [SerializeField] private float _maxSkillGauge = 100f;

        [Tooltip("공격 1회당 스킬 게이지 충전량")]
        [SerializeField] private float _skillGaugePerAttack = 20f;

        [Header("타겟 갱신")]
        [Tooltip("타겟 센트리를 재탐색하는 주기 (초)")]
        [SerializeField] private float _targetRefreshInterval = 0.5f;

        [Header("센트리 레이어")]
        [Tooltip("SentryLive 레이어로 설정하세요.")]
        [SerializeField] private LayerMask _sentryLayer;

        [Header("경험치 설정")]
        [Tooltip("사망 시 센트리들에게 분배할 경험치")]
        [SerializeField] private int _expOnDeath = 30;

        [Header("페이크 쿼터뷰 설정")]
        [Tooltip("Y 위치 기반 SortingOrder 갱신 배율")]
        [SerializeField] private float _sortingOrderScale = 10f;

        [Header("피격 넉백 설정")]
        [Tooltip("Strike 피격 시 밀려나는 거리")]
        [SerializeField] private float _strikeKnockDistance = 1.2f;

        [Tooltip("Shoot 피격 시 밀려나는 거리")]
        [SerializeField] private float _shootKnockDistance = 0.4f;

        [Tooltip("넉백 이동 소요 시간 (초)")]
        [SerializeField] private float _knockDuration = 0.18f;

        // ─────────────────────────────────────────
        //  내부 상태 변수
        // ─────────────────────────────────────────

        /// <summary>현재 추격 중인 센트리</summary>
        private SentryBase _currentTarget;

        /// <summary>현재 HP</summary>
        private int _currentHp;

        /// <summary>사망 여부 (중복 Die() 방지)</summary>
        private bool _isDead = false;

        /// <summary>기절 여부 — true이면 이동·공격 AI 정지</summary>
        private bool _isStunned = false;

        /// <summary>
        /// 전투 AI 활성화 여부.
        ///
        /// [중요] 소환 직후 기본값은 false입니다.
        /// 낙하 연출 → 선택창 대기 구간에서 적이 움직이지 않도록 합니다.
        /// BattleManager.BattleStartRoutine()에서 ActivateBattleAI()를 호출해야
        /// 비로소 이동·공격 AI가 활성화됩니다.
        /// </summary>
        private bool _isBattleStarted = false;

        /// <summary>
        /// 콤보 순번 권한 여부.
        /// EnemyComboManager.SetComboTurn()으로 설정됩니다.
        /// 단독 모드이면 항상 true로 동작합니다.
        /// </summary>
        private bool _isMyTurn = true;

        /// <summary>마지막 공격 시각</summary>
        private float _lastAttackTime;

        /// <summary>마지막 타겟 재탐색 시각</summary>
        private float _lastTargetRefreshTime;

        /// <summary>현재 스킬 게이지 누적량</summary>
        private float _currentSkillGauge = 0f;

        /// <summary>FixedUpdate에서 사용할 이동 방향 벡터</summary>
        private Vector2 _moveDir = Vector2.zero;

        /// <summary>이번 FixedUpdate에서 이동 여부</summary>
        private bool _shouldMove = false;

        /// <summary>Rigidbody2D 캐시 (MovePosition 이동)</summary>
        private Rigidbody2D _rigid2D;

        /// <summary>SpriteRenderer 캐시 (방향 플립 + SortingOrder)</summary>
        private SpriteRenderer _spriteRenderer;

        // ─────────────────────────────────────────
        //  외부 공개 프로퍼티
        // ─────────────────────────────────────────

        /// <summary>사망 여부</summary>
        public bool IsDead => _isDead;

        /// <summary>현재 HP</summary>
        public int CurrentHp => _currentHp;

        /// <summary>최대 HP (EnemyBattleUIManager HP 바 계산용)</summary>
        public int MaxHp => _maxHp;

        /// <summary>적 레벨 (EnemyBattleUIManager 레벨 텍스트 표시용)</summary>
        public int Level => _level;

        /// <summary>현재 스킬 게이지 (EnemyBattleUIManager 표시용)</summary>
        public float SkillGauge => _currentSkillGauge;

        /// <summary>최대 스킬 게이지 (EnemyBattleUIManager 표시용)</summary>
        public float MaxSkillGauge => _maxSkillGauge;

        /// <summary>기절 상태 여부 (EnemyBattleUIManager StunIcon 표시용)</summary>
        public bool IsStunned => _isStunned;

        // ─────────────────────────────────────────
        //  초기화
        // ─────────────────────────────────────────

        /// <summary>
        /// EnemySpawner가 생성 직후 호출합니다.
        /// _isBattleStarted는 false로 초기화되어 AI가 정지 상태로 시작합니다.
        /// </summary>
        public void Init(Transform ignored = null)
        {
            _currentTarget = null;
            _lastTargetRefreshTime = 0f;
            _isMyTurn = true;
            _isBattleStarted = false; // AI 비활성 상태로 시작
            _shouldMove = false;
        }

        // ─────────────────────────────────────────
        //  전투 AI 활성화 (BattleManager 연동)
        // ─────────────────────────────────────────

        /// <summary>
        /// 적 전투 AI를 활성화합니다.
        /// [전투하기] 선택 → 카운트다운 완료 → BattleManager.BattleStartRoutine()에서 호출합니다.
        /// 이 메서드 호출 이후부터 이동·공격 AI가 실행됩니다.
        /// </summary>
        public void ActivateBattleAI()
        {
            _isBattleStarted = true;
            Debug.Log($"[{name}] 전투 AI 활성화");
        }

        // ─────────────────────────────────────────
        //  콤보 순번 제어 (EnemyComboManager 연동)
        // ─────────────────────────────────────────

        /// <summary>
        /// EnemyComboManager가 공격 순번을 설정합니다.
        /// isMyTurn = false 이면 TryAttack()을 건너뜁니다.
        /// </summary>
        public void SetComboTurn(bool isMyTurn)
        {
            _isMyTurn = isMyTurn;
        }

        // ─────────────────────────────────────────
        //  유니티 생명주기
        // ─────────────────────────────────────────

        private void Awake()
        {
            _rigid2D = GetComponent<Rigidbody2D>();
            _spriteRenderer = GetComponent<SpriteRenderer>();
        }

        private void Start()
        {
            _currentHp = _maxHp;
        }

        private void Update()
        {
            // _isBattleStarted = false : 낙하 연출 / 선택창 대기 구간 — AI 완전 정지
            if (_isDead || _isStunned || !_isBattleStarted)
            {
                _shouldMove = false;
                return;
            }

            // Y 기반 SortingOrder 갱신 (페이크 쿼터뷰 깊이 정렬)
            if (_spriteRenderer != null)
                _spriteRenderer.sortingOrder =
                    Mathf.RoundToInt(-transform.position.y * _sortingOrderScale);

            // 주기적 타겟 재탐색
            if (Time.time >= _lastTargetRefreshTime + _targetRefreshInterval)
            {
                _lastTargetRefreshTime = Time.time;
                FindTarget();
            }

            // 현재 타겟 유효성 검사
            if (_currentTarget == null || _currentTarget.IsKnockedOut)
            {
                FindTarget();
                if (_currentTarget == null)
                {
                    _shouldMove = false;
                    return;
                }
            }

            HandleBattleAI();
        }

        private void FixedUpdate()
        {
            // _isBattleStarted = false이면 MovePosition도 실행하지 않습니다.
            if (_isDead || _isStunned || !_shouldMove || !_isBattleStarted) return;
            if (_rigid2D != null)
                _rigid2D.MovePosition(
                    _rigid2D.position + _moveDir * _moveSpeed * Time.fixedDeltaTime);
        }

        // ─────────────────────────────────────────
        //  타겟 탐색
        // ─────────────────────────────────────────

        /// <summary>
        /// SentryLive 레이어 내 가장 가까운 센트리를 타겟으로 설정합니다.
        /// KO 상태인 센트리는 제외합니다.
        /// </summary>
        private void FindTarget()
        {
            Collider2D[] hits = Physics2D.OverlapCircleAll(
                transform.position, _detectionRange, _sentryLayer);

            SentryBase nearest = null;
            float minDist = float.MaxValue;

            foreach (Collider2D col in hits)
            {
                if (col == null) continue;
                SentryBase sentry = col.GetComponent<SentryBase>();
                if (sentry == null || sentry.IsKnockedOut) continue;

                float dist = Vector2.Distance(transform.position, col.transform.position);
                if (dist < minDist) { minDist = dist; nearest = sentry; }
            }

            _currentTarget = nearest;
        }

        // ─────────────────────────────────────────
        //  전투 AI
        // ─────────────────────────────────────────

        /// <summary>타겟 추격 및 공격 범위 내 공격 시도.</summary>
        private void HandleBattleAI()
        {
            if (_currentTarget == null) { _shouldMove = false; return; }

            float dist = Vector2.Distance(
                transform.position, _currentTarget.transform.position);

            if (dist <= _attackRange)
            {
                _shouldMove = false;
                _moveDir = Vector2.zero;
                TryAttack();
            }
            else
            {
                _moveDir = ((Vector2)_currentTarget.transform.position
                               - (Vector2)transform.position).normalized;
                _shouldMove = true;

                if (_spriteRenderer != null)
                    _spriteRenderer.flipX = _moveDir.x < 0;
            }
        }

        // ─────────────────────────────────────────
        //  기본 공격
        // ─────────────────────────────────────────

        /// <summary>
        /// 타겟 센트리에게 데미지를 입힙니다.
        /// 콤보 순번이 아니면 건너뜁니다 (단독 모드 제외).
        /// </summary>
        private void TryAttack()
        {
            if (Time.time < _lastAttackTime + _attackCooldown) return;

            // 콤보 순번 체크
            if (EnemyComboManager.Instance != null &&
                !EnemyComboManager.Instance.IsSingleMode() &&
                !_isMyTurn)
                return;

            if (_currentTarget == null || _currentTarget.IsKnockedOut)
            {
                _currentTarget = null;
                return;
            }

            _lastAttackTime = Time.time;

            _currentTarget.TakeDamage(_attackDamage);

            // 스킬 게이지 충전
            _currentSkillGauge = Mathf.Min(
                _currentSkillGauge + _skillGaugePerAttack, _maxSkillGauge);

            // 공격 방향 연출
            if (_currentTarget != null)
            {
                Vector3 punchDir =
                    (_currentTarget.transform.position - transform.position).normalized * 0.3f;
                transform.DOPunchPosition(punchDir, 0.18f, 5, 0.4f);
            }

            EnemyComboManager.Instance?.AdvanceTurn();

            Debug.Log($"[{name}] → [{_currentTarget?.SentryName}] 공격! " +
                      $"데미지: {_attackDamage}");
        }

        // ─────────────────────────────────────────
        //  피격 처리
        // ─────────────────────────────────────────

        /// <summary>
        /// 센트리 공격을 받았을 때 호출됩니다.
        /// HP 변경 후 EnemyBattleUIManager에 갱신을 알립니다.
        /// </summary>
        /// <param name="damage">입힐 데미지</param>
        /// <param name="hitType">피격 타입 (Strike / Shoot)</param>
        /// <param name="hitSourcePos">공격 발원 위치 (넉백 방향 계산용)</param>
        public void TakeDamage(int damage, HitType hitType, Vector3 hitSourcePos)
        {
            if (_isDead || _isStunned) return;

            _currentHp -= damage;
            _currentHp = Mathf.Max(_currentHp, 0);

            // 피격 연출
            transform.DOShakePosition(0.15f, 0.2f, 10, 90f);
            if (_spriteRenderer != null)
                _spriteRenderer.DOColor(Color.red, 0.05f)
                    .SetLoops(4, LoopType.Yoyo)
                    .OnComplete(() =>
                    {
                        if (_spriteRenderer != null)
                            _spriteRenderer.color = Color.white;
                    });

            // 넉백 — DOMove (Kinematic 안전)
            float knockDist = hitType == HitType.Strike
                ? _strikeKnockDistance
                : _shootKnockDistance;

            if (knockDist > 0f)
            {
                Vector3 knockDir = (transform.position - hitSourcePos).normalized;
                Vector3 knockTarget = transform.position + knockDir * knockDist;
                transform.DOMove(knockTarget, _knockDuration).SetEase(Ease.OutQuart);
            }

            // HP 변경을 EnemyBattleUIManager에 통보
            EnemyBattleUIManager.Instance?.OnHpChanged(this);

            if (_currentHp <= 0) Die();
        }

        // ─────────────────────────────────────────
        //  기절 (WallSentry 스킬 연동)
        // ─────────────────────────────────────────

        /// <summary>
        /// WallSentry 스킬로 적을 기절시킵니다.
        /// duration초 후 자동 해제됩니다.
        /// </summary>
        public void Stun(float duration)
        {
            if (_isDead) return;
            StartCoroutine(StunRoutine(duration));
        }

        private IEnumerator StunRoutine(float duration)
        {
            _isStunned = true;
            _shouldMove = false;

            if (_spriteRenderer != null)
                _spriteRenderer.DOColor(Color.cyan, 0.15f);

            yield return new WaitForSeconds(duration);

            _isStunned = false;

            if (_spriteRenderer != null && !_isDead)
                _spriteRenderer.DOColor(Color.white, 0.15f);

            Debug.Log($"[{name}] 기절 해제");
        }

        // ─────────────────────────────────────────
        //  사망 처리
        // ─────────────────────────────────────────

        /// <summary>
        /// HP가 0이 되면 호출됩니다.
        /// 콤보 순번 정리 → 킬 카운트 + 경험치 → 사망 연출 → Destroy
        /// </summary>
        private void Die()
        {
            if (_isDead) return;
            _isDead = true;
            _shouldMove = false;

            // 콤보 순번 정리 + KO 아이콘 표시
            EnemyComboManager.Instance?.OnEnemyDied(this);

            // 킬 카운트 + 경험치 분배
            BattleManager.Instance?.OnEnemyDied(_expOnDeath);

            // 사망 연출 → 파괴
            transform.DOKill();
            transform.DOScale(Vector3.zero, 0.3f)
                .SetEase(Ease.InBack)
                .OnComplete(() => Destroy(gameObject));

            if (_spriteRenderer != null)
                _spriteRenderer.DOFade(0f, 0.25f);

            Debug.Log($"[{name}] 사망! 경험치 보상: {_expOnDeath}");
        }
    }
}