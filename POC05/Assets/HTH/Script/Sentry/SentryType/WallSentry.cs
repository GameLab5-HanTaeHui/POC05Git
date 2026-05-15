using UnityEngine;
using DG.Tweening;

namespace SENTRY
{
    /// <summary>
    /// 벽(방어/압박) 센트리 소환수.
    ///
    /// [변경 사항 - 페이크 쿼터뷰 대응]
    /// - HandleBattleAI() 내부의 _rigid2D.linearVelocity 직접 할당을
    ///   SentryBase.BattleMove() 호출로 교체했습니다.
    /// - TryPush()의 적 밀치기도 enemyRb.AddForce → DOTween.DOMove로 교체했습니다.
    ///   Kinematic 상태의 적에게 AddForce는 효과가 없기 때문입니다.
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

        [Tooltip("적을 밀쳐내는 거리 (DOTween 이동량 — Kinematic 적 전용)")]
        [SerializeField] private float _pushDistance = 2.5f;

        [Tooltip("밀치기 이동에 걸리는 시간 (초)")]
        [SerializeField] private float _pushDuration = 0.25f;

        [Tooltip("적 레이어")]
        [SerializeField] private LayerMask _enemyLayer;

        [Tooltip("적에게 접근하는 이동 속도 (BattleMove에 사용)")]
        [SerializeField] private float _approachSpeed = 2f;

        [Header("스킬 설정")]
        [Tooltip("스킬 게이지 최대치")]
        [SerializeField] private float _maxSkillGauge = 100f;

        [Tooltip("기본 공격 1회 시 스킬 게이지 충전량")]
        [SerializeField] private float _skillGaugePerAttack = 20f;

        [Tooltip("스킬 발동 시 기절 지속 시간 (초)")]
        [SerializeField] private float _stunDuration = 2.5f;

        [Tooltip("스킬 밀치기 데미지 배율")]
        [SerializeField] private float _skillDamageMultiplier = 2.5f;

        [Tooltip("스킬 밀치기 거리 배율")]
        [SerializeField] private float _skillPushDistanceMultiplier = 2f;

        [Header("컴포넌트 참조")]
        [Tooltip("벽 스킬 연출 컴포넌트. 같은 오브젝트에 붙어 있어야 합니다.")]
        [SerializeField] private SkillEffect_Wall _skillEffect;

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

        /// <summary>Rigidbody2D 캐시</summary>
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

        /// <summary>SentryBase.Init() 이후 WallSentry 전용 초기화를 수행합니다.</summary>
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

        /// <summary>범위 내 가장 가까운 생존 적을 탐색합니다.</summary>
        private void FindTarget()
        {
            Collider2D[] hits = Physics2D.OverlapCircleAll(
                transform.position, _blockRange, _enemyLayer);

            if (hits.Length == 0) { _currentTarget = null; return; }

            float minDist = float.MaxValue;
            Transform closest = null;

            foreach (var col in hits)
            {
                if (col == null) continue;
                Enemy e = col.GetComponent<Enemy>();
                if (e == null || e.IsDead) continue;

                float d = Vector2.Distance(transform.position, col.transform.position);
                if (d < minDist) { minDist = d; closest = col.transform; }
            }

            _currentTarget = closest;
        }

        /// <summary>적에게 접근하고 근접 시 밀치기를 시도합니다.</summary>
        private void HandleBattleAI()
        {
            if (_currentTarget == null ||
                !_currentTarget.gameObject.activeInHierarchy)
            {
                _currentTarget = null;
                BattleStop();
                return;
            }

            float dist = Vector2.Distance(transform.position, _currentTarget.position);
            Vector2 dir = ((Vector2)_currentTarget.position
                           - (Vector2)transform.position).normalized;

            if (dist > 1.0f)
                BattleMove(dir, _approachSpeed * OverloadSpeedMultiplier);
            else
            {
                BattleStop();
                TryPush();
            }
        }

        // ─────────────────────────────────────────
        //  기본 공격 (밀치기)
        // ─────────────────────────────────────────

