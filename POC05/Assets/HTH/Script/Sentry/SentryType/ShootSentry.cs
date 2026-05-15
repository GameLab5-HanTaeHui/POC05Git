using UnityEngine;
using DG.Tweening;
using System.Collections;

namespace SENTRY
{
    /// <summary>
    /// 사격(원거리) 센트리.
    ///
    /// [담당 영역 — 전투 정보만]
    ///   공격력 / 교전 거리 / 쿨타임 / 재배치 속도 / 스킬 게이지
    ///   기본 정보(이름, HP, 레벨, EXP, 추종 설정)는 SentryBase에 있습니다.
    ///
    /// [변경 사항 — 후퇴 중 공격 허용]
    ///   기존: _preferredDistance 범위 내에 있을 때만 TryAttack() 호출
    ///         너무 가까우면 후퇴만 하고 공격 불가 → 혼자 남으면 계속 도망만 감
    ///   수정: 거리 조절(이동)과 공격을 독립적으로 처리
    ///         후퇴 중이거나 접근 중이어도 쿨타임이 됐으면 TryAttack() 호출
    ///         단, 최소 공격 가능 거리(_maxAttackRange) 이내일 때만 발사
    ///
    /// [레이어 기반 탐색]
    ///   FindTarget()에서 EnemyLive 레이어만 탐색합니다.
    /// </summary>
    public class ShootSentry : SentryBase
    {
        // ─────────────────────────────────────────
        //  Inspector 노출 필드 — 전투 정보 전담
        // ─────────────────────────────────────────

        [Header("사격 전투 설정")]
        [Tooltip("기본 공격 데미지")]
        [SerializeField] private int _attackDamage = 20;

        [Tooltip("적 탐지 최대 반경")]
        [SerializeField] private float _detectionRange = 10f;

        [Tooltip("유지하려는 교전 거리 (이 거리를 중심으로 0.7~1.3배 범위 내에서 자유롭게 이동)")]
        [SerializeField] private float _preferredDistance = 5f;

        [Tooltip("이 거리 이내면 후퇴 중에도 공격합니다.\n" +
                 "보통 _detectionRange와 같거나 약간 작게 설정합니다.")]
        [SerializeField] private float _maxAttackRange = 9f;

        [Tooltip("공격 쿨타임 (초)")]
        [SerializeField] private float _attackCooldown = 1.5f;

        [Tooltip("거리 조절 이동 속도")]
        [SerializeField] private float _repositionSpeed = 2f;

        [Header("레이어 설정")]
        [Tooltip("탐색할 적 레이어. EnemyLive를 설정하세요.")]
        [SerializeField] private LayerMask _enemyLayer;

        [Tooltip("시야 차폐 레이어")]
        [SerializeField] private LayerMask _obstacleLayer;

        [Header("탄환 설정")]
        [SerializeField] private GameObject _bulletPrefab;
        [SerializeField] private Transform _firePoint;

        [Header("스킬 설정")]
        [SerializeField] private float _maxSkillGauge = 100f;
        [SerializeField] private float _skillGaugePerAttack = 12f;
        [SerializeField] private float _skillDamageMultiplier = 1.5f;

        [Header("컴포넌트 참조")]
        [SerializeField] private SkillEffect_Shoot _skillEffect;

        // ─────────────────────────────────────────
        //  내부 상태
        // ─────────────────────────────────────────

        /// <summary>현재 추격 중인 적</summary>
        private Transform _currentTarget;

        /// <summary>마지막 공격 시각</summary>
        private float _lastAttackTime;

        /// <summary>현재 스킬 게이지</summary>
        private float _currentSkillGauge = 0f;

        /// <summary>배틀 AI 활성 여부</summary>
        private bool _isInBattle = false;

        // ─────────────────────────────────────────
        //  외부 공개 프로퍼티
        // ─────────────────────────────────────────

        /// <summary>현재 스킬 게이지 (PlayerBattleUIManager 표시용)</summary>
        public float SkillGauge => _currentSkillGauge;

        /// <summary>최대 스킬 게이지 (PlayerBattleUIManager 표시용)</summary>
        public float MaxSkillGauge => _maxSkillGauge;

        // ─────────────────────────────────────────
        //  초기화
        // ─────────────────────────────────────────

