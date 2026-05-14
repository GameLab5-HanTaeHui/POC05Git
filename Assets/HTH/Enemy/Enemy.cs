using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;
using System.Collections;

namespace SENTRY
{
    /// <summary>
    /// 배틀 필드에서 등장하는 적 캐릭터.
    ///
    /// [설계 의도]
    /// - 기존 Enemy.cs 구조를 최대한 유지하면서 새 컨셉에 맞게 보강합니다.
    /// - 플레이어를 직접 공격하는 로직은 유지합니다.
    ///   (플레이어가 배틀 필드에 함께 존재하기 때문)
    /// - WallSentry 스킬 연동을 위해 Stun() 메서드가 추가되었습니다.
    /// - 사망 시 GainExp를 통해 센트리들에게 경험치를 분배합니다.
    /// - HitType enum은 이 파일에 함께 정의합니다.
    ///
    /// [히어라키 위치]
    /// BattleField
    ///   └── Enemy (프리팹 - EnemySpawner가 동적 생성)
    ///         ├── Enemy (이 스크립트)
    ///         ├── Rigidbody2D
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

        [Tooltip("이동 속도")]
        [SerializeField] private float _moveSpeed = 3f;

        [Tooltip("점프 힘")]
        [SerializeField] private float _jumpForce = 7f;

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

        [Header("AI 설정")]
        [Tooltip("바닥 및 벽 감지에 사용할 레이어")]
        [SerializeField] private LayerMask _groundLayer;

        [Tooltip("점프 쿨타임 (초)")]
        [SerializeField] private float _jumpCooldown = 1f;

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

        /// <summary>땅에 닿아 있는지 여부 (점프 판단용)</summary>
        private bool _isGrounded;

        /// <summary>마지막 공격 시각</summary>
        private float _lastAttackTime;

        /// <summary>마지막 점프 시각</summary>
        private float _lastJumpTime;

        private Rigidbody2D _rigid2D;
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

            // 수동 배치된 적(스포너 없이 씬에 직접 배치)을 위한 플레이어 자동 탐색
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

            float distToPlayer = Vector2.Distance(transform.position, _player.position);

