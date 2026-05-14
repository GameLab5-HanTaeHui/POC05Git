using UnityEngine;
using DG.Tweening;

namespace SENTRY
{
    /// <summary>
    /// 타격(근접) 센트리 소환수.
    ///
    /// [변경 사항 - 페이크 쿼터뷰 대응]
    /// - HandleBattleAI() 내부의 linearVelocity 할당을
    ///   SentryBase.BattleMove() 호출로 교체했습니다.
    /// - Kinematic 상태에서 linearVelocity는 무시되므로
    ///   MovePosition 기반의 BattleMove()가 필수입니다.
    ///
    /// [히어라키 위치]
    /// Sentries
    ///   └── StrikeSentry
    ///         ├── SentryBase
    ///         ├── StrikeSentry  (이 스크립트)
    ///         ├── SkillEffect_Strike
    ///         ├── Rigidbody2D
    ///         ├── Collider2D
    ///         └── SpriteRenderer
    /// </summary>
    public class StrikeSentry : SentryBase
    {
        // ─────────────────────────────────────────
        //  Inspector 노출 필드
        // ─────────────────────────────────────────

        [Header("타격 전투 설정")]
        [Tooltip("기본 공격 데미지")]
        [SerializeField] private int _attackDamage = 30;

        [Tooltip("근접 공격이 닿는 범위 (원형 반지름)")]
        [SerializeField] private float _attackRange = 1.2f;

        [Tooltip("기본 공격 쿨타임 (초)")]
        [SerializeField] private float _attackCooldown = 1.2f;

        [Tooltip("적을 향해 이동하는 속도 (배틀 필드 BattleMove에 사용)")]
        [SerializeField] private float _chaseSpeed = 3.5f;

        [Tooltip("적 감지 반경")]
        [SerializeField] private float _detectionRange = 8f;

        [Tooltip("적 레이어")]
        [SerializeField] private LayerMask _enemyLayer;

        [Header("스킬 설정")]
        [Tooltip("스킬 게이지 최대치")]
        [SerializeField] private float _maxSkillGauge = 100f;

        [Tooltip("기본 공격 1회 시 스킬 게이지 충전량")]
        [SerializeField] private float _skillGaugePerAttack = 15f;

        [Tooltip("고유 스킬 데미지 배율 (기본 공격 대비)")]
        [SerializeField] private float _skillDamageMultiplier = 2f;

        [Header("컴포넌트 참조")]
        [Tooltip("타격 스킬 연출 컴포넌트. 같은 오브젝트에 붙어 있어야 합니다.")]
        [SerializeField] private SkillEffect_Strike _skillEffect;

        // ─────────────────────────────────────────
        //  내부 상태 변수
        // ─────────────────────────────────────────

        /// <summary>현재 추적 중인 적 Transform</summary>
        private Transform _currentTarget;

        /// <summary>마지막 공격 시각 (Time.time)</summary>
        private float _lastAttackTime;

        /// <summary>현재 스킬 게이지 누적량</summary>
        private float _currentSkillGauge = 0f;

        /// <summary>배틀 AI 활성 여부</summary>
        private bool _isInBattle = false;

        /// <summary>Rigidbody2D 캐시 (탐색 필드 이동에만 사용)</summary>
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

        /// <summary>SentryBase.Init() 이후 StrikeSentry 전용 초기화를 수행합니다.</summary>
        public override void Init(Transform player)
        {
            base.Init(player);
            _rigid2D = GetComponent<Rigidbody2D>();
            _currentSkillGauge = 0f;

            if (_skillEffect == null)
                _skillEffect = GetComponent<SkillEffect_Strike>();
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

            // 스킬 연출 중에는 AI 정지
            if (_skillEffect != null && _skillEffect.IsPlaying) return;

            FindTarget();
            HandleBattleAI();
        }

        // ─────────────────────────────────────────
        //  전투 AI
        // ─────────────────────────────────────────

