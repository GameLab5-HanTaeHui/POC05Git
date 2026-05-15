using UnityEngine;
using DG.Tweening;

namespace SENTRY
{
    /// <summary>
    /// 벽(방어/압박) 센트리.
    ///
    /// [담당 영역 — 전투 정보만]
    ///   밀치기 데미지 / 범위 / 쿨타임 / 접근 속도 / 스킬 게이지
    ///   기본 정보(이름, HP, 레벨, EXP, 추종 설정)는 SentryBase에 있습니다.
    ///
    /// [레이어 기반 탐색]
    ///   FindTarget()에서 EnemyLive 레이어만 탐색합니다.
    /// </summary>
    public class WallSentry : SentryBase
    {
        // ─────────────────────────────────────────
        //  Inspector 노출 필드 — 전투 정보 전담
        // ─────────────────────────────────────────

        [Header("방어 / 밀치기 설정")]
        [SerializeField] private int _pushDamage = 15;
        [SerializeField] private float _blockRange = 2.5f;
        [SerializeField] private float _attackCooldown = 2f;
        [SerializeField] private float _pushDistance = 2.5f;
        [SerializeField] private float _pushDuration = 0.25f;
        [SerializeField] private float _approachSpeed = 2f;

        [Header("레이어 설정")]
        [Tooltip("탐색할 적 레이어. EnemyLive를 설정하세요.")]
        [SerializeField] private LayerMask _enemyLayer;

        [Header("스킬 설정")]
        [SerializeField] private float _maxSkillGauge = 100f;
        [SerializeField] private float _skillGaugePerAttack = 20f;
        [SerializeField] private float _stunDuration = 2.5f;
        [SerializeField] private float _skillDamageMultiplier = 2.5f;
        [SerializeField] private float _skillPushDistMultiplier = 2f;

        [Header("컴포넌트 참조")]
        [SerializeField] private SkillEffect_Wall _skillEffect;

        // ─────────────────────────────────────────
        //  내부 상태
        // ─────────────────────────────────────────

        private Transform _currentTarget;
        private float _lastAttackTime;
        private float _currentSkillGauge = 0f;
        private bool _isInBattle = false;

        // ─────────────────────────────────────────
        //  외부 공개 프로퍼티
        // ─────────────────────────────────────────

        public float SkillGauge => _currentSkillGauge;
        public float MaxSkillGauge => _maxSkillGauge;

        // ─────────────────────────────────────────
        //  초기화
        // ─────────────────────────────────────────

        public override void Init(Transform player)
        {
            base.Init(player);
            _currentSkillGauge = 0f;
            if (_skillEffect == null)
                _skillEffect = GetComponent<SkillEffect_Wall>();
        }

        // ─────────────────────────────────────────
        //  배틀 진입 / 종료
        // ─────────────────────────────────────────

        public void EnterBattle()
        {
            _isInBattle = true;
            StopFollowing();
        }

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
            if (_skillEffect != null && _skillEffect.IsPlaying) return;

            FindTarget();
            HandleBattleAI();
        }

        // ─────────────────────────────────────────
        //  전투 AI
        // ─────────────────────────────────────────

        /// <summary>EnemyLive 레이어 내 가장 가까운 적 탐색.</summary>
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
                if (e != null && e.IsDead) continue;

