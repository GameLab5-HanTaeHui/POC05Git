using UnityEngine;
using DG.Tweening;

namespace SENTRY
{
    /// <summary>
    /// 벽(방어/압박) 센트리.
    ///
    /// [버그 수정 — TilemapCollider 뚫림]
    /// TryPush(), UseSkill(), FallbackSkill() 의 DOMove 호출에
    /// BattlePhysicsHelper.GetSafeTarget()을 적용했습니다.
    ///
    /// [버그 수정 — Y축 승천 / 맵 뒤 뚫림]
    /// 밀치기 방향 계산을 BattlePhysicsHelper.FlatDirection()으로 교체했습니다.
    /// 기존 (target - from).normalized는 Y 성분이 포함되어 오브젝트가
    /// 하늘로 승천하거나 맵 뒤로 뚫리는 문제가 있었습니다.
    /// FlatDirection()은 Y=0으로 평탄화한 방향만 반환합니다.
    ///
    /// [Inspector 추가 필드]
    /// _wallLayer     — TilemapCollider가 있는 레이어 (보통 Ground 또는 Wall)
    /// _enemyRadius   — 적 콜라이더 반경 (CircleCast 크기, 기본 0.4f)
    /// </summary>
    public class WallSentry : SentryBase
    {
        // ─────────────────────────────────────────
        //  Inspector 노출 필드
        // ─────────────────────────────────────────

        [Header("방어 / 밀치기 설정")]
        [Tooltip("기본 밀치기 데미지")]
        [SerializeField] private int _pushDamage = 15;

        [Tooltip("적 탐지 / 밀치기 유효 범위")]
        [SerializeField] private float _blockRange = 2.5f;

        [Tooltip("기본 공격 쿨타임 (초)")]
        [SerializeField] private float _attackCooldown = 2f;

        [Tooltip("기본 밀치기 거리")]
        [SerializeField] private float _pushDistance = 2.5f;

        [Tooltip("밀치기 DOMove 소요 시간 (초)")]
        [SerializeField] private float _pushDuration = 0.25f;

        [Tooltip("적에게 접근하는 속도")]
        [SerializeField] private float _approachSpeed = 2f;

        [Header("레이어 설정")]
        [Tooltip("탐색할 적 레이어. EnemyLive를 설정하세요.")]
        [SerializeField] private LayerMask _enemyLayer;

        [Tooltip("밀치기 CircleCast 충돌 검사 레이어.\n" +
                 "TilemapCollider가 있는 레이어를 설정하세요 (보통 Ground 또는 Wall).")]
        [SerializeField] private LayerMask _wallLayer;

        [Header("스킬 설정")]
        [SerializeField] private float _maxSkillGauge = 100f;
        [SerializeField] private float _skillGaugePerAttack = 20f;
        [SerializeField] private float _stunDuration = 2.5f;
        [SerializeField] private float _skillDamageMultiplier = 2.5f;
        [SerializeField] private float _skillPushDistMultiplier = 2f;

        [Header("물리 보정 설정")]
        [Tooltip("밀치기 CircleCast 반경. 적 콜라이더 반경과 맞추세요 (기본 0.4f).")]
        [SerializeField] private float _enemyRadius = 0.4f;

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
        /// [버그 수정 — TilemapCollider 뚫림]
        /// GetSafeTarget()으로 밀치기 목표 위치를 벽 직전으로 클램핑합니다.
        ///
        /// [버그 수정 — Y축 승천 / 맵 뒤 뚫림]
        /// FlatDirection()으로 Y 성분을 제거한 방향만 계산합니다.
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

            enemy.TakeDamage(
                Mathf.RoundToInt(_pushDamage * OverloadDamageMultiplier),
                HitType.Strike,
                transform.position);

            if (!enemy.IsDead && _currentTarget != null)
            {
                // [Y축 고정] FlatDirection — Y 성분 제거하여 승천/뚫림 방지
                Vector3 pushDir = BattlePhysicsHelper.FlatDirection(
                    transform.position, _currentTarget.position);
                Vector3 rawTarget = _currentTarget.position + pushDir * _pushDistance;
                Vector3 safeTarget = BattlePhysicsHelper.GetSafeTarget(
                    from: _currentTarget.position,
                    to: rawTarget,
                    radius: _enemyRadius,
                    wallLayer: _wallLayer);

                Transform t = _currentTarget;
                _currentTarget.DOMove(safeTarget, _pushDuration)
                    .SetEase(Ease.OutQuart)
                    .OnUpdate(() => BattlePhysicsHelper.ClampZ(t));
            }

            if (_currentTarget != null)
            {
                Vector3 punchDir = BattlePhysicsHelper
                    .PunchDir(BattlePhysicsHelper.FlatDirection(transform.position, _currentTarget.position) * 0.4f);
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

        /// <summary>
        /// 스킬 게이지 만참 시 강한 밀치기 + 기절을 발동합니다.
        ///
        /// [버그 수정 — Y축 승천 / 맵 뒤 뚫림]
        /// onImpact 콜백 내 방향 계산을 FlatDirection()으로 교체했습니다.
        /// </summary>
        private void UseSkill()
        {
            if (_currentTarget == null) return;
            if (_skillEffect == null) { FallbackSkill(); return; }

            int skillDmg = Mathf.RoundToInt(
                _pushDamage * _skillDamageMultiplier * OverloadDamageMultiplier);
            float pushDist = _pushDistance * _skillPushDistMultiplier;
            float stunDur = _stunDuration;
            Transform captured = _currentTarget;
            Vector3 selfPos = transform.position; // 콜백 시점에 transform이 null일 수 있으므로 캡처

            _skillEffect.PlaySkill(
                captured,
                onImpact: () =>
                {
                    if (captured == null) return;
                    Enemy e = captured.GetComponent<Enemy>();
                    if (e == null) return;

                    e.TakeDamage(skillDmg, HitType.Strike, selfPos);
                    e.Stun(stunDur);

                    if (!e.IsDead && captured != null)
                    {
                        // [Y축 고정] FlatDirection — Y 성분 제거
                        Vector3 pushDir = BattlePhysicsHelper.FlatDirection(selfPos, captured.position);
                        Vector3 rawTarget = captured.position + pushDir * pushDist;
                        Vector3 safeTarget = BattlePhysicsHelper.GetSafeTarget(
                            from: captured.position,
                            to: rawTarget,
                            radius: _enemyRadius,
                            wallLayer: _wallLayer);

                        captured.DOMove(safeTarget, _pushDuration * 0.5f).SetEase(Ease.OutExpo);
                    }

                    SentryComboManager.Instance?.OnSentrySkillUsed();
                }
            );
        }

        /// <summary>
        /// SkillEffect_Wall 컴포넌트가 없을 때 즉시 스킬을 처리합니다.
        ///
        /// [버그 수정 — Y축 승천 / 맵 뒤 뚫림]
        /// 방향 계산을 FlatDirection()으로 교체했습니다.
        /// </summary>
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
                // [Y축 고정] FlatDirection — Y 성분 제거
                Vector3 pushDir = BattlePhysicsHelper.FlatDirection(
                    transform.position, _currentTarget.position);
                Vector3 rawTarget = _currentTarget.position
                                     + pushDir * _pushDistance * _skillPushDistMultiplier;
                Vector3 safeTarget = BattlePhysicsHelper.GetSafeTarget(
                    from: _currentTarget.position,
                    to: rawTarget,
                    radius: _enemyRadius,
                    wallLayer: _wallLayer);

                _currentTarget.DOMove(safeTarget, _pushDuration).SetEase(Ease.OutExpo);
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