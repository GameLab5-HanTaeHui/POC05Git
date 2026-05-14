using UnityEngine;
using DG.Tweening;

namespace SENTRY
{
    /// <summary>
    /// 사격(원거리) 센트리 소환수.
    ///
    /// [변경 사항 - Session 4]
    /// - UseSkill() 내부 연출을 SkillEffect_Shoot.PlaySkill()에 위임합니다.
    /// - 각 발사 시점의 실제 탄환 생성은 콜백(Action)으로 전달합니다.
    /// - 스킬 연출 중 AI 정지 여부를 SkillEffect_Shoot.IsPlaying으로 판단합니다.
    ///
    /// [히어라키 위치]
    /// Sentries
    ///   └── ShootSentry
    ///         ├── SentryBase
    ///         ├── ShootSentry  (이 스크립트)
    ///         ├── SkillEffect_Shoot (+ LineRenderer)
    ///         ├── Rigidbody2D
    ///         ├── Collider2D
    ///         └── SpriteRenderer
    /// </summary>
    public class ShootSentry : SentryBase
    {
        // ─────────────────────────────────────────
        //  Inspector 노출 필드
        // ─────────────────────────────────────────

        [Header("사격 전투 설정")]
        [Tooltip("기본 탄환 1발당 데미지")]
        [SerializeField] private int _attackDamage = 20;

        [Tooltip("적 탐지 최대 반경")]
        [SerializeField] private float _detectionRange = 10f;

        [Tooltip("이상적인 교전 거리")]
        [SerializeField] private float _preferredDistance = 5f;

        [Tooltip("기본 공격 쿨타임 (초)")]
        [SerializeField] private float _attackCooldown = 1.5f;

        [Tooltip("적 레이어")]
        [SerializeField] private LayerMask _enemyLayer;

        [Tooltip("시야를 가로막는 차폐물 레이어")]
        [SerializeField] private LayerMask _obstacleLayer;

        [Header("탄환 설정")]
        [Tooltip("발사할 탄환 프리팹 (Bullet.cs 포함)")]
        [SerializeField] private GameObject _bulletPrefab;

        [Tooltip("총구 위치. 없으면 센트리 중앙에서 발사합니다.")]
        [SerializeField] private Transform _firePoint;

        [Header("스킬 설정")]
        [Tooltip("스킬 게이지 최대치")]
        [SerializeField] private float _maxSkillGauge = 100f;

        [Tooltip("기본 공격 1회 시 스킬 게이지 충전량")]
        [SerializeField] private float _skillGaugePerAttack = 12f;

        [Tooltip("3연발 스킬의 데미지 배율")]
        [SerializeField] private float _skillDamageMultiplier = 1.5f;

        [Header("컴포넌트 참조")]
        [Tooltip("사격 스킬 연출 컴포넌트. 같은 오브젝트에 붙어 있어야 합니다.")]
        [SerializeField] private SkillEffect_Shoot _skillEffect;

        // ─────────────────────────────────────────
        //  내부 상태 변수
        // ─────────────────────────────────────────

        private Transform _currentTarget;
        private float _lastAttackTime;
        private float _currentSkillGauge = 0f;
        private bool _isInBattle = false;
        private Rigidbody2D _rigid2D;

        // ─────────────────────────────────────────
        //  외부 공개 프로퍼티
        // ─────────────────────────────────────────

        /// <summary>현재 스킬 게이지 (UI 표시용)</summary>
        public float SkillGauge => _currentSkillGauge;

        /// <summary>최대 스킬 게이지 (UI 표시용)</summary>
        public float MaxSkillGauge => _maxSkillGauge;

        // ─────────────────────────────────────────
        //  초기화
        // ─────────────────────────────────────────

        public override void Init(Transform player)
        {
            base.Init(player);
            _rigid2D = GetComponent<Rigidbody2D>();
            _currentSkillGauge = 0f;

            if (_skillEffect == null)
                _skillEffect = GetComponent<SkillEffect_Shoot>();
        }

        // ─────────────────────────────────────────
        //  배틀 진입 / 종료
        // ─────────────────────────────────────────

        /// <summary>배틀 진입 시 BattleManager가 호출합니다.</summary>
        public void EnterBattle()
        {
            _isInBattle = true;
            StopFollowing();
        }

        /// <summary>배틀 종료 시 BattleManager가 호출합니다.</summary>
        public void ExitBattle()
        {
            _isInBattle = false;
            _currentTarget = null;
            StartFollowing();
        }

        // ─────────────────────────────────────────
        //  유니티 생명주기
        // ─────────────────────────────────────────

        private void Update()
        {
            if (IsKnockedOut || !_isInBattle) return;

            // 스킬 연출 중 AI 정지
            if (_skillEffect != null && _skillEffect.IsPlaying) return;

            FindTarget();
            HandleBattleAI();
        }

        // ─────────────────────────────────────────
        //  전투 AI
        // ─────────────────────────────────────────