        /// <summary>
        /// 쿨타임이 지났으면 밀치기 공격을 실행합니다.
        ///
        /// [페이크 쿼터뷰 대응]
        ///   기존 enemyRb.AddForce → DOTween.DOMove로 교체.
        ///   적도 Kinematic 상태이므로 AddForce가 동작하지 않습니다.
        ///   DOMove로 적을 밀쳐내는 방향으로 이동시킵니다.
        /// </summary>
        private void TryPush()
        {
            if (Time.time < _lastAttackTime + _attackCooldown) return;

            // ── null / 파괴된 오브젝트 이중 안전 체크 ──
            // FindTarget()과 TryPush() 사이 프레임에 적이 Die()로 파괴될 수 있으므로
            // Transform 뿐 아니라 gameObject 활성 여부까지 확인합니다.
            if (_currentTarget == null) return;
            if (!_currentTarget.gameObject.activeInHierarchy) { _currentTarget = null; return; }

            Enemy enemy = _currentTarget.GetComponent<Enemy>();

            // Enemy 컴포넌트가 없거나 이미 사망 처리 중이면 타겟 무효화 후 중단
            if (enemy == null || enemy.IsDead) { _currentTarget = null; return; }

            _lastAttackTime = Time.time;

            // 데미지 적용
            enemy.TakeDamage(
                Mathf.RoundToInt(_pushDamage * OverloadDamageMultiplier),
                HitType.Strike,
                transform.position);

            // 밀치기 DOMove — 데미지 후에도 오브젝트가 살아있을 때만 실행
            // Die() 애니메이션 중에 DOMove를 추가로 실행하면 충돌이 발생합니다.
            if (!enemy.IsDead && _currentTarget != null)
            {
                Vector3 pushDir = (_currentTarget.position - transform.position).normalized;
                Vector3 pushTarget = _currentTarget.position + pushDir * _pushDistance;
                _currentTarget.DOMove(pushTarget, _pushDuration).SetEase(Ease.OutQuart);
            }

            // 밀치기 타격 연출 (WallSentry 자신)
            Vector3 punchDir =
                (_currentTarget != null
                    ? (_currentTarget.position - transform.position).normalized
                    : transform.right) * 0.4f;
            transform.DOPunchPosition(punchDir, 0.2f, 5, 0.5f);

            ChargeSkillGauge(_skillGaugePerAttack);
            Debug.Log($"[{SentryName}] 밀치기 공격! 데미지: " +
                      $"{Mathf.RoundToInt(_pushDamage * OverloadDamageMultiplier)}");
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
        //  고유 스킬 (강한 밀치기 + 기절) — SkillEffect_Wall 연동
        // ─────────────────────────────────────────

        /// <summary>
        /// [고유 스킬] SkillEffect_Wall에 연출을 위임하고,
        /// 충격 시점에 데미지 + 기절(Stun) 콜백을 전달합니다.
        /// </summary>
        private void UseSkill()
        {
            if (_currentTarget == null) return;

            if (_skillEffect == null)
            {
                FallbackSkillPush();
                return;
            }

            int skillDamage =
                Mathf.RoundToInt(_pushDamage * _skillDamageMultiplier
                                            * OverloadDamageMultiplier);
            float skillPushDist = _pushDistance * _skillPushDistanceMultiplier;
            Transform capturedTarget = _currentTarget;
            float stunDuration = _stunDuration;

            Debug.Log($"<color=yellow>[{SentryName} 스킬 발동!]</color> " +
                      $"강한 밀치기 + {stunDuration}초 기절");

            _skillEffect.PlaySkill(
                capturedTarget,
                onImpact: () =>
                {
                    if (capturedTarget == null) return;

                    Enemy enemy = capturedTarget.GetComponent<Enemy>();
                    if (enemy != null)
                    {
                        enemy.TakeDamage(skillDamage, HitType.Strike, transform.position);
                        enemy.Stun(stunDuration);
                    }

                    // 스킬도 Kinematic 대응 — DOMove로 강하게 밀어냄
                    Vector3 pushDir =
                        (capturedTarget.position - transform.position).normalized;
                    Vector3 pushTarget =
                        capturedTarget.position + pushDir * skillPushDist;

                    capturedTarget.DOMove(pushTarget, _pushDuration * 0.5f)
                                  .SetEase(Ease.OutExpo);

                    // 스킬 사용 완료 → 콤보 게이지 충전 통보
                    SentryComboManager.Instance?.OnSentrySkillUsed();
                }
            );
        }

        /// <summary>SkillEffect 컴포넌트가 없을 때의 폴백 처리.</summary>
        private void FallbackSkillPush()
        {
            if (_currentTarget == null) return;

            int dmg = Mathf.RoundToInt(_pushDamage * _skillDamageMultiplier
                                                     * OverloadDamageMultiplier);
            Enemy e = _currentTarget.GetComponent<Enemy>();
            if (e != null)
            {
                e.TakeDamage(dmg, HitType.Strike, transform.position);
                e.Stun(_stunDuration);
            }

            Vector3 pushDir =
                (_currentTarget.position - transform.position).normalized;
            Vector3 pushTarget =
                _currentTarget.position + pushDir * _pushDistance * _skillPushDistanceMultiplier;

            _currentTarget.DOMove(pushTarget, _pushDuration).SetEase(Ease.OutExpo);
        }

        // ─────────────────────────────────────────
        //  레벨업 (Override)
        // ─────────────────────────────────────────

        /// <summary>레벨업 시 방어/압박 스탯을 추가 강화합니다.</summary>
        protected override void LevelUp()
        {
            base.LevelUp();
            _stunDuration += 0.2f;
            _blockRange += 0.1f;
            _pushDamage = Mathf.RoundToInt(_pushDamage * 1.1f);
            Debug.Log($"[{SentryName}] 기절 시간: {_stunDuration:F1}초 / " +
                      $"범위: {_blockRange:F1} / 데미지: {_pushDamage}");
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