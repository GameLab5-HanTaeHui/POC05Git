using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;
using System.Collections;

namespace SENTRY
{
    /// <summary>
    /// 배틀 필드에서 등장하는 적 캐릭터.
    ///
    /// [변경 사항 - 페이크 쿼터뷰 대응]
    /// - ChasePlayer()의 linearVelocityX 직접 할당을
    ///   Rigidbody2D.MovePosition()으로 교체했습니다.
    /// - 점프 로직(AddForce, linearVelocityY)은 페이크 쿼터뷰 모드에서
    ///   의미가 없으므로 완전히 제거했습니다.
    ///   (배틀 필드는 중력 없음 — Y축 = 깊이 역할)
    /// - Update()에서 Y 위치 기반 SortingOrder를 매 프레임 갱신합니다.
    ///   (Y가 낮을수록 가까이 = 앞에 그려집니다.)
    ///
    /// [히어라키 위치]
    /// BattleField
    ///   └── Enemy (프리팹 — EnemySpawner가 동적 생성)
    ///         ├── Enemy (이 스크립트)
    ///         ├── Rigidbody2D  (gravityScale=0, Kinematic — EnemySpawner가 설정)
    ///         ├── Collider2D
    ///         └── SpriteRenderer
    ///               └── HpBarGroup
    ///                     └── HpFillSprite
    /// </summary>

    /// <summary>
    /// 피격 타입 열거형.
    /// Strike = 근접/타격형 (강한 넉백), Shoot = 원거리/사격형 (약한 밀림)
    /// </summary>
    public enum HitType { Strike, Shoot }

    public class Enemy : MonoBehaviour
    {
        // ─────────────────────────────────────────
        //  Inspector 노출 필드
        // ─────────────────────────────────────────

        [Header("기본 스탯")]
        [Tooltip("최대 체력")]
        [SerializeField] private int _maxHp = 100;

        [Tooltip("이동 속도 (MovePosition 기반 — units/sec)")]
        [SerializeField] private float _moveSpeed = 2.5f;

        [Tooltip("센트리 탐지 최대 반경.\n" +
                 "배틀 필드 대각선 길이보다 크게 설정하세요.")]
        [SerializeField] private float _detectionRange = 30f;

        [Tooltip("근접 공격이 닿는 거리")]
        [SerializeField] private float _attackRange = 1.2f;

        [Header("공격 설정")]
        [Tooltip("센트리에게 가하는 기본 데미지")]
        [SerializeField] private int _attackDamage = 15;

        [Tooltip("공격 쿨타임 (초)")]
        [SerializeField] private float _attackCooldown = 1.2f;

        [Header("타겟 갱신")]
        [Tooltip("타겟 센트리를 재탐색하는 주기 (초).\n" +
                 "낮을수록 가까운 센트리로 빠르게 교체합니다.")]
        [SerializeField] private float _targetRefreshInterval = 0.5f;

        [Header("경험치 설정")]
        [Tooltip("사망 시 센트리들에게 분배할 경험치 양")]
        [SerializeField] private int _expOnDeath = 30;

        [Header("HP 바 UI (스프라이트 방식)")]
        [Tooltip("HP 비율에 따라 X 스케일이 줄어드는 스프라이트")]
        [SerializeField] private SpriteRenderer _hpFillSprite;

        [Tooltip("HP 바 전체 그룹 오브젝트 (사망 시 비활성화)")]
        [SerializeField] private GameObject _hpBarGroup;

        [Header("페이크 쿼터뷰 설정")]
        [Tooltip("Y 위치 기반 SortingOrder 갱신 배율.\n" +
                 "Y가 1 낮아질수록 SortingOrder가 이 값만큼 높아져 앞에 그려집니다.")]
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

        /// <summary>
        /// 기절(스턴) 여부.
        /// true이면 이동·공격 AI가 모두 멈춥니다.
        /// WallSentry 스킬 연동으로 설정됩니다.
        /// </summary>
        private bool _isStunned = false;

        /// <summary>마지막 공격 시각</summary>
        private float _lastAttackTime;

        /// <summary>마지막 타겟 재탐색 시각</summary>
        private float _lastTargetRefreshTime;

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

        // ─────────────────────────────────────────
        //  초기화
        // ─────────────────────────────────────────

