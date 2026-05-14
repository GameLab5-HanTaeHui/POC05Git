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
        [SerializeField] private float _moveSpeed = 3f;

        [Tooltip("플레이어를 인식하는 범위")]
        [SerializeField] private float _detectionRange = 10f;

        [Tooltip("플레이어에게 근접 공격을 가하는 사거리")]
        [SerializeField] private float _attackRange = 1.5f;

        [Header("공격 설정")]
        [Tooltip("플레이어에게 가하는 데미지")]
        [SerializeField] private int _attackDamage = 10;

        [Tooltip("공격 쿨타임 (초)")]
        [SerializeField] private float _attackCooldown = 1f;

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
                 "기본값 10 → Y가 1 낮아질수록 SortingOrder가 10 높아져 앞에 그려집니다.")]
        [SerializeField] private float _sortingOrderScale = 10f;

        // ─────────────────────────────────────────
        //  내부 상태 변수
        // ─────────────────────────────────────────

        /// <summary>추적할 플레이어 Transform. Init()으로 설정됩니다.</summary>
        private Transform _player;

        /// <summary>플레이어 체력 컴포넌트 캐시. 공격 전 생존 여부 확인에 사용.</summary>
        private PlayerHealth _playerHealth;

        /// <summary>현재 HP</summary>
        private int _currentHp;

        /// <summary>사망 여부 (중복 Die() 방지)</summary>
        private bool _isDead = false;

        /// <summary>
        /// 기절(스턴) 여부.
        /// true 이면 모든 AI 동작이 멈춥니다. WallSentry 스킬로 설정됩니다.
        /// </summary>
        private bool _isStunned = false;

        /// <summary>마지막 공격 시각</summary>
        private float _lastAttackTime;

        /// <summary>Rigidbody2D 캐시 (MovePosition 이동에 사용)</summary>
        private Rigidbody2D _rigid2D;

        /// <summary>SpriteRenderer 캐시 (방향 플립 + SortingOrder 깊이 정렬)</summary>
        private SpriteRenderer _spriteRenderer;

        // ─────────────────────────────────────────
        //  초기화
        // ─────────────────────────────────────────

        /// <summary>
        /// 적을 초기화합니다. EnemySpawner가 생성 직후 호출합니다.
        /// </summary>
        /// <param name="playerTransform">플레이어 Transform</param>
        public void Init(Transform playerTransform)
        {
            _player = playerTransform;
            if (_player != null)
                _playerHealth = _player.GetComponentInChildren<PlayerHealth>();
        }

        // ─────────────────────────────────────────
        //  유니티 생명주기
        // ─────────────────────────────────────────

        private void Start()
        {
            _currentHp = _maxHp;
            _rigid2D = GetComponent<Rigidbody2D>();
            _spriteRenderer = GetComponent<SpriteRenderer>();

            // HP 바 초기화
            if (_hpFillSprite != null)
                _hpFillSprite.transform.localScale = Vector3.one;

            // 수동 배치된 적을 위한 플레이어 자동 탐색
            if (_player == null)
            {
                GameObject pObj = GameObject.FindGameObjectWithTag("Player");
                if (pObj != null) Init(pObj.transform);
            }
        }

        private void Update()
        {
            // 사망 / 기절 / 플레이어 없음 → AI 정지
            if (_isDead || _isStunned || _player == null) return;

            // ── Y 기반 SortingOrder 갱신 (페이크 쿼터뷰 깊이 정렬) ──
            // Y가 낮을수록(화면 아래 = 가까이) 앞에 그려집니다.
            if (_spriteRenderer != null)
                _spriteRenderer.sortingOrder =
                    Mathf.RoundToInt(-transform.position.y * _sortingOrderScale);

            float distToPlayer = Vector2.Distance(transform.position, _player.position);

            if (distToPlayer <= _detectionRange)
                ChasePlayer();
        }

        // ─────────────────────────────────────────
        //  AI: 플레이어 추적 및 공격
        // ─────────────────────────────────────────

        /// <summary>
        /// 플레이어를 추적하고, 공격 범위 안에 들어오면 공격합니다.
        ///
        /// [페이크 쿼터뷰 대응]
        ///   linearVelocityX 직접 할당 → Rigidbody2D.MovePosition()으로 변경.
        ///   Y축은 깊이 역할이므로 X/Y 모두 이동에 사용합니다.
        ///   점프 로직(AddForce, _isGrounded)은 완전 제거했습니다.
        /// </summary>
        private void ChasePlayer()
        {
            float distToPlayer = Vector2.Distance(transform.position, _player.position);

            // ── 1. 플레이어 공격 ──
            if (distToPlayer <= _attackRange)
            {
                // 플레이어가 살아 있을 때만 공격
                if (_playerHealth != null && !_playerHealth.IsDead)
                {
                    if (Time.time >= _lastAttackTime + _attackCooldown)
                    {
                        _playerHealth.TakeDamage(_attackDamage);
                        _lastAttackTime = Time.time;

                        Vector3 punchDir =
                            (_player.position - transform.position).normalized * 0.4f;
                        transform.DOPunchPosition(punchDir, 0.2f, 5, 0.5f);

                        Debug.Log($"[{name}] 플레이어 공격! 데미지: {_attackDamage}");
                    }
                }
                return;
            }

            // ── 2. 플레이어를 향해 이동 (MovePosition 기반) ──
            // 페이크 쿼터뷰: X(좌우) + Y(깊이 원근) 모두 이동
            Vector2 dir = ((Vector2)_player.position
                           - (Vector2)transform.position).normalized;

            if (_rigid2D != null)
                _rigid2D.MovePosition(_rigid2D.position + dir * _moveSpeed * Time.fixedDeltaTime);

            // 이동 방향에 따라 스프라이트 좌우 반전
            if (_spriteRenderer != null)
                _spriteRenderer.flipX = dir.x < 0;
        }

        // ─────────────────────────────────────────
        //  물리 충돌 (몸빵 데미지)
        // ─────────────────────────────────────────

        /// <summary>
        /// 플레이어와 충돌 시 쿨타임마다 데미지를 줍니다.
        /// (OnCollisionStay2D는 Kinematic 간 충돌에서 동작하지 않을 수 있음 — 참고)
        /// </summary>
        private void OnCollisionStay2D(Collision2D collision)
        {
            if (!collision.gameObject.CompareTag("Player")) return;
            if (_playerHealth == null || _playerHealth.IsDead) return;

            if (Time.time >= _lastAttackTime + _attackCooldown)
            {
                _playerHealth.TakeDamage(_attackDamage);
                _lastAttackTime = Time.time;
            }
        }

        // ─────────────────────────────────────────
        //  피격 처리
        // ─────────────────────────────────────────

        /// <summary>
        /// 외부(센트리 공격, 탄환 등)에서 호출하여 데미지를 입힙니다.
        /// </summary>
        /// <param name="damage">입힐 데미지</param>
        /// <param name="hitType">피격 타입 (Strike=강한 밀침, Shoot=약한 밀림)</param>
        /// <param name="hitSourcePos">공격 발원 위치 (밀침 방향 계산용)</param>
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

            // 피격 방향으로 DOMove 밀림 연출 (Kinematic 안전)
            // Strike는 강하게, Shoot은 약하게 밀려납니다.
            float knockDist = hitType == HitType.Strike ? 1.0f : 0.4f;
            Vector3 knockDir =
                (transform.position - hitSourcePos).normalized;
            Vector3 knockTarget = transform.position + knockDir * knockDist;

            transform.DOMove(knockTarget, 0.15f).SetEase(Ease.OutQuart);

            UpdateHpBar();

            if (_currentHp <= 0) Die();
        }

        // ─────────────────────────────────────────
        //  기절 (WallSentry 스킬 연동)
        // ─────────────────────────────────────────

        /// <summary>
        /// WallSentry 스킬 연동으로 적을 기절시킵니다.
        /// duration초 후 자동으로 기절이 해제됩니다.
        /// </summary>
        /// <param name="duration">기절 지속 시간 (초)</param>
        public void Stun(float duration)
        {
            if (_isDead) return;
            StartCoroutine(StunRoutine(duration));
        }

        /// <summary>기절 상태를 duration초 유지 후 해제합니다.</summary>
        private IEnumerator StunRoutine(float duration)
        {
            _isStunned = true;

            if (_spriteRenderer != null)
                _spriteRenderer.DOColor(Color.cyan, 0.2f);

            yield return new WaitForSeconds(duration);

            _isStunned = false;

            if (_spriteRenderer != null && !_isDead)
                _spriteRenderer.DOColor(Color.white, 0.2f);

            Debug.Log($"[{name}] 기절 해제");
        }

        // ─────────────────────────────────────────
        //  HP 바 갱신
        // ─────────────────────────────────────────

        /// <summary>현재 HP 비율에 맞게 HP 바 스프라이트 X 스케일을 조정합니다.</summary>
        private void UpdateHpBar()
        {
            if (_hpFillSprite == null) return;

            float ratio = (float)_currentHp / _maxHp;
            _hpFillSprite.transform.localScale =
                new Vector3(ratio, 1f, 1f);
        }

        // ─────────────────────────────────────────
        //  사망 처리
        // ─────────────────────────────────────────

        /// <summary>
        /// 적이 사망할 때 호출됩니다.
        /// BattleManager에 킬 카운트와 경험치를 알리고 오브젝트를 파괴합니다.
        /// </summary>
        private void Die()
        {
            if (_isDead) return;
            _isDead = true;

            if (_hpBarGroup != null)
                _hpBarGroup.SetActive(false);

            // DOTween 사망 연출
            transform.DOScale(Vector3.zero, 0.3f)
                     .SetEase(Ease.InBack)
                     .OnComplete(() => Destroy(gameObject));

            if (_spriteRenderer != null)
                _spriteRenderer.DOFade(0f, 0.25f);

            // BattleManager에 사망 통보 → 경험치 분배 + 킬 카운트
            BattleManager.Instance?.OnEnemyDied(_expOnDeath);

            Debug.Log($"[{name}] 사망! 경험치 보상: {_expOnDeath}");
        }
    }
}