using UnityEngine;
using DG.Tweening;
using System.Collections;

namespace SENTRY
{
    /// <summary>
    /// 배틀 필드에서 등장하는 적 캐릭터.
    ///
    /// [AI 동작 흐름]
    ///   FindTarget()     → 씬 내 생존 센트리 중 가장 가까운 센트리 탐색
    ///   HandleBattleAI() → 타겟 방향으로 이동, 공격 범위 내 진입 시 공격
    ///   TryAttack()      → SentryBase.TakeDamage() 호출
    ///
    /// [타겟 사망 처리 정책]
    ///   - 공격 모션이 시작된 경우 공격은 끝까지 실행 (TryAttack 내 IsKnockedOut 체크를 공격 후로 이동)
    ///   - 사망한 센트리는 "Dead" 태그를 달아 RefreshTarget()의 탐색에서 제외
    ///   - 이 방식으로 "공격 중 타겟 소멸" NullRef를 방지합니다.
    ///
    /// [콤보 순번 시스템]
    ///   - EnemyComboManager.Initialize(comboCount)로 순번 모드 결정
    ///   - SetComboTurn(bool)으로 이 Enemy의 공격 순번 여부를 설정
    ///   - _isMyTurn이 false이면 TryAttack()을 건너뜁니다 (단독 모드 제외)
    /// </summary>

    /// <summary>피격 타입 열거형. Strike = 강한 넉백, Shoot = 약한 밀림</summary>
    public enum HitType { Strike, Shoot }

    public class Enemy : MonoBehaviour
    {
        // ─────────────────────────────────────────
        //  Inspector 노출 필드
        // ─────────────────────────────────────────

        [Header("기본 스탯")]
        [Tooltip("레벨")]
        [SerializeField] private int _level = 0;

        [Tooltip("최대 체력")]
        [SerializeField] private int _maxHp = 100;

        [Tooltip("이동 속도 (MovePosition 기반 — units/sec)")]
        [SerializeField] private float _moveSpeed = 2.5f;

        [Tooltip("센트리 탐지 최대 반경. 배틀 필드 대각선 길이보다 크게 설정하세요.")]
        [SerializeField] private float _detectionRange = 30f;

        [Tooltip("근접 공격이 닿는 거리")]
        [SerializeField] private float _attackRange = 1.2f;

        [Header("공격 설정")]
        [Tooltip("센트리에게 가하는 기본 데미지")]
        [SerializeField] private int _attackDamage = 15;

        [Tooltip("공격 쿨타임 (초)")]
        [SerializeField] private float _attackCooldown = 1.2f;

        [Header("타겟 갱신")]
        [Tooltip("타겟 센트리를 재탐색하는 주기 (초).")]
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
        [Tooltip("Y 위치 기반 SortingOrder 갱신 배율.")]
        [SerializeField] private float _sortingOrderScale = 10f;

        [Header("피격 넉백 설정")]
        [Tooltip("Strike 피격 시 밀려나는 거리 (DOMove — Kinematic 안전)")]
        [SerializeField] private float _strikeKnockDistance = 1.2f;

        [Tooltip("Shoot 피격 시 밀려나는 거리")]
        [SerializeField] private float _shootKnockDistance = 0.4f;

        [Tooltip("넉백 이동 소요 시간 (초)")]
        [SerializeField] private float _knockDuration = 0.18f;

        [Header("태그 설정")]
        [Tooltip("정상 상태일 때 태그. 탐색 필터링에 사용됩니다.")]
        [SerializeField] private string _aliveTag = "Enemy";

        [Tooltip("사망 시 변경할 태그. RefreshTarget()에서 이 태그를 가진 오브젝트는 제외합니다.")]
        [SerializeField] private string _deadTag = "Dead";

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
        /// _isSingleMode이면 항상 true로 동작합니다.
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

        /// <summary>사망 여부</summary>
        public bool IsDead => _isDead;

        /// <summary>현재 HP</summary>
        public int CurrentHp => _currentHp;

        /// <summary>최대 HP</summary>
        public int MaxHp => _maxHp;

        /// <summary>레벨 (EnemyBattleUIManager 표시용)</summary>
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
        /// EnemyComboManager가 이 Enemy의 공격 순번을 설정합니다.
        /// isMyTurn = true 이면 공격 가능, false 이면 TryAttack()을 건너뜁니다.
        /// 단독 모드(comboCount=1)에서는 항상 true로 유지됩니다.
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

            // Y 기반 SortingOrder 갱신 (페이크 쿼터뷰 깊이 정렬)
            if (_spriteRenderer != null)
                _spriteRenderer.sortingOrder =
                    Mathf.RoundToInt(-transform.position.y * _sortingOrderScale);

            // 주기적 타겟 재탐색
            if (Time.time >= _lastTargetRefreshTime + _targetRefreshInterval)
            {
                _lastTargetRefreshTime = Time.time;
                RefreshTarget();
            }