        /// <summary>시야 확보된 가장 가까운 적을 탐색합니다.</summary>
        private void FindTarget()
        {
            Collider2D[] hits = Physics2D.OverlapCircleAll(
                transform.position, _detectionRange, _enemyLayer);

            if (hits.Length == 0) { _currentTarget = null; return; }

            float minDist = float.MaxValue;
            Transform best = null;

            foreach (var col in hits)
            {
                float d = Vector2.Distance(transform.position, col.transform.position);
                if (d >= minDist) continue;

                // 차폐물 검사
                Vector2 dir = (col.transform.position - transform.position).normalized;
                RaycastHit2D obstacleHit = Physics2D.Raycast(
                    transform.position, dir, d, _obstacleLayer);

                if (obstacleHit.collider == null)
                {
                    minDist = d;
                    best = col.transform;
                }
            }

            _currentTarget = best;
        }

        /// <summary>교전 거리를 유지하며 기본 공격을 시도합니다.</summary>
        private void HandleBattleAI()
        {
            if (_currentTarget == null)
            {
                if (_rigid2D != null) _rigid2D.linearVelocity = Vector2.zero;
                return;
            }

            float dist = Vector2.Distance(transform.position, _currentTarget.position);
            Vector2 dir = ((Vector2)_currentTarget.position - (Vector2)transform.position).normalized;

            if (dist < _preferredDistance * 0.7f)
                _rigid2D.linearVelocity = -dir * 2f;          // 후퇴
            else if (dist > _preferredDistance * 1.3f)
                _rigid2D.linearVelocity = dir * 2f;            // 전진
            else
            {
                _rigid2D.linearVelocity = Vector2.zero;        // 정지 후 발사
                TryAttack();
            }
        }

        // ─────────────────────────────────────────
        //  기본 공격
        // ─────────────────────────────────────────

        /// <summary>쿨타임이 지났으면 탄환 1발을 발사합니다.</summary>
        private void TryAttack()
        {
            if (Time.time < _lastAttackTime + _attackCooldown) return;
            if (_currentTarget == null) return;

            _lastAttackTime = Time.time;
            FireBullet(_currentTarget, _attackDamage);

            // 발사 연출: 총구 방향 약한 펀치
            Vector3 shootDir = (_currentTarget.position - transform.position).normalized;
            transform.DOPunchPosition(shootDir * 0.1f, 0.1f, 5, 0.5f);

            ChargeSkillGauge(_skillGaugePerAttack);
        }

        /// <summary>
        /// 지정 타겟을 향해 탄환을 생성합니다.
        /// 기본 공격과 스킬 모두에서 호출됩니다.
        /// </summary>
        public void FireBullet(Transform target, int damage)
        {
            if (_bulletPrefab == null || target == null) return;

            Vector3 spawnPos = (_firePoint != null) ? _firePoint.position : transform.position;
            GameObject bulletObj = Instantiate(_bulletPrefab, spawnPos, Quaternion.identity);

            SentryBullet bullet = bulletObj.GetComponent<SentryBullet>();
            if (bullet != null)
            {
                bullet.damage = damage;
                bullet.Setup(target);
            }
        }

        // ─────────────────────────────────────────
        //  스킬 게이지
        // ─────────────────────────────────────────

        private void ChargeSkillGauge(float amount)
        {
            _currentSkillGauge = Mathf.Min(_currentSkillGauge + amount, _maxSkillGauge);

            if (_currentSkillGauge >= _maxSkillGauge
                && (_skillEffect == null || !_skillEffect.IsPlaying))
            {
                _currentSkillGauge = 0f;
                UseSkill();
            }
        }

        // ─────────────────────────────────────────
        //  고유 스킬 (3연발) - SkillEffect_Shoot 연동
        // ─────────────────────────────────────────

        /// <summary>
        /// [고유 스킬] SkillEffect_Shoot에 연출을 위임하고,
        /// 각 발사 시점에 실제 탄환 생성 콜백을 전달합니다.
        /// </summary>
        private void UseSkill()
        {
            if (_currentTarget == null) return;
            if (_skillEffect == null)
            {
                FallbackSkillFire();
                return;
            }

            int skillDamage = Mathf.RoundToInt(_attackDamage * _skillDamageMultiplier);
            Transform capturedTarget = _currentTarget;

            Debug.Log($"<color=yellow>[{SentryName} 스킬 발동!]</color> 3연발");

            _skillEffect.PlaySkill(
                capturedTarget,
                onEachShot: () =>
                {
                    // 각 발사 시점에 탄환 생성
                    if (capturedTarget != null)
                        FireBullet(capturedTarget, skillDamage);
                }
            );
        }

        /// <summary>SkillEffect 컴포넌트가 없을 때의 폴백 처리.</summary>
        private void FallbackSkillFire()
        {
            if (_currentTarget == null) return;
            int skillDamage = Mathf.RoundToInt(_attackDamage * _skillDamageMultiplier);
            for (int i = 0; i < 3; i++)
                FireBullet(_currentTarget, skillDamage);
        }

        // ─────────────────────────────────────────
        //  레벨업 (Override)
        // ─────────────────────────────────────────

        /// <summary>레벨업 시 공격력과 탐지 범위를 추가로 증가시킵니다.</summary>
        protected override void LevelUp()
        {
            base.LevelUp();
            _attackDamage = Mathf.RoundToInt(_attackDamage * 1.1f);
            _detectionRange += 0.3f;
            Debug.Log($"[{SentryName}] 공격력: {_attackDamage} / 탐지 범위: {_detectionRange:F1}");
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, _detectionRange);
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, _preferredDistance);
        }
#endif
    }
}