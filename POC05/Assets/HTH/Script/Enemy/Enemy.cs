using UnityEngine;
using DG.Tweening;
using System.Collections;

namespace SENTRY
{
    /// <summary>
    /// 배틀 필드에서 등장하는 적 캐릭터.
    ///
    /// [버그 수정 — TilemapCollider 뚫림]
    /// TakeDamage()의 넉백 DOMove에 BattlePhysicsHelper.GetSafeTarget()을 적용했습니다.
    /// 넉백 목표 위치를 CircleCast로 검증해 벽에 막히면 직전 위치로 클램핑합니다.
    ///
    /// [Inspector 추가 필드]
    /// _wallLayer — TilemapCollider가 있는 레이어 설정 (보통 "Ground" 또는 "Wall")
    /// _colliderRadius — 넉백 CircleCast 반경 (기본 0.4f)
    /// </summary>

    /// <summary>피격 타입. Strike = 강한 넉백, Shoot = 약한 밀림</summary>
    public enum HitType { Strike, Shoot }

    public class Enemy : MonoBehaviour
    {
        // ─────────────────────────────────────────
        //  Inspector 노출 필드
        // ─────────────────────────────────────────

        [Header("기본 스탯")]
        [Tooltip("레벨 (EnemyBattleUIManager 표시용)")]
        [SerializeField] private int _level = 0;

        [Tooltip("최대 체력")]
        [SerializeField] private int _maxHp = 100;

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
        [Tooltip("기본 공격 1회 시 스킬 게이지 충전량")]
        [SerializeField] private float _skillGaugePerAttack = 10f;

        [Tooltip("스킬 게이지 최대치")]
        [SerializeField] private float _maxSkillGauge = 100f;

        [Header("타겟 갱신")]
        [Tooltip("타겟 센트리를 재탐색하는 주기 (초)")]
        [SerializeField] private float _targetRefreshInterval = 0.5f;

        [Header("경험치 설정")]
        [Tooltip("사망 시 센트리들에게 분배할 경험치 양")]
        [SerializeField] private int _expOnDeath = 30;

        [Header("레이어 설정")]
        [Tooltip("탐색할 센트리 레이어. SentryLive를 설정하세요.")]
        [SerializeField] private LayerMask _sentryLayer;

        [Tooltip("넉백 CircleCast 충돌 검사 레이어.\n" +
                 "TilemapCollider가 있는 레이어를 설정하세요 (보통 Ground 또는 Wall).")]
        [SerializeField] private LayerMask _wallLayer;

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

        [Tooltip("넉백 CircleCast 반경. 캐릭터 콜라이더 반경과 맞추세요.")]
        [SerializeField] private float _colliderRadius = 0.4f;

        // ─────────────────────────────────────────
        //  내부 상태 변수
        // ─────────────────────────────────────────

        private SentryBase _currentTarget;
        private int _currentHp;
        private bool _isDead = false;
        private bool _isStunned = false;
        private bool _isBattleStarted = false;
        private bool _isMyTurn = true;
        private float _lastAttackTime;
        private float _lastTargetRefreshTime;
        private float _currentSkillGauge = 0f;
        private Vector2 _moveDir = Vector2.zero;
        private bool _shouldMove = false;
        private Rigidbody2D _rigid2D;
        private SpriteRenderer _spriteRenderer;
        private Color _originColor;

        // ─────────────────────────────────────────
        //  외부 공개 프로퍼티
        // ─────────────────────────────────────────

        public bool IsDead => _isDead;
        public int CurrentHp => _currentHp;
        public int MaxHp => _maxHp;
        public int Level => _level;
        public float SkillGauge => _currentSkillGauge;
        public float MaxSkillGauge => _maxSkillGauge;
        public bool IsStunned => _isStunned;

        // ─────────────────────────────────────────
        //  초기화
        // ─────────────────────────────────────────

        /// <summary>EnemySpawner가 생성 직후 호출합니다.</summary>
        public void Init(Transform ignored = null)
        {
            _currentTarget = null;
            _lastTargetRefreshTime = 0f;
            _isMyTurn = true;
            _isBattleStarted = false;
            _shouldMove = false;
        }