            // 현재 타겟 유효성 검사 — KO 또는 Dead 태그
            if (_currentTarget == null ||
                _currentTarget.IsKnockedOut ||
                _currentTarget.CompareTag(_deadTag))
            {
                RefreshTarget();
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
        /// 씬 내 생존 센트리 중 가장 가까운 센트리를 타겟으로 설정합니다.
        /// KO 상태이거나 "Dead" 태그를 가진 센트리는 탐색에서 제외합니다.
        /// </summary>
        private void RefreshTarget()
        {
            SentryBase[] sentries =
                FindObjectsByType<SentryBase>(FindObjectsSortMode.None);

            SentryBase nearest = null;
            float minDist = _detectionRange;

            foreach (SentryBase sentry in sentries)
            {
                // KO 상태 또는 Dead 태그 — 타겟 제외
                if (sentry.IsKnockedOut) continue;
                if (sentry.CompareTag(_deadTag)) continue;

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
        /// 쿨타임이 지났으면 타겟 센트리에게 데미지를 입힙니다.
        ///
        /// [콤보 순번]
        ///   _isMyTurn이 false이면 공격을 건너뜁니다.
        ///   EnemyComboManager.IsSingleMode()가 true이면 순번 무관하게 공격합니다.
        ///
        /// [타겟 사망 처리]
        ///   공격 시작 시점에 이미 KO라면 건너뜁니다.
        ///   데미지를 입힌 후 타겟이 KO되더라도 공격 자체는 완료로 처리합니다.
        ///   AdvanceTurn()은 공격 완료 후 항상 호출합니다.
        /// </summary>
        private void TryAttack()
        {
            if (Time.time < _lastAttackTime + _attackCooldown) return;

            // 콤보 순번 체크 (단독 모드이면 순번 무시)
            if (EnemyComboManager.Instance != null &&
                !EnemyComboManager.Instance.IsSingleMode() &&
                !_isMyTurn)
                return;

            // 공격 시작 시점에 타겟 유효성 확인
            if (_currentTarget == null ||
                _currentTarget.IsKnockedOut ||
                _currentTarget.CompareTag(_deadTag))
            {
                _currentTarget = null;
                return;
            }

            _lastAttackTime = Time.time;

            // 공격 실행 — 데미지 적용 후 타겟이 KO되더라도 아래 코드는 계속 실행됩니다.
            _currentTarget.TakeDamage(_attackDamage);

            // 타격 연출
            if (_currentTarget != null)
            {
                Vector3 punchDir =
                    (_currentTarget.transform.position - transform.position).normalized * 0.3f;
                transform.DOPunchPosition(punchDir, 0.18f, 5, 0.4f);

                Debug.Log($"[{name}] → [{_currentTarget.SentryName}] 공격! " +
                          $"데미지: {_attackDamage}");
            }

            // 콤보 순번 다음으로 넘김
            EnemyComboManager.Instance?.AdvanceTurn();
        }

        // ─────────────────────────────────────────
        //  피격 처리
        // ─────────────────────────────────────────

        /// <summary>
        /// 센트리 공격을 받았을 때 호출됩니다.
        /// HitType으로 Strike(강한 넉백) / Shoot(약한 밀림)을 구분합니다.
        /// </summary>
        public void TakeDamage(int damage, HitType hitType, Vector3 hitSourcePos)
        {
            if (_isDead || _isStunned) return;

            _currentHp -= damage;
            _currentHp = Mathf.Max(_currentHp, 0);

            transform.DOShakePosition(0.15f, 0.2f, 10, 90f);

            if (_spriteRenderer != null)
                _spriteRenderer.DOColor(Color.red, 0.05f)
                    .SetLoops(4, LoopType.Yoyo)
                    .OnComplete(() =>
                    {
                        if (_spriteRenderer != null)
                            _spriteRenderer.color = Color.white;
                    });

            float knockDist = hitType == HitType.Strike
                ? _strikeKnockDistance
                : _shootKnockDistance;

            if (knockDist > 0f)
            {
                Vector3 knockDir = (transform.position - hitSourcePos).normalized;
                Vector3 knockTarget = transform.position + knockDir * knockDist;
                transform.DOMove(knockTarget, _knockDuration).SetEase(Ease.OutQuart);
            }

            UpdateHpBar();

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
        //  HP 바 갱신
        // ─────────────────────────────────────────

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
        ///
        /// [처리 순서]
        ///   1. _isDead = true → 이후 모든 Update/공격 차단
        ///   2. tag = _deadTag → RefreshTarget()에서 타겟 후보 제외
        ///   3. EnemyComboManager.OnEnemyDied() → 콤보 순번 및 EnemyBattleUI 연동
        ///   4. BattleManager.OnEnemyDied() → 킬 카운트 + 경험치 분배
        ///   5. DOTween 사망 연출 → Destroy
        /// </summary>
        private void Die()
        {
            if (_isDead) return;
            _isDead = true;
            _shouldMove = false;

            // Dead 태그 — 다른 Enemy의 RefreshTarget에서 이 센트리를 제외합니다.
            // (센트리가 이 적을 타겟으로 삼는 경우도 마찬가지)
            gameObject.tag = _deadTag;

            if (_hpBarGroup != null) _hpBarGroup.SetActive(false);

            // 콤보 순번 및 EnemyBattleUIManager KO 아이콘 연동
            EnemyComboManager.Instance?.OnEnemyDied(this);

            // 킬 카운트 + 경험치 분배
            BattleManager.Instance?.OnEnemyDied(_expOnDeath);

            // DOTween 사망 연출 → 파괴
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