            if (distToPlayer <= _detectionRange)
                ChasePlayer();
            else
                _rigid2D.linearVelocityX = 0f;
        }

        // ─────────────────────────────────────────
        //  AI: 플레이어 추적 및 공격
        // ─────────────────────────────────────────

        /// <summary>
        /// 플레이어를 추적하고, 공격 범위 안에 들어오면 공격합니다.
        /// 경로 상 벽이 있으면 점프를 시도합니다.
        /// </summary>
        private void ChasePlayer()
        {
            float direction = _player.position.x > transform.position.x ? 1f : -1f;
            float distToPlayer = Vector2.Distance(transform.position, _player.position);

            // ── 1. 플레이어 공격 ──
            if (distToPlayer <= _attackRange)
            {
                _rigid2D.linearVelocityX = 0f;
                _spriteRenderer.flipX = direction < 0;

                // 플레이어가 살아 있을 때만 공격
                if (_playerHealth != null && !_playerHealth.IsDead)
                {
                    if (Time.time >= _lastAttackTime + _attackCooldown)
                    {
                        _playerHealth.TakeDamage(_attackDamage);
                        _lastAttackTime = Time.time;
                        transform.DOPunchPosition(Vector3.right * direction * 0.4f, 0.2f);
                        Debug.Log($"[{name}] 플레이어 공격! 데미지: {_attackDamage}");
                    }
                }
                return;
            }

            // ── 2. 이동 및 지형 인식 ──
            _rigid2D.linearVelocityX = direction * _moveSpeed;
            _spriteRenderer.flipX = direction < 0;

            _isGrounded = Physics2D.Raycast(transform.position, Vector2.down, 1.1f, _groundLayer);

            if (_isGrounded && Time.time >= _lastJumpTime + _jumpCooldown)
            {
                bool shouldJump = false;

                // 전방에 벽이 있으면 점프 시도
                bool wallAhead = Physics2D.Raycast(
                    transform.position, Vector2.right * direction, 1f, _groundLayer);
                if (wallAhead)
                {
                    bool clearAbove = !Physics2D.Raycast(
                        transform.position + new Vector3(0f, 1.5f, 0f),
                        Vector2.right * direction, 1f, _groundLayer);
                    if (clearAbove) shouldJump = true;
                }

                // 플레이어가 위에 있으면 점프 시도
                if (!shouldJump && _player.position.y > transform.position.y + 1f)
                {
                    Vector2 checkDir = (Vector2.up + Vector2.right * direction).normalized;
                    RaycastHit2D platformCheck = Physics2D.Raycast(
                        transform.position + new Vector3(0f, 0.5f, 0f), checkDir, 3f, _groundLayer);
                    if (platformCheck.collider != null) shouldJump = true;
                }

                if (shouldJump) Jump();
            }
        }

        // ─────────────────────────────────────────
        //  점프
        // ─────────────────────────────────────────

        /// <summary>순간 힘을 가해 점프합니다.</summary>
        private void Jump()
        {
            _rigid2D.linearVelocityY = 0f;
            _rigid2D.AddForce(Vector2.up * _jumpForce, ForceMode2D.Impulse);
            _lastJumpTime = Time.time;
        }

        // ─────────────────────────────────────────
        //  물리 충돌 (몸빵 데미지)
        // ─────────────────────────────────────────

        /// <summary>
        /// 플레이어와 지속 충돌 시 쿨타임마다 데미지를 줍니다.
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
        /// <param name="hitType">피격 타입 (넉백 세기 결정)</param>
        /// <param name="hitSourcePosition">공격 발생 위치 (넉백 방향 계산용)</param>
        public void TakeDamage(int damage, HitType hitType, Vector3 hitSourcePosition)
        {
            if (_isDead) return;

            _currentHp -= damage;
            _currentHp = Mathf.Max(_currentHp, 0);

            // HP 바 갱신
            if (_hpFillSprite != null)
            {
                float ratio = (float)_currentHp / _maxHp;
                _hpFillSprite.transform.DOScaleX(ratio, 0.2f);
            }

            PlayHitEffect(hitType, hitSourcePosition);

            if (_currentHp <= 0) Die();
        }

        // ─────────────────────────────────────────
        //  기절 (WallSentry 스킬 연동)
        // ─────────────────────────────────────────

        /// <summary>
        /// 지정한 시간 동안 적을 기절시킵니다.
        /// WallSentry 고유 스킬에서 호출됩니다.
        /// </summary>
        /// <param name="duration">기절 지속 시간 (초)</param>
        public void Stun(float duration)
        {
            if (_isDead) return;
            StartCoroutine(StunRoutine(duration));
        }

        /// <summary>
        /// 기절 상태를 duration 초 동안 유지한 뒤 해제합니다.
        /// </summary>
        private IEnumerator StunRoutine(float duration)
        {
            _isStunned = true;
            _rigid2D.linearVelocity = Vector2.zero;

            // 기절 연출: 노란 별이 도는 느낌을 위해 스프라이트를 노랗게 깜빡임
            _spriteRenderer.DOColor(Color.yellow, 0.15f)
                .SetLoops(Mathf.RoundToInt(duration / 0.3f), LoopType.Yoyo);

            Debug.Log($"[{name}] {duration}초 기절!");

            yield return new WaitForSeconds(duration);

            _isStunned = false;
            _spriteRenderer.DOKill();
            _spriteRenderer.color = Color.white;

            Debug.Log($"[{name}] 기절 해제");
        }

        // ─────────────────────────────────────────
        //  피격 연출
        // ─────────────────────────────────────────

        /// <summary>
        /// 피격 타입에 따라 넉백 방향과 크기, 스프라이트 연출을 다르게 재생합니다.
        /// 기존 Enemy.cs의 PlayHitEffect 구조를 유지합니다.
        /// </summary>
        private void PlayHitEffect(HitType hitType, Vector3 hitSourcePosition)
        {
            // 기존 트위닝 즉시 종료 후 초기화
            transform.DOKill();
            _spriteRenderer.DOKill();
            _spriteRenderer.color = Color.white;

            Vector2 pushDir = (transform.position - hitSourcePosition).normalized;
            _rigid2D.linearVelocity = Vector2.zero;

            if (hitType == HitType.Strike)
            {
                // 강한 넉백 + 빨간 깜빡임
                _rigid2D.AddForce(pushDir * 8f, ForceMode2D.Impulse);
                _spriteRenderer.DOColor(Color.red, 0.1f).SetLoops(2, LoopType.Yoyo);

                // 0.3초 후 스턴 해제 (이동 재개)
                DOVirtual.DelayedCall(0.3f, () =>
                {
                    if (this != null && _rigid2D != null && !_isStunned)
                        _rigid2D.linearVelocity = Vector2.zero;
                });
            }
            else if (hitType == HitType.Shoot)
            {
                // 약한 밀림 + 노란 깜빡임
                _rigid2D.AddForce(pushDir * 3f, ForceMode2D.Impulse);
                _spriteRenderer.DOColor(Color.yellow, 0.1f).SetLoops(2, LoopType.Yoyo);

                DOVirtual.DelayedCall(0.4f, () =>
                {
                    if (this != null && _rigid2D != null && !_isStunned)
                        _rigid2D.linearVelocity = Vector2.zero;
                });
            }
        }

        // ─────────────────────────────────────────
        //  사망 처리
        // ─────────────────────────────────────────

        /// <summary>
        /// HP가 0이 되면 호출됩니다.
        /// 사망 연출 후 BattleManager에 사망을 알리고 오브젝트를 파괴합니다.
        /// </summary>
        private void Die()
        {
            if (_isDead) return;
            _isDead = true;

            if (_hpBarGroup != null) _hpBarGroup.SetActive(false);

            // 물리 비활성화 (관통 방지)
            _rigid2D.simulated = false;

            // BattleManager에 사망 통보 → 경험치 분배 + 킬 카운트
            if (BattleManager.Instance != null)
                BattleManager.Instance.OnEnemyDied(_expOnDeath);

            // 사망 연출: 축소 후 파괴
            transform.DOScale(Vector3.zero, 0.3f)
                .SetEase(Ease.InBack)
                .OnComplete(() => Destroy(gameObject));

            Debug.Log($"<color=red>[{name} 사망]</color> 경험치 {_expOnDeath} 분배");
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            // 감지 범위 (노란색)
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, _detectionRange);

            // 공격 범위 (빨간색)
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, _attackRange);
        }
#endif
    }
}