        /// <summary>
        /// 적을 초기화합니다. EnemySpawner가 생성 직후 호출합니다.
        ///
        /// [변경 사항]
        ///   기존의 playerTransform 파라미터는 더 이상 의미가 없습니다.
        ///   Enemy는 씬 내 생존 센트리를 자동 탐색합니다.
        ///   EnemySpawner 시그니처 유지를 위해 파라미터를 받지만 무시합니다.
        /// </summary>
        /// <param name="ignored">사용되지 않는 파라미터 (EnemySpawner 호환용)</param>
        public void Init(Transform ignored = null)
        {
            _currentTarget = null;
            _lastTargetRefreshTime = 0f;
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

            if (_hpFillSprite != null)
                _hpFillSprite.transform.localScale = Vector3.one;
        }

        private void Update()
        {
            if (_isDead || _isStunned)
            {
                _shouldMove = false;
                return;
            }

            // ── Y 기반 SortingOrder 갱신 ──
            if (_spriteRenderer != null)
                _spriteRenderer.sortingOrder =
                    Mathf.RoundToInt(-transform.position.y * _sortingOrderScale);

            // ── 주기적 타겟 재탐색 ──
            if (Time.time >= _lastTargetRefreshTime + _targetRefreshInterval)
            {
                _lastTargetRefreshTime = Time.time;
                RefreshTarget();
            }

            // ── 현재 타겟 유효성 검사 ──
            if (_currentTarget == null || _currentTarget.IsKnockedOut)
            {
                RefreshTarget();
                if (_currentTarget == null)
                {
                    // 생존 센트리 없음 → 이동 중단 대기
                    _shouldMove = false;
                    return;
                }
            }

            HandleBattleAI();
        }

        private void FixedUpdate()
        {
            // MovePosition은 FixedUpdate에서 호출해야 물리 보간이 올바르게 적용됩니다.
            if (_isDead || _isStunned || !_shouldMove) return;
            if (_rigid2D != null)
                _rigid2D.MovePosition(
                    _rigid2D.position + _moveDir * _moveSpeed * Time.fixedDeltaTime);
        }

        // ─────────────────────────────────────────
        //  타겟 탐색
        // ─────────────────────────────────────────

        /// <summary>
        /// 씬 내 생존 중인 센트리 중 가장 가까운 센트리를 타겟으로 설정합니다.
        ///
        /// [탐색 대상]
        ///   StrikeSentry, ShootSentry, WallSentry 모두 SentryBase를 상속하므로
        ///   FindObjectsByType&lt;SentryBase&gt;()로 한 번에 탐색합니다.
        ///   KO 상태(_isKnockedOut)인 센트리는 제외합니다.
        /// </summary>
        private void RefreshTarget()
        {
            SentryBase[] sentries =
                FindObjectsByType<SentryBase>(FindObjectsSortMode.None);

            SentryBase nearest = null;
            float minDist = _detectionRange;

            foreach (SentryBase sentry in sentries)
            {
                if (sentry.IsKnockedOut) continue;

                float dist = Vector2.Distance(
                    transform.position, sentry.transform.position);

                if (dist < minDist)
                {
                    minDist = dist;
                    nearest = sentry;
                }
            }

            _currentTarget = nearest;
        }

        // ─────────────────────────────────────────
        //  전투 AI
        // ─────────────────────────────────────────

        /// <summary>
        /// 타겟 센트리를 향해 이동하고, 공격 범위 내 진입 시 공격을 시도합니다.
        /// 이동 방향은 _moveDir에 저장하며 FixedUpdate에서 MovePosition으로 적용합니다.
        /// </summary>
        private void HandleBattleAI()
        {
            if (_currentTarget == null) { _shouldMove = false; return; }

            float dist = Vector2.Distance(
                transform.position, _currentTarget.transform.position);

            if (dist <= _attackRange)
            {
                // 공격 범위 내 — 이동 정지 후 공격
                _shouldMove = false;
                _moveDir = Vector2.zero;
                TryAttack();
            }
            else
            {
                // 타겟 방향으로 이동
                _moveDir = ((Vector2)_currentTarget.transform.position
                               - (Vector2)transform.position).normalized;
                _shouldMove = true;

                // 이동 방향에 따라 스프라이트 좌우 반전
                if (_spriteRenderer != null)
                    _spriteRenderer.flipX = _moveDir.x < 0;
            }
        }

        // ─────────────────────────────────────────
        //  기본 공격
        // ─────────────────────────────────────────