        public override void Init(Transform player)
        {
            base.Init(player);
            _currentSkillGauge = 0f;
            if (_skillEffect == null)
                _skillEffect = GetComponent<SkillEffect_Shoot>();
        }

        // ─────────────────────────────────────────
        //  배틀 진입 / 종료
        // ─────────────────────────────────────────

        /// <summary>
        /// 배틀 시작 시 BattleManager.BattleStartRoutine()에서 호출합니다.
        /// AI를 활성화하고 플레이어 추종을 멈춥니다.
        /// </summary>
        public void EnterBattle()
        {
            _isInBattle = true;
            StopFollowing();
        }

        /// <summary>
        /// 배틀 종료 시 BattleManager.EndBattle()에서 호출합니다.
        /// AI를 비활성화하고 플레이어 추종을 재개합니다.
        /// </summary>
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

        /// <summary>EnemyLive 레이어 내 시야 확보된 가장 가까운 적 탐색.</summary>
        private void FindTarget()
        {
            Collider2D[] hits = Physics2D.OverlapCircleAll(
                transform.position, _detectionRange, _enemyLayer);

            if (hits.Length == 0) { _currentTarget = null; return; }

            float minDist = float.MaxValue;
            Transform best = null;

            foreach (var col in hits)
            {
                if (col == null) continue;
                Enemy e = col.GetComponent<Enemy>();
                if (e != null && e.IsDead) continue;

                float d = Vector2.Distance(transform.position, col.transform.position);
                if (d >= minDist) continue;

                Vector2 dir = (col.transform.position - transform.position).normalized;
                RaycastHit2D hit = Physics2D.Raycast(
                    transform.position, dir, d, _obstacleLayer);

                if (hit.collider == null) { minDist = d; best = col.transform; }
            }

            _currentTarget = best;
        }

        /// <summary>
        /// 교전 거리 조절과 공격을 독립적으로 처리합니다.
        ///
        /// [변경 내용]
        /// 기존 구조: 거리 범위에 따라 후퇴 / 접근 / 공격 중 하나만 실행
        ///   → 후퇴 중에는 공격 불가 → 혼자 남으면 계속 도망만 감
        ///
        /// 변경 구조: 이동과 공격을 분리하여 독립 실행
        ///   Step 1. 거리 조절 — 너무 가까우면 후퇴, 너무 멀면 접근
        ///   Step 2. 공격 — _maxAttackRange 이내이고 쿨타임이 됐으면 항상 TryAttack()
        ///
        ///   후퇴 중이어도 적이 _maxAttackRange 이내에 있으면 발사합니다.
        ///   이로써 혼자 남아 계속 후퇴하는 상황에서도 공격이 가능합니다.
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
            Vector2 dir = ((Vector2)_currentTarget.position
                             - (Vector2)transform.position).normalized;
            float speed = _repositionSpeed * OverloadSpeedMultiplier;

            // ── Step 1. 거리 조절 ──
            // 너무 가까우면 후퇴, 너무 멀면 접근, 범위 내면 정지
            if (dist < _preferredDistance * 0.7f)
                BattleMove(-dir, speed);   // 후퇴
            else if (dist > _preferredDistance * 1.3f)
                BattleMove(dir, speed);    // 접근
            else
                BattleStop();              // 적정 거리 — 정지

            // ── Step 2. 공격 ──
            // 거리 조절 상태(후퇴/접근/정지)와 무관하게
            // _maxAttackRange 이내이고 쿨타임이 됐으면 발사합니다.
            if (dist <= _maxAttackRange)
                TryAttack();
        }

        // ─────────────────────────────────────────
        //  기본 공격
        // ─────────────────────────────────────────

        /// <summary>
        /// 쿨타임 및 타겟 유효성 확인 후 발사합니다.
        /// HandleBattleAI()에서 거리·이동 상태와 무관하게 호출됩니다.
        /// </summary>
        private void TryAttack()
        {
            if (Time.time < _lastAttackTime + _attackCooldown) return;
            if (_currentTarget == null) return;

            Enemy e = _currentTarget.GetComponent<Enemy>();
            if (e == null || e.IsDead) { _currentTarget = null; return; }

            _lastAttackTime = Time.time;

            FireBullet(_currentTarget,
                Mathf.RoundToInt(_attackDamage * OverloadDamageMultiplier));

            // 반동 연출 (타겟이 여전히 유효할 때만)
            if (_currentTarget != null)
            {
                Vector3 shootDir =
                    (_currentTarget.position - transform.position).normalized;
                transform.DOPunchPosition(shootDir * 0.1f, 0.1f, 5, 0.5f);
            }

            ChargeSkillGauge(_skillGaugePerAttack);
        }

