using UnityEngine;
using DG.Tweening;
using System.Collections;

namespace SENTRY
{
    /// <summary>
    /// 배틀 필드에서 등장하는 적 캐릭터.
    ///
    /// [AI 동작 흐름]
    ///   FindTarget()     → SentryLive 레이어 내 가장 가까운 센트리 탐색
    ///   HandleBattleAI() → 타겟 방향으로 이동, 공격 범위 내 진입 시 공격
    ///   TryAttack()      → SentryBase.TakeDamage() 호출
    ///
    /// [HP 표시]
    ///   Enemy 자체에 HP UI 없음. EnemyBattleUIManager가 전담합니다.
    ///   TakeDamage() 호출 시 EnemyBattleUIManager.OnHpChanged()를 통해 갱신합니다.
    ///
    /// [레이어 기반 탐색]
    ///   FindTarget()에서 _sentryLayer(SentryLive) 레이어만 탐색합니다.
    ///   SentryDead / SentryCombo 레이어는 Physics2D 결과에서 자동 제외됩니다.
    ///
    /// [타겟 사망 처리]
    ///   TakeDamage 후 센트리가 KO → SentryBase가 레이어를 SentryDead로 변경
    ///   → 다음 FindTarget()에서 자동 제외됩니다.
    ///   공격 연출은 null 체크 후 계속 실행됩니다.
    ///
    /// [콤보 순번]
    ///   EnemyComboManager.SetComboTurn(bool)으로 순번 설정.
    ///   _isMyTurn = false이면 TryAttack()을 건너뜁니다 (단독 모드 제외).
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

        [Header("타겟 갱신")]
        [Tooltip("타겟 센트리를 재탐색하는 주기 (초)")]
        [SerializeField] private float _targetRefreshInterval = 0.5f;

        [Header("경험치 설정")]
        [Tooltip("사망 시 센트리들에게 분배할 경험치 양")]
        [SerializeField] private int _expOnDeath = 30;

        [Header("레이어 설정")]
        [Tooltip("탐색할 센트리 레이어. SentryLive를 설정하세요.\n" +
                 "SentryDead / SentryCombo 레이어는 자동으로 제외됩니다.")]
        [SerializeField] private LayerMask _sentryLayer;

        [Header("페이크 쿼터뷰 설정")]
        [Tooltip("Y 위치 기반 SortingOrder 갱신 배율")]
        [SerializeField] private float _sortingOrderScale = 10f;

        [Header("피격 넉백 설정")]
        [Tooltip("Strike 피격 시 밀려나는 거리 (DOMove — Kinematic 안전)")]
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

        /// <summary>기절 여부. WallSentry 스킬 연동.</summary>
        private bool _isStunned = false;

        /// <summary>
        /// 콤보 순번 여부.
        /// EnemyComboManager.SetComboTurn()으로 설정됩니다.
        /// 단독 모드이면 항상 true로 동작합니다.
        /// </summary>
        private bool _isMyTurn = true;

        /// <summary>마지막 공격 시각</summary>
        private float _lastAttackTime;

        /// <summary>마지막 타겟 재탐색 시각</summary>
        private float _lastTargetRefreshTime;

        /// <summary>FixedUpdate에서 사용할 이동 방향 벡터</summary>
        private Vector2 _moveDir = Vector2.zero;

        /// <summary>이번 FixedUpdate에서 이동 여부</summary>
        private bool _shouldMove = false;

        /// <summary>Rigidbody2D 캐시</summary>
        private Rigidbody2D _rigid2D;

        /// <summary>SpriteRenderer 캐시</summary>
        private SpriteRenderer _spriteRenderer;

        // ─────────────────────────────────────────
        //  외부 공개 프로퍼티
        // ─────────────────────────────────────────

        public bool IsDead => _isDead;
        public int CurrentHp => _currentHp;
        public int MaxHp => _maxHp;
        public int Level => _level;