        /// <summary>범위 내 가장 가까운 적을 탐색합니다.</summary>
        private void FindTarget()
        {
            Collider2D[] hits = Physics2D.OverlapCircleAll(
                transform.position, _detectionRange, _enemyLayer);

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

        /// <summary>
        /// 타겟을 추적하고 공격 범위 내 진입 시 공격을 시도합니다.
        ///
        /// [페이크 쿼터뷰 대응]
        ///   linearVelocity 직접 할당 → BattleMove() 호출로 변경.
        ///   BattleMove()는 내부적으로 Rigidbody2D.MovePosition()을 사용하여
        ///   Kinematic 상태에서도 정상 이동합니다.
        /// </summary>
        private void HandleBattleAI()
        {
            if (_currentTarget == null)
            {
                BattleStop();
                return;
            }

            float dist = Vector2.Distance(transform.position, _currentTarget.position);

            if (dist <= _attackRange)
            {
                // 공격 범위 내 — 정지 후 공격
                BattleStop();
                TryAttack();
            }
            else
            {
                // 적을 향해 추적 이동
                Vector2 dir = ((Vector2)_currentTarget.position
                               - (Vector2)transform.position).normalized;
                BattleMove(dir, _chaseSpeed * OverloadSpeedMultiplier);
            }
        }

        // ─────────────────────────────────────────
        //  기본 공격
        // ─────────────────────────────────────────

        /// <summary>쿨타임이 지났으면 기본 공격을 실행하고 스킬 게이지를 충전합니다.</summary>
        private void TryAttack()
        {
            if (Time.time < _lastAttackTime + _attackCooldown) return;
            if (_currentTarget == null) return;

            _lastAttackTime = Time.time;

            Enemy enemy = _currentTarget.GetComponent<Enemy>();
            if (enemy != null)
                enemy.TakeDamage(
                    Mathf.RoundToInt(_attackDamage * OverloadDamageMultiplier),
                    HitType.Strike,
                    transform.position);

            // 기본 타격 연출 (폴백 — SkillEffect 없을 때)
            Vector3 punchDir =
                (_currentTarget.position - transform.position).normalized * 0.3f;
            transform.DOPunchPosition(punchDir, 0.2f, 5, 0.5f);

            ChargeSkillGauge(_skillGaugePerAttack);
            Debug.Log($"[{SentryName}] 기본 공격! 데미지: " +
                      $"{Mathf.RoundToInt(_attackDamage * OverloadDamageMultiplier)}");
        }

        // ─────────────────────────────────────────
        //  스킬 게이지
        // ─────────────────────────────────────────

        /// <summary>스킬 게이지를 충전하고 가득 차면 스킬을 발동합니다.</summary>
        private void ChargeSkillGauge(float amount)
        {
            _currentSkillGauge =
                Mathf.Min(_currentSkillGauge + amount, _maxSkillGauge);

            if (_currentSkillGauge >= _maxSkillGauge
                && (_skillEffect == null || !_skillEffect.IsPlaying))
            {
                _currentSkillGauge = 0f;
                UseSkill();
            }
        }

        // ─────────────────────────────────────────
        //  고유 스킬 (2연타) — SkillEffect_Strike 연동
        // ─────────────────────────────────────────

        /// <summary>
        /// [고유 스킬] SkillEffect_Strike에 연출을 위임하고,
        /// 1타·2타 시점에 데미지를 콜백으로 전달합니다.
        /// </summary>
        private void UseSkill()
        {
            if (_currentTarget == null) return;

            if (_skillEffect == null)
            {
                FallbackSkillDamage();
                return;
            }

            int skillDamage =
                Mathf.RoundToInt(_attackDamage * _skillDamageMultiplier
                                              * OverloadDamageMultiplier);
            Transform capturedTarget = _currentTarget;

            Debug.Log($"<color=yellow>[{SentryName} 스킬 발동!]</color> " +
                      $"2연타 데미지: {skillDamage}");

            _skillEffect.PlaySkill(
                capturedTarget,
                onFirstHit: () =>
                {
                    if (capturedTarget == null) return;
                    Enemy e = capturedTarget.GetComponent<Enemy>();
                    e?.TakeDamage(skillDamage, HitType.Strike, transform.position);
                },
                onSecondHit: () =>
                {
                    if (capturedTarget == null) return;
                    Enemy e = capturedTarget.GetComponent<Enemy>();
                    e?.TakeDamage(skillDamage, HitType.Strike, transform.position);
                    ComboManager.Instance?.OnSentrySkillUsed();
                }
            );
        }

        /// <summary>SkillEffect 컴포넌트가 없을 때의 폴백 처리.</summary>
        private void FallbackSkillDamage()
        {
            if (_currentTarget == null) return;
            int dmg = Mathf.RoundToInt(_attackDamage * _skillDamageMultiplier
                                                      * OverloadDamageMultiplier);
            Enemy e = _currentTarget.GetComponent<Enemy>();
            e?.TakeDamage(dmg, HitType.Strike, transform.position);
        }

        // ─────────────────────────────────────────
        //  레벨업 (Override)
        // ─────────────────────────────────────────

        /// <summary>레벨업 시 타격 스탯을 추가 강화합니다.</summary>
        protected override void LevelUp()
        {
            base.LevelUp();
            _attackDamage = Mathf.RoundToInt(_attackDamage * 1.1f);
            _chaseSpeed += 0.1f;
            _attackRange += 0.05f;
            Debug.Log($"[{SentryName}] 공격력: {_attackDamage} / " +
                      $"추적 속도: {_chaseSpeed:F1} / 범위: {_attackRange:F2}");
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, _detectionRange);
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, _attackRange);
        }
#endif
    }
}