        /// <summary>
        /// 탄환 프리팹을 발사합니다.
        /// SentryComboManager의 콤보 연출에서도 직접 호출합니다.
        /// </summary>
        /// <param name="target">발사 목표 Transform</param>
        /// <param name="damage">적용할 데미지</param>
        public void FireBullet(Transform target, int damage)
        {
            if (_bulletPrefab == null || target == null) return;

            Vector3 spawnPos = _firePoint != null
                ? _firePoint.position : transform.position;

            GameObject obj = Instantiate(_bulletPrefab, spawnPos, Quaternion.identity);
            SentryBullet bullet = obj.GetComponent<SentryBullet>();
            if (bullet != null) { bullet.damage = damage; bullet.Setup(target); }
        }

        // ─────────────────────────────────────────
        //  스킬 게이지
        // ─────────────────────────────────────────

        /// <summary>
        /// 공격 1회마다 스킬 게이지를 충전합니다.
        /// 최대치 도달 시 UseSkill()을 자동 호출합니다.
        /// </summary>
        private void ChargeSkillGauge(float amount)
        {
            _currentSkillGauge = Mathf.Min(
                _currentSkillGauge + amount, _maxSkillGauge);

            if (_currentSkillGauge >= _maxSkillGauge &&
                (_skillEffect == null || !_skillEffect.IsPlaying))
            {
                _currentSkillGauge = 0f;
                UseSkill();
            }
        }

        // ─────────────────────────────────────────
        //  고유 스킬 (3연발)
        // ─────────────────────────────────────────

        /// <summary>
        /// 스킬 게이지 만참 시 3연발 스킬을 발동합니다.
        /// SkillEffect_Shoot 컴포넌트가 없으면 즉시 3발을 발사합니다.
        /// </summary>
        private void UseSkill()
        {
            if (_currentTarget == null) return;

            if (_skillEffect == null)
            {
                int dmg = Mathf.RoundToInt(
                    _attackDamage * _skillDamageMultiplier * OverloadDamageMultiplier);
                for (int i = 0; i < 3; i++) FireBullet(_currentTarget, dmg);
                SentryComboManager.Instance?.OnSentrySkillUsed();
                return;
            }

            int skillDmg = Mathf.RoundToInt(
                _attackDamage * _skillDamageMultiplier * OverloadDamageMultiplier);
            Transform captured = _currentTarget;

            _skillEffect.PlaySkill(
                captured,
                onEachShot: () => FireBullet(captured, skillDmg));

            StartCoroutine(WaitSkillComplete());
        }

        /// <summary>스킬 연출 완료 후 SentryComboManager에 통보합니다.</summary>
        private IEnumerator WaitSkillComplete()
        {
            yield return null;
            while (_skillEffect != null && _skillEffect.IsPlaying)
                yield return null;
            SentryComboManager.Instance?.OnSentrySkillUsed();
        }

        // ─────────────────────────────────────────
        //  레벨업
        // ─────────────────────────────────────────

        /// <summary>
        /// 레벨업 시 공격력, 탐지 범위, 교전 거리를 증가시킵니다.
        /// 최대 공격 범위(_maxAttackRange)도 탐지 범위에 맞게 갱신합니다.
        /// </summary>
        protected override void LevelUp()
        {
            base.LevelUp();
            _attackDamage = Mathf.RoundToInt(_attackDamage * 1.1f);
            _detectionRange += 0.2f;
            _preferredDistance += 0.1f;

            // 탐지 범위가 늘면 최대 공격 범위도 동기화
            _maxAttackRange = _detectionRange * 0.9f;
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            // 탐지 범위 (하늘색)
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, _detectionRange);

            // 교전 유지 범위 (파랑)
            Gizmos.color = Color.blue;
            Gizmos.DrawWireSphere(transform.position, _preferredDistance);

            // 최대 공격 가능 범위 (노랑)
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, _maxAttackRange);
        }
#endif
    }
}