        /// <summary>
        /// 쿨타임이 지났으면 타겟 센트리에게 데미지를 입힙니다.
        /// SentryBase.TakeDamage()를 직접 호출합니다.
        /// </summary>
        private void TryAttack()
        {
            if (Time.time < _lastAttackTime + _attackCooldown) return;
            if (_currentTarget == null || _currentTarget.IsKnockedOut) return;

            _lastAttackTime = Time.time;

            _currentTarget.TakeDamage(_attackDamage);

            // 공격 방향 찌르기 연출
            Vector3 punchDir =
                (_currentTarget.transform.position - transform.position).normalized * 0.3f;
            transform.DOPunchPosition(punchDir, 0.18f, 5, 0.4f);

            Debug.Log($"[{name}] → [{_currentTarget.SentryName}] 공격! " +
                      $"데미지: {_attackDamage}");
        }

        // ─────────────────────────────────────────
        //  피격 처리
        // ─────────────────────────────────────────

        /// <summary>
        /// 센트리 공격(근접/원거리)을 받았을 때 호출됩니다.
        ///
        /// [넉백 처리 — Kinematic 안전]
        ///   Kinematic 상태에서 AddForce는 동작하지 않으므로
        ///   DOTween.DOMove로 밀려나는 방향으로 이동시킵니다.
        ///   Strike(강한 근접) = 멀리, Shoot(원거리 탄환) = 짧게 밀립니다.
        /// </summary>
        /// <param name="damage">입힐 데미지</param>
        /// <param name="hitType">피격 타입 (Strike / Shoot)</param>
        /// <param name="hitSourcePos">공격 발원 위치 (넉백 방향 계산용)</param>
        public void TakeDamage(int damage, HitType hitType, Vector3 hitSourcePos)
        {
            if (_isDead || _isStunned) return;

            _currentHp -= damage;
            _currentHp = Mathf.Max(_currentHp, 0);

            // 피격 연출 — 빨간 플래시 + 흔들림
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
                Vector3 knockDir =
                    (transform.position - hitSourcePos).normalized;
                Vector3 knockTarget =
                    transform.position + knockDir * knockDist;
                transform.DOMove(knockTarget, _knockDuration)
                    .SetEase(Ease.OutQuart);
            }

            UpdateHpBar();

            if (_currentHp <= 0) Die();
        }

        // ─────────────────────────────────────────
        //  기절 (WallSentry 스킬 연동)
        // ─────────────────────────────────────────

        /// <summary>
        /// WallSentry 스킬로 적을 기절시킵니다.
        /// duration초 후 자동으로 기절이 해제됩니다.
        /// 기절 중에는 이동·공격 AI가 모두 멈춥니다.
        /// </summary>
        /// <param name="duration">기절 지속 시간 (초)</param>
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
        //  HP 바 갱신
        // ─────────────────────────────────────────

        /// <summary>
        /// HP 비율에 맞게 HpFillSprite의 X 스케일을 DOTween으로 부드럽게 갱신합니다.
        /// </summary>
        private void UpdateHpBar()
        {
            if (_hpFillSprite == null) return;

            float ratio = _maxHp > 0 ? (float)_currentHp / _maxHp : 0f;

            _hpFillSprite.transform
                .DOScaleX(ratio, 0.2f)
                .SetEase(Ease.OutQuart);
        }

        // ─────────────────────────────────────────
        //  사망 처리
        // ─────────────────────────────────────────

        /// <summary>
        /// HP가 0이 되면 호출됩니다.
        /// DOTween 사망 연출 후 BattleManager에 킬 카운트와 경험치를 통보합니다.
        /// </summary>
        private void Die()
        {
            if (_isDead) return;
            _isDead = true;
            _shouldMove = false;

            if (_hpBarGroup != null) _hpBarGroup.SetActive(false);

            // 사망 연출 — 축소 → 파괴
            transform.DOKill();
            transform.DOScale(Vector3.zero, 0.3f)
                .SetEase(Ease.InBack)
                .OnComplete(() => Destroy(gameObject));

            if (_spriteRenderer != null)
                _spriteRenderer.DOFade(0f, 0.25f);

            BattleManager.Instance?.OnEnemyDied(_expOnDeath);

            Debug.Log($"[{name}] 사망! 경험치 보상: {_expOnDeath}");
        }
    }
}