        // ─────────────────────────────────────────
        //  전투 AI 활성화
        // ─────────────────────────────────────────

        /// <summary>
        /// BattleManager.BattleStartRoutine()에서 호출합니다.
        /// 이 메서드 이후부터 이동·공격 AI가 활성화됩니다.
        /// </summary>
        public void ActivateBattleAI()
        {
            _isBattleStarted = true;
            Debug.Log($"[{name}] 전투 AI 활성화");
        }

        // ─────────────────────────────────────────
        //  콤보 순번 제어
        // ─────────────────────────────────────────

        /// <summary>EnemyComboManager가 공격 순번을 설정합니다.</summary>
        public void SetComboTurn(bool isMyTurn) => _isMyTurn = isMyTurn;

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
            _originColor = _spriteRenderer.color;
        }

        private void Update()
        {
            if (_isDead || _isStunned || !_isBattleStarted)
            {
                _shouldMove = false;
                return;
            }

            if (_spriteRenderer != null)
                _spriteRenderer.sortingOrder =
                    Mathf.RoundToInt(-transform.position.y * _sortingOrderScale);

            if (Time.time >= _lastTargetRefreshTime + _targetRefreshInterval)
            {
                _lastTargetRefreshTime = Time.time;
                FindTarget();
            }

            if (_currentTarget == null || _currentTarget.IsKnockedOut)
            {
                FindTarget();
                if (_currentTarget == null) { _shouldMove = false; return; }
            }

            HandleBattleAI();
        }

        private void FixedUpdate()
        {
            if (_isDead || _isStunned || !_shouldMove || !_isBattleStarted) return;
            if (_rigid2D != null)
                _rigid2D.MovePosition(
                    _rigid2D.position + _moveDir * _moveSpeed * Time.fixedDeltaTime);
        }

        // ─────────────────────────────────────────
        //  타겟 탐색
        // ─────────────────────────────────────────

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

        private void TryAttack()
        {
            if (Time.time < _lastAttackTime + _attackCooldown) return;

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

            _currentSkillGauge = Mathf.Min(
                _currentSkillGauge + _skillGaugePerAttack, _maxSkillGauge);

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
        ///
        /// [버그 수정 — TilemapCollider 뚫림]
        /// 넉백 DOMove 전에 BattlePhysicsHelper.GetSafeTarget()으로
        /// 이동 경로 상의 벽 충돌을 CircleCast로 검사합니다.
        /// 충돌이 있으면 벽 직전 위치로 클램핑해서 DOMove를 실행합니다.
        /// </summary>
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
                            _spriteRenderer.color = _originColor;
                    });

            // ── 넉백 — BattlePhysicsHelper로 벽 충돌 보정 후 DOMove ──
            float knockDist = hitType == HitType.Strike
                ? _strikeKnockDistance
                : _shootKnockDistance;

            if (knockDist > 0f)
            {
                Vector3 knockDir = (transform.position - hitSourcePos).normalized;
                Vector3 rawTarget = transform.position + knockDir * knockDist;

                // [버그 수정] CircleCast로 벽 충돌 검사 → 안전한 위치로 클램핑
                Vector3 safeTarget = BattlePhysicsHelper.GetSafeTarget(
                    from: transform.position,
                    to: rawTarget,
                    radius: _colliderRadius,
                    wallLayer: _wallLayer);

                transform.DOMove(safeTarget, _knockDuration).SetEase(Ease.OutQuart);
            }

            EnemyBattleUIManager.Instance?.OnHpChanged(this);

            if (_currentHp <= 0) Die();
        }

        // ─────────────────────────────────────────
        //  기절
        // ─────────────────────────────────────────

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
                _spriteRenderer.DOColor(_originColor, 0.15f);

            Debug.Log($"[{name}] 기절 해제");
        }

        // ─────────────────────────────────────────
        //  사망
        // ─────────────────────────────────────────

        private void Die()
        {
            if (_isDead) return;
            _isDead = true;
            _shouldMove = false;

            EnemyComboManager.Instance?.OnEnemyDied(this);
            BattleManager.Instance?.OnEnemyDied(_expOnDeath);

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