                float d = Vector2.Distance(transform.position, col.transform.position);
                if (d < minDist) { minDist = d; closest = col.transform; }
            }

            _currentTarget = closest;
        }

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
        /// 밀치기 공격.
        ///
        /// [타겟 사망 예외처리]
        ///   TakeDamage 후 적이 KO될 수 있습니다.
        ///   DOMove는 KO 후에도 실행될 수 있지만 Die()의 DOKill()이 취소합니다.
        ///   null 체크로 NullRef를 방지합니다.
        /// </summary>
        private void TryPush()
        {
            if (Time.time < _lastAttackTime + _attackCooldown) return;
            if (_currentTarget == null) return;
            if (!_currentTarget.gameObject.activeInHierarchy)
            {
                _currentTarget = null;
                return;
            }

            Enemy enemy = _currentTarget.GetComponent<Enemy>();
            if (enemy == null || enemy.IsDead) { _currentTarget = null; return; }

            _lastAttackTime = Time.time;

            // 데미지 적용 (이 이후 enemy.IsDead가 true가 될 수 있음)
            enemy.TakeDamage(
                Mathf.RoundToInt(_pushDamage * OverloadDamageMultiplier),
                HitType.Strike,
                transform.position);

            // DOMove — 사망 처리 중이어도 실행. Die()의 DOKill()이 중단시킴
            if (!enemy.IsDead && _currentTarget != null)
            {
                Vector3 pushDir = (_currentTarget.position - transform.position).normalized;
                Vector3 pushTarget = _currentTarget.position + pushDir * _pushDistance;
                _currentTarget.DOMove(pushTarget, _pushDuration).SetEase(Ease.OutQuart);
            }

            // 타격 연출 (자신)
            if (_currentTarget != null)
            {
                Vector3 punchDir =
                    (_currentTarget.position - transform.position).normalized * 0.4f;
                transform.DOPunchPosition(punchDir, 0.2f, 5, 0.5f);
            }

            ChargeSkillGauge(_skillGaugePerAttack);
            Debug.Log($"[{SentryName}] 밀치기! 데미지: " +
                      $"{Mathf.RoundToInt(_pushDamage * OverloadDamageMultiplier)}");
        }

        // ─────────────────────────────────────────
        //  스킬 게이지
        // ─────────────────────────────────────────

        private void ChargeSkillGauge(float amount)
        {
            _currentSkillGauge = Mathf.Min(_currentSkillGauge + amount, _maxSkillGauge);
            if (_currentSkillGauge >= _maxSkillGauge &&
                (_skillEffect == null || !_skillEffect.IsPlaying))
            {
                _currentSkillGauge = 0f;
                UseSkill();
            }
        }

        // ─────────────────────────────────────────
        //  고유 스킬 (강한 밀치기 + 기절)
        // ─────────────────────────────────────────

        private void UseSkill()
        {
            if (_currentTarget == null) return;

            if (_skillEffect == null) { FallbackSkill(); return; }

            int skillDmg = Mathf.RoundToInt(
                _pushDamage * _skillDamageMultiplier * OverloadDamageMultiplier);
            float pushDist = _pushDistance * _skillPushDistMultiplier;
            float stunDur = _stunDuration;
            Transform captured = _currentTarget;

            _skillEffect.PlaySkill(
                captured,
                onImpact: () =>
                {
                    if (captured == null) return;
                    Enemy e = captured.GetComponent<Enemy>();
                    if (e == null) return;

                    e.TakeDamage(skillDmg, HitType.Strike, transform.position);
                    e.Stun(stunDur);

                    if (!e.IsDead && captured != null)
                    {
                        Vector3 dir = (captured.position - transform.position).normalized;
                        Vector3 target = captured.position + dir * pushDist;
                        captured.DOMove(target, _pushDuration * 0.5f).SetEase(Ease.OutExpo);
                    }

                    SentryComboManager.Instance?.OnSentrySkillUsed();
                }
            );
        }

        private void FallbackSkill()
        {
            if (_currentTarget == null) return;
            int dmg = Mathf.RoundToInt(
                _pushDamage * _skillDamageMultiplier * OverloadDamageMultiplier);
            Enemy e = _currentTarget.GetComponent<Enemy>();
            if (e == null) return;
            e.TakeDamage(dmg, HitType.Strike, transform.position);
            e.Stun(_stunDuration);
            if (!e.IsDead)
            {
                Vector3 dir = (_currentTarget.position - transform.position).normalized;
                Vector3 target = _currentTarget.position + dir * _pushDistance * _skillPushDistMultiplier;
                _currentTarget.DOMove(target, _pushDuration).SetEase(Ease.OutExpo);
            }
        }

        // ─────────────────────────────────────────
        //  레벨업
        // ─────────────────────────────────────────

        protected override void LevelUp()
        {
            base.LevelUp();
            _stunDuration += 0.2f;
            _blockRange += 0.1f;
            _pushDamage = Mathf.RoundToInt(_pushDamage * 1.1f);
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