        // ─────────────────────────────────────────
        //  초기화
        // ─────────────────────────────────────────

        /// <summary>EnemySpawner가 생성 직후 호출합니다.</summary>
        public void Init(Transform ignored = null)
        {
            _currentTarget = null;
            _lastTargetRefreshTime = 0f;
            _isMyTurn = true;
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
            if (_isDead || _isStunned)
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

            // 현재 타겟 유효성 검사 — KO 또는 SentryDead 레이어
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
            if (_isDead || _isStunned || !_shouldMove) return;
            if (_rigid2D != null)
                _rigid2D.MovePosition(
                    _rigid2D.position + _moveDir * _moveSpeed * Time.fixedDeltaTime);
        }

        // ─────────────────────────────────────────
        //  타겟 탐색
        // ─────────────────────────────────────────

        /// <summary>
        /// SentryLive 레이어 내 가장 가까운 센트리를 타겟으로 설정합니다.
        /// SentryDead / SentryCombo 레이어는 Physics2D가 자동 제외합니다.
        /// Inspector에서 _sentryLayer를 SentryLive 레이어로 설정하세요.
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
        ///
        /// [콤보 순번] _isMyTurn = false 이면 건너뜁니다 (단독 모드 제외).
        /// [타겟 사망] TakeDamage 후 센트리가 KO되어도 연출은 null 체크 후 계속 실행됩니다.
        /// </summary>
        private void TryAttack()
        {
            if (Time.time < _lastAttackTime + _attackCooldown) return;

            // 콤보 순번 체크
            if (EnemyComboManager.Instance != null &&
                !EnemyComboManager.Instance.IsSingleMode() &&
                !_isMyTurn)
                return;

            // 공격 시작 시점 타겟 유효성 확인
            if (_currentTarget == null || _currentTarget.IsKnockedOut)
            {
                _currentTarget = null;
                return;
            }

            _lastAttackTime = Time.time;

            // 데미지 적용 (이후 센트리가 KO될 수 있음)
            _currentTarget.TakeDamage(_attackDamage);

            // 연출 — null 체크로 NullRef 방지
            if (_currentTarget != null)
            {
                Vector3 punchDir =
                    (_currentTarget.transform.position - transform.position).normalized * 0.3f;
                transform.DOPunchPosition(punchDir, 0.18f, 5, 0.4f);

                Debug.Log($"[{name}] → [{_currentTarget.SentryName}] 공격! " +
                          $"데미지: {_attackDamage}");
            }

            // 콤보 순번 넘김
            EnemyComboManager.Instance?.AdvanceTurn();
        }

        // ─────────────────────────────────────────
        //  피격 처리
        // ─────────────────────────────────────────

        /// <summary>
        /// 센트리 공격을 받았을 때 호출됩니다.
        /// HP 변경 후 EnemyBattleUIManager에 갱신을 알립니다.
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

            // HP 변경을 EnemyBattleUIManager에 통보 — UI 갱신 전담
            EnemyBattleUIManager.Instance?.OnHpChanged(this);

            if (_currentHp <= 0) Die();
        }

        // ─────────────────────────────────────────
        //  기절 (WallSentry 스킬 연동)
        // ─────────────────────────────────────────

        /// <summary>WallSentry 스킬로 적을 기절시킵니다. duration초 후 자동 해제.</summary>
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
        ///
        /// [처리 순서]
        ///   1. _isDead = true → 이후 모든 Update / 공격 차단
        ///   2. EnemyComboManager.OnEnemyDied() → 콤보 순번 정리 + EnemyBattleUI KO 아이콘
        ///   3. BattleManager.OnEnemyDied() → 킬 카운트 + 경험치 분배
        ///   4. DOTween 사망 연출 → Destroy
        /// </summary>
        private void Die()
        {
            if (_isDead) return;
            _isDead = true;
            _shouldMove = false;

            // 콤보 순번 정리 + EnemyBattleUIManager KO 아이콘 표시
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