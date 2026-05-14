using UnityEngine;
using DG.Tweening;

namespace SENTRY
{
    /// <summary>
    /// 벽(방어/압박) 센트리 소환수.
    ///
    /// [변경 사항 - Session 4]
    /// - UseSkill() 연출을 SkillEffect_Wall.PlaySkill()에 위임합니다.
    /// - Session 3에서 TODO였던 Enemy.Stun() 호출이 완성되었습니다.
    /// - 충격 시점의 데미지 + 기절 처리는 onImpact 콜백으로 전달합니다.
    ///
    /// [히어라키 위치]
    /// Sentries
    ///   └── WallSentry
    ///         ├── SentryBase
    ///         ├── WallSentry  (이 스크립트)
    ///         ├── SkillEffect_Wall
    ///         ├── Rigidbody2D
    ///         ├── Collider2D
    ///         └── SpriteRenderer
    /// </summary>
    public class WallSentry : SentryBase
    {
        // ─────────────────────────────────────────
        //  Inspector 노출 필드
        // ─────────────────────────────────────────

        [Header("방어 / 밀치기 설정")]
        [Tooltip("기본 밀치기 데미지")]
        [SerializeField] private int _pushDamage = 15;

        [Tooltip("적 감지 및 차단 반경")]
        [SerializeField] private float _blockRange = 2.5f;

        [Tooltip("기본 공격 쿨타임 (초)")]
        [SerializeField] private float _attackCooldown = 2f;

        [Tooltip("기본 밀치기 힘")]
        [SerializeField] private float _pushForce = 6f;

        [Tooltip("적 레이어")]
        [SerializeField] private LayerMask _enemyLayer;

        [Header("스킬 설정")]
        [Tooltip("스킬 게이지 최대치")]
        [SerializeField] private float _maxSkillGauge = 100f;

        [Tooltip("기본 공격 1회 시 스킬 게이지 충전량")]
        [SerializeField] private float _skillGaugePerAttack = 20f;

        [Tooltip("스킬 발동 시 기절 지속 시간 (초)")]
        [SerializeField] private float _stunDuration = 2.5f;

        [Tooltip("스킬 밀치기 데미지 배율")]
        [SerializeField] private float _skillDamageMultiplier = 2.5f;

        [Tooltip("스킬 밀치기 힘 배율")]
        [SerializeField] private float _skillPushForceMultiplier = 2f;

        [Header("컴포넌트 참조")]
        [Tooltip("벽 스킬 연출 컴포넌트. 같은 오브젝트에 붙어 있어야 합니다.")]
        [SerializeField] private SkillEffect_Wall _skillEffect;

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
                _skillEffect = GetComponent<SkillEffect_Wall>();
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

        private void FindTarget()
        {
            Collider2D[] hits = Physics2D.OverlapCircleAll(
                transform.position, _blockRange, _enemyLayer);

            if (hits.Length == 0) { _currentTarget = null; return; }

            float minDist = float.MaxValue;
            Transform closest = null;
            foreach (var col in hits)
            {
                float d = Vector2.Distance(transform.position, col.transform.position);
                if (d < minDist) { minDist = d; closest = col.transform; }
            }
            _currentTarget = closest;
        }

        private void HandleBattleAI()
        {
            if (_currentTarget == null)
            {
                if (_rigid2D != null) _rigid2D.linearVelocity = Vector2.zero;
                return;
            }

            float dist = Vector2.Distance(transform.position, _currentTarget.position);
            Vector2 dir = ((Vector2)_currentTarget.position - (Vector2)transform.position).normalized;

            if (dist > 1.0f)
                _rigid2D.linearVelocity = dir * 2f;
            else
            {
                _rigid2D.linearVelocity = Vector2.zero;
                TryPush();
            }
        }

        // ─────────────────────────────────────────
        //  기본 공격 (밀치기)
        // ─────────────────────────────────────────

