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
    /// [레이어 기반 탐색]
    ///   FindTarget()에서 EnemyLive 레이어만 탐색합니다.
    /// </summary>
    public class ShootSentry : SentryBase
    {
        // ─────────────────────────────────────────
        //  Inspector 노출 필드 — 전투 정보 전담
        // ─────────────────────────────────────────

        [Header("사격 전투 설정")]
        [SerializeField] private int _attackDamage = 20;
        [SerializeField] private float _detectionRange = 10f;
        [SerializeField] private float _preferredDistance = 5f;
        [SerializeField] private float _attackCooldown = 1.5f;
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
                _skillEffect = GetComponent<SkillEffect_Shoot>();
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

        /// <summary>교전 거리 유지하며 사격 시도.</summary>
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

            if (dist < _preferredDistance * 0.7f)
                BattleMove(-dir, speed);
            else if (dist > _preferredDistance * 1.3f)
                BattleMove(dir, speed);
            else
            {
                BattleStop();
                TryAttack();
            }
        }

        // ─────────────────────────────────────────
        //  기본 공격
        // ─────────────────────────────────────────

        private void TryAttack()
        {
            if (Time.time < _lastAttackTime + _attackCooldown) return;
            if (_currentTarget == null) return;

            Enemy e = _currentTarget.GetComponent<Enemy>();
            if (e == null || e.IsDead) { _currentTarget = null; return; }

            _lastAttackTime = Time.time;

            // 탄환 발사 (데미지 적용 후 타겟 KO 가능 — FireBullet 내부 null 안전)
            FireBullet(_currentTarget,
                Mathf.RoundToInt(_attackDamage * OverloadDamageMultiplier));

            if (_currentTarget != null)
            {
                Vector3 shootDir =
                    (_currentTarget.position - transform.position).normalized;
                transform.DOPunchPosition(shootDir * 0.1f, 0.1f, 5, 0.5f);
            }

            ChargeSkillGauge(_skillGaugePerAttack);
        }

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
        //  고유 스킬 (3연발)
        // ─────────────────────────────────────────

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
                onEachShot: () => FireBullet(captured, skillDmg)
            );

            StartCoroutine(WaitSkillComplete());
        }

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

        protected override void LevelUp()
        {
            base.LevelUp();
            _attackDamage = Mathf.RoundToInt(_attackDamage * 1.1f);
            _detectionRange += 0.2f;
            _preferredDistance += 0.1f;
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, _detectionRange);
            Gizmos.color = Color.blue;
            Gizmos.DrawWireSphere(transform.position, _preferredDistance);
        }
#endif
    }
}