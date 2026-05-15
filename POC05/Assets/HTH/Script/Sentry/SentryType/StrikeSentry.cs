using UnityEngine;
using DG.Tweening;

namespace SENTRY
{
    /// <summary>
    /// 타격(근접) 센트리.
    ///
    /// [담당 영역 — 전투 정보만]
    ///   공격력 / 공격 범위 / 쿨타임 / 추격 속도 / 스킬 게이지
    ///   기본 정보(이름, HP, 레벨, EXP, 추종 설정)는 SentryBase에 있습니다.
    ///
    /// [레이어 기반 탐색]
    ///   FindTarget()에서 EnemyLive 레이어만 탐색합니다.
    ///   EnemyDead / EnemyCombo 레이어는 Physics2D 결과에서 자동 제외됩니다.
    ///
    /// [공격 중 타겟 사망 예외처리]
    ///   TryAttack() 시작 시 타겟 유효성을 확인합니다.
    ///   데미지 적용 후 타겟이 KO되더라도 공격 연출은 계속 실행됩니다.
    ///   이후 로직에서 null 체크로 보호합니다.
    /// </summary>
    public class StrikeSentry : SentryBase
    {
        // ─────────────────────────────────────────
        //  Inspector 노출 필드 — 전투 정보 전담
        // ─────────────────────────────────────────

        [Header("타격 전투 설정")]
        [SerializeField] private int _attackDamage = 30;
        [SerializeField] private float _attackRange = 1.2f;
        [SerializeField] private float _attackCooldown = 1.2f;
        [SerializeField] private float _chaseSpeed = 3.5f;
        [SerializeField] private float _detectionRange = 8f;

        [Header("스킬 설정")]
        [SerializeField] private float _maxSkillGauge = 100f;
        [SerializeField] private float _skillGaugePerAttack = 15f;
        [SerializeField] private float _skillDamageMultiplier = 2f;

        [Header("레이어 설정")]
        [Tooltip("탐색할 적 레이어. EnemyLive 레이어를 설정하세요.")]
        [SerializeField] private LayerMask _enemyLayer;

        [Header("컴포넌트 참조")]
        [SerializeField] private SkillEffect_Strike _skillEffect;

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
                _skillEffect = GetComponent<SkillEffect_Strike>();
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

        /// <summary>
        /// EnemyLive 레이어 내에서 가장 가까운 적을 탐색합니다.
        /// EnemyDead / EnemyCombo 레이어는 Physics2D 결과에서 자동 제외됩니다.
        /// </summary>
        private void FindTarget()
        {
            Collider2D[] hits = Physics2D.OverlapCircleAll(
                transform.position, _detectionRange, _enemyLayer);

            if (hits.Length == 0) { _currentTarget = null; return; }

            float minDist = float.MaxValue;
            Transform closest = null;

            foreach (var col in hits)
            {
                if (col == null) continue;
                // IsDead 이중 체크 (레이어 변경 전 극히 짧은 프레임 간격 대비)
                Enemy e = col.GetComponent<Enemy>();
                if (e != null && e.IsDead) continue;

                float d = Vector2.Distance(transform.position, col.transform.position);
                if (d < minDist) { minDist = d; closest = col.transform; }
            }

            _currentTarget = closest;
        }

        /// <summary>
        /// 타겟 추격 및 공격 범위 내 공격 시도.
        /// 타겟 유효성 이중 체크: FindTarget과 이 메서드 사이 프레임에 적이 사망 가능.
        /// </summary>
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

            if (dist <= _attackRange)
            {
                BattleStop();
                TryAttack();
            }
            else
            {
                Vector2 dir = ((Vector2)_currentTarget.position
                               - (Vector2)transform.position).normalized;
                BattleMove(dir, _chaseSpeed * OverloadSpeedMultiplier);
            }
        }

        // ─────────────────────────────────────────
        //  기본 공격
        // ─────────────────────────────────────────

        /// <summary>
        /// 기본 공격.
        ///
        /// [타겟 사망 예외처리]
        ///   공격 시작 시점에 IsDead 체크로 유효성 확인.
        ///   TakeDamage 호출 후 타겟이 KO되더라도 공격 연출(_currentTarget null 체크 포함)은
        ///   정상 실행됩니다. 중간에 NullRef가 발생하지 않습니다.
        /// </summary>
        private void TryAttack()
        {
            if (Time.time < _lastAttackTime + _attackCooldown) return;
            if (_currentTarget == null) return;

            Enemy enemy = _currentTarget.GetComponent<Enemy>();
            if (enemy == null || enemy.IsDead)
            {
                _currentTarget = null;
                return;
            }

            _lastAttackTime = Time.time;

            // 데미지 적용 (이 시점 이후 enemy가 KO될 수 있음)
            enemy.TakeDamage(
                Mathf.RoundToInt(_attackDamage * OverloadDamageMultiplier),
                HitType.Strike,
                transform.position);

            // 연출 — _currentTarget이 파괴됐을 수 있으므로 null 체크 후 실행
            if (_currentTarget != null)
            {
                Vector3 punchDir = BattlePhysicsHelper
                    .PunchDir(BattlePhysicsHelper.FlatDirection(transform.position, _currentTarget.position) * 0.3f);
                transform.DOPunchPosition(punchDir, 0.2f, 5, 0.5f);
            }

            ChargeSkillGauge(_skillGaugePerAttack);
            Debug.Log($"[{SentryName}] 기본 공격 데미지: " +
                      $"{Mathf.RoundToInt(_attackDamage * OverloadDamageMultiplier)}");
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
        //  고유 스킬 (2연타)
        // ─────────────────────────────────────────

        private void UseSkill()
        {
            if (_currentTarget == null) return;

            if (_skillEffect == null)
            {
                FallbackSkill();
                return;
            }

            int dmg = Mathf.RoundToInt(
                _attackDamage * _skillDamageMultiplier * OverloadDamageMultiplier);
            Transform captured = _currentTarget;

            _skillEffect.PlaySkill(
                captured,
                onFirstHit: () =>
                {
                    if (captured == null) return;
                    Enemy e = captured.GetComponent<Enemy>();
                    e?.TakeDamage(dmg, HitType.Strike, transform.position);
                },
                onSecondHit: () =>
                {
                    if (captured == null) return;
                    Enemy e = captured.GetComponent<Enemy>();
                    e?.TakeDamage(dmg, HitType.Strike, transform.position);
                    SentryComboManager.Instance?.OnSentrySkillUsed();
                }
            );
        }

        private void FallbackSkill()
        {
            if (_currentTarget == null) return;
            int dmg = Mathf.RoundToInt(
                _attackDamage * _skillDamageMultiplier * OverloadDamageMultiplier);
            Enemy e = _currentTarget.GetComponent<Enemy>();
            e?.TakeDamage(dmg, HitType.Strike, transform.position);
        }

        // ─────────────────────────────────────────
        //  레벨업
        // ─────────────────────────────────────────

        protected override void LevelUp()
        {
            base.LevelUp();
            _attackDamage = Mathf.RoundToInt(_attackDamage * 1.1f);
            _chaseSpeed += 0.1f;
            _attackRange += 0.05f;
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