        private void TryPush()
        {
            if (Time.time < _lastAttackTime + _attackCooldown) return;
            if (_currentTarget == null) return;

            _lastAttackTime = Time.time;

            Enemy enemy = _currentTarget.GetComponent<Enemy>();
            if (enemy != null)
                enemy.TakeDamage(_pushDamage, HitType.Strike, transform.position);

            Rigidbody2D enemyRb = _currentTarget.GetComponent<Rigidbody2D>();
            if (enemyRb != null)
            {
                Vector2 pushDir = (_currentTarget.position - transform.position).normalized;
                enemyRb.AddForce(pushDir * _pushForce, ForceMode2D.Impulse);
            }

            Vector3 punchDir = (_currentTarget.position - transform.position).normalized * 0.4f;
            transform.DOPunchPosition(punchDir, 0.2f, 5, 0.5f);

            ChargeSkillGauge(_skillGaugePerAttack);
            Debug.Log($"[{SentryName}] 밀치기 공격! 데미지: {_pushDamage}");
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
        //  고유 스킬 (강한 밀치기 + 기절) - SkillEffect_Wall 연동
        // ─────────────────────────────────────────

        /// <summary>
        /// [고유 스킬] SkillEffect_Wall에 연출을 위임하고,
        /// 충격 시점에 데미지 + 기절(Stun) 콜백을 전달합니다.
        /// Session 3의 TODO였던 Enemy.Stun() 호출이 여기서 완성됩니다.
        /// </summary>
        private void UseSkill()
        {
            if (_currentTarget == null) return;
            if (_skillEffect == null)
            {
                FallbackSkillPush();
                return;
            }

            int skillDamage = Mathf.RoundToInt(_pushDamage * _skillDamageMultiplier);
            float skillPushForce = _pushForce * _skillPushForceMultiplier;
            Transform capturedTarget = _currentTarget;
            float stunDuration = _stunDuration;

            Debug.Log($"<color=yellow>[{SentryName} 스킬 발동!]</color> 강한 밀치기 + {stunDuration}초 기절");

            _skillEffect.PlaySkill(
                capturedTarget,
                onImpact: () =>
                {
                    if (capturedTarget == null) return;

                    // 데미지 적용
                    Enemy enemy = capturedTarget.GetComponent<Enemy>();
                    if (enemy != null)
                    {
                        enemy.TakeDamage(skillDamage, HitType.Strike, transform.position);
                        // Session 3 TODO 완성: Enemy.Stun() 호출
                        enemy.Stun(stunDuration);
                    }

                    // 강한 밀치기 힘
                    Rigidbody2D enemyRb = capturedTarget.GetComponent<Rigidbody2D>();
                    if (enemyRb != null)
                    {
                        Vector2 pushDir = (capturedTarget.position - transform.position).normalized;
                        enemyRb.AddForce(pushDir * skillPushForce, ForceMode2D.Impulse);
                    }
                }
            );
        }

        /// <summary>SkillEffect 컴포넌트가 없을 때의 폴백 처리.</summary>
        private void FallbackSkillPush()
        {
            if (_currentTarget == null) return;
            int skillDamage = Mathf.RoundToInt(_pushDamage * _skillDamageMultiplier);
            Enemy e = _currentTarget.GetComponent<Enemy>();
            if (e != null)
            {
                e.TakeDamage(skillDamage, HitType.Strike, transform.position);
                e.Stun(_stunDuration);
            }
        }

        // ─────────────────────────────────────────
        //  레벨업 (Override)
        // ─────────────────────────────────────────

        protected override void LevelUp()
        {
            base.LevelUp();
            _stunDuration += 0.2f;
            _blockRange += 0.1f;
            _pushDamage = Mathf.RoundToInt(_pushDamage * 1.1f);
            Debug.Log($"[{SentryName}] 기절 시간: {_stunDuration:F1}초 / 범위: {_blockRange:F1}");
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(transform.position, _blockRange);
        }
#endif
    }
}