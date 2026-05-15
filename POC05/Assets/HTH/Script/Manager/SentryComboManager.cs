using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;

namespace SENTRY
{
    /// <summary>
    /// 센트리 콤보 시스템을 관리하는 싱글턴 매니저.
    /// (구 ComboManager → SentryComboManager 이름 변경)
    ///
    /// [버그 수정 — TilemapCollider 뚫림]
    /// 콤보 연출 내 모든 DOMove 호출(센트리 이동, 적 이동, 복귀)에
    /// BattlePhysicsHelper.GetSafeTarget()을 적용했습니다.
    ///
    ///   적용 대상:
    ///     CalcSafeComboPositions() — 콤보 집결 위치 보정
    ///     ReturnFromCombo()       — 복귀 DOMove 보정
    ///     Combo_StrikeShoot()     — Strike 접근 DOMove 보정
    ///     Combo_StrikeWall()      — Strike 접근, Wall 돌진 DOMove 보정
    ///     Combo_ShootWall()       — Wall 낙하 DOMove 보정
    ///     Combo_AllThree()        — Strike/Wall 접근 + 최종 밀치기 DOMove 보정
    ///
    ///   3콤보 4단계 AddForce → DOMove + GetSafeTarget으로 교체:
    ///     AddForce는 Kinematic 상태에서 무시되며, Dynamic으로 전환해도
    ///     배틀 필드 구조에서 벽 뚫림이 발생했습니다.
    ///
    /// [Inspector 추가 필드]
    ///   _wallLayer     — TilemapCollider 레이어 (보통 Ground 또는 Wall)
    ///   _sentryRadius  — 센트리 콜라이더 반경 (기본 0.4f)
    ///   _enemyRadius   — 적 콜라이더 반경 (기본 0.4f)
    /// </summary>
    public class SentryComboManager : MonoBehaviour
    {
        public static SentryComboManager Instance { get; private set; }

        // ─────────────────────────────────────────
        //  Inspector — 센트리 참조
        // ─────────────────────────────────────────

        [Header("센트리 참조")]
        [SerializeField] private StrikeSentry _strikeSentry;
        [SerializeField] private ShootSentry _shootSentry;
        [SerializeField] private WallSentry _wallSentry;

        // ─────────────────────────────────────────
        //  Inspector — 물리 보정 (벽 뚫림 방지)
        // ─────────────────────────────────────────

        [Header("물리 보정 — 벽 뚫림 방지")]
        [Tooltip("TilemapCollider가 있는 레이어 (보통 Ground 또는 Wall).\n" +
                 "콤보 DOMove 전 CircleCast 충돌 검사에 사용됩니다.")]
        [SerializeField] private LayerMask _wallLayer;

        [Tooltip("센트리 콜라이더 반경 (CircleCast 크기). 기본 0.4f")]
        [SerializeField] private float _sentryRadius = 0.4f;

        [Tooltip("적 콜라이더 반경 (CircleCast 크기). 기본 0.4f")]
        [SerializeField] private float _enemyRadius = 0.4f;

        // ─────────────────────────────────────────
        //  Inspector — 콤보 게이지
        // ─────────────────────────────────────────

        [Header("콤보 게이지 설정")]
        [SerializeField] private float _maxComboGauge = 100f;
        [SerializeField] private float _comboGaugePerKill = 20f;
        [SerializeField] private float _comboGaugePerSkill = 15f;

        // ─────────────────────────────────────────
        //  Inspector — 콤보 쿨타임
        // ─────────────────────────────────────────

        [Header("콤보 쿨타임 (초)")]
        [SerializeField] private float _combo3Cooldown = 60f;
        [SerializeField] private float _comboACooldown = 25f;
        [SerializeField] private float _comboBCooldown = 25f;
        [SerializeField] private float _comboCCooldown = 25f;

        // ─────────────────────────────────────────
        //  Inspector — 콤보 포지션 오프셋
        // ─────────────────────────────────────────

        [Header("콤보 포지션 오프셋 (적 위치 기준 상대 좌표)")]
        [SerializeField] private Vector2 _strikeComboOffset = new Vector2(-1.5f, 0f);
        [SerializeField] private Vector2 _shootComboOffset = new Vector2(-4f, 0f);
        [SerializeField] private Vector2 _wallComboOffset = new Vector2(1f, 0f);

        // ─────────────────────────────────────────
        //  Inspector — 3콤보 전용
        // ─────────────────────────────────────────

        [Header("3콤보 전용 설정")]
        [SerializeField] private int _combo3BulletCount = 10;
        [SerializeField] private float _combo3BulletInterval = 0.1f;
        [SerializeField] private float _combo3DamageMultiplier = 4f;

        [Tooltip("3콤보 4단계 최종 밀치기 거리")]
        [SerializeField] private float _combo3FinalPushDist = 5f;

        [Tooltip("3콤보 4단계 최종 밀치기 소요 시간 (초)")]
        [SerializeField] private float _combo3FinalPushDuration = 0.3f;

        // ─────────────────────────────────────────
        //  Inspector — 2콤보 설정
        // ─────────────────────────────────────────

        [Header("2콤보 설정")]
        [SerializeField] private float _combo2DamageMultiplier = 2.5f;
        [SerializeField] private GameObject _piercingBulletPrefab;

        // ─────────────────────────────────────────
        //  콤보 타입 열거형
        // ─────────────────────────────────────────

        private enum ComboType { AllThree, StrikeShoot, StrikeWall, ShootWall }

        // ─────────────────────────────────────────
        //  내부 상태
        // ─────────────────────────────────────────

        private float _currentComboGauge = 0f;
        private bool _isComboPlaying = false;
        private Queue<ComboType> _comboQueue = new Queue<ComboType>();

        private float _combo3CooldownTimer = 0f;
        private float _comboACooldownTimer = 0f;
        private float _comboBCooldownTimer = 0f;
        private float _comboCCooldownTimer = 0f;

        // ─────────────────────────────────────────
        //  외부 공개 프로퍼티
        // ─────────────────────────────────────────

        public float ComboGauge => _currentComboGauge;
        public float MaxComboGauge => _maxComboGauge;
        public float Combo2CooldownRatio => GetBest2ComboCooldownRatio();
        public float Combo3CooldownRatio => GetRatio(_combo3CooldownTimer, _combo3Cooldown);

        // ─────────────────────────────────────────
        //  유니티 생명주기
        // ─────────────────────────────────────────

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);
        }

        private void Update()
        {
            TickCooldowns();
        }

        private void TickCooldowns()
        {
            float dt = Time.deltaTime;
            if (_combo3CooldownTimer > 0f) _combo3CooldownTimer -= dt;
            if (_comboACooldownTimer > 0f) _comboACooldownTimer -= dt;
            if (_comboBCooldownTimer > 0f) _comboBCooldownTimer -= dt;
            if (_comboCCooldownTimer > 0f) _comboCCooldownTimer -= dt;
        }

        // ─────────────────────────────────────────
        //  외부 이벤트
        // ─────────────────────────────────────────

        public void OnBattleStart()
        {
            _currentComboGauge = 0f;
            _comboQueue.Clear();
            _isComboPlaying = false;
        }

        public void OnBattleEnd()
        {
            _isComboPlaying = false;
            _comboQueue.Clear();
        }

        public void OnEnemyKilled()
        {
            _currentComboGauge = Mathf.Min(_currentComboGauge + _comboGaugePerKill, _maxComboGauge);
            if (_currentComboGauge >= _maxComboGauge)
            {
                _currentComboGauge = 0f;
                TryEnqueueCombo();
            }
        }

        public void OnSentrySkillUsed()
        {
            _currentComboGauge = Mathf.Min(_currentComboGauge + _comboGaugePerSkill, _maxComboGauge);
            if (_currentComboGauge >= _maxComboGauge)
            {
                _currentComboGauge = 0f;
                TryEnqueueCombo();
            }
        }

        // ─────────────────────────────────────────
        //  콤보 큐 추가
        // ─────────────────────────────────────────

        private void TryEnqueueCombo()
        {
            bool strikeAlive = _strikeSentry != null && !_strikeSentry.IsKnockedOut;
            bool shootAlive = _shootSentry != null && !_shootSentry.IsKnockedOut;
            bool wallAlive = _wallSentry != null && !_wallSentry.IsKnockedOut;

            if (strikeAlive && shootAlive && wallAlive && _combo3CooldownTimer <= 0f)
            {
                _comboQueue.Enqueue(ComboType.AllThree);
                TryDequeueAndPlay();
                return;
            }

            var candidates = new List<ComboType>();
            if (strikeAlive && shootAlive && _comboACooldownTimer <= 0f) candidates.Add(ComboType.StrikeShoot);
            if (strikeAlive && wallAlive && _comboBCooldownTimer <= 0f) candidates.Add(ComboType.StrikeWall);
            if (shootAlive && wallAlive && _comboCCooldownTimer <= 0f) candidates.Add(ComboType.ShootWall);

            if (candidates.Count > 0)
            {
                _comboQueue.Enqueue(candidates[Random.Range(0, candidates.Count)]);
                TryDequeueAndPlay();
            }
        }

        // ─────────────────────────────────────────
        //  콤보 큐 실행
        // ─────────────────────────────────────────

        private void TryDequeueAndPlay()
        {
            if (_isComboPlaying || _comboQueue.Count == 0) return;

            Enemy target = FindNearestEnemy();
            if (target == null) { _comboQueue.Clear(); return; }

            ComboType next = _comboQueue.Dequeue();
            switch (next)
            {
                case ComboType.AllThree:
                    _combo3CooldownTimer = _combo3Cooldown;
                    StartCoroutine(Combo_AllThree(target));
                    break;
                case ComboType.StrikeShoot:
                    _comboACooldownTimer = _comboACooldown;
                    StartCoroutine(Combo_StrikeShoot(target));
                    break;
                case ComboType.StrikeWall:
                    _comboBCooldownTimer = _comboBCooldown;
                    StartCoroutine(Combo_StrikeWall(target));
                    break;
                case ComboType.ShootWall:
                    _comboCCooldownTimer = _comboCCooldown;
                    StartCoroutine(Combo_ShootWall(target));
                    break;
            }
        }

        private void OnComboFinished()
        {
            _isComboPlaying = false;
            if (_comboQueue.Count > 0) TryDequeueAndPlay();
        }

        // ─────────────────────────────────────────
        //  콤보 집결 위치 계산 (벽 충돌 보정 포함)
        // ─────────────────────────────────────────

        /// <summary>
        /// 적 위치 기준 각 센트리 콤보 집결 위치를 계산하고
        /// BattlePhysicsHelper로 벽 충돌 여부를 검사해 안전한 위치로 보정합니다.
        /// </summary>
        private (Vector3 strike, Vector3 shoot, Vector3 wall) CalcSafeComboPositions(Enemy target)
        {
            Vector3 basePos = target.transform.position;

            // [Y축 고정] 콤보 집결 위치는 적 위치 기준 오프셋이므로
            // 오프셋 자체에 Y 성분을 제거하고 적의 Y를 그대로 유지합니다.
            Vector3 rawStrike = new Vector3(
                basePos.x + _strikeComboOffset.x, basePos.y, basePos.z + _strikeComboOffset.y);
            Vector3 rawShoot = new Vector3(
                basePos.x + _shootComboOffset.x, basePos.y, basePos.z + _shootComboOffset.y);
            Vector3 rawWall = new Vector3(
                basePos.x + _wallComboOffset.x, basePos.y, basePos.z + _wallComboOffset.y);

            Vector3 safeStrike = _strikeSentry != null
                ? BattlePhysicsHelper.GetSafeTarget(
                    _strikeSentry.transform.position, rawStrike, _sentryRadius, _wallLayer)
                : rawStrike;

            Vector3 safeShoot = _shootSentry != null
                ? BattlePhysicsHelper.GetSafeTarget(
                    _shootSentry.transform.position, rawShoot, _sentryRadius, _wallLayer)
                : rawShoot;

            Vector3 safeWall = _wallSentry != null
                ? BattlePhysicsHelper.GetSafeTarget(
                    _wallSentry.transform.position, rawWall, _sentryRadius, _wallLayer)
                : rawWall;

            return (safeStrike, safeShoot, safeWall);
        }

        // ─────────────────────────────────────────
        //  복귀 DOMove (벽 충돌 보정 포함)
        // ─────────────────────────────────────────

        /// <summary>
        /// 콤보 종료 후 센트리를 원래 위치로 복귀시킵니다.
        /// 복귀 경로에도 BattlePhysicsHelper를 적용해 벽 뚫림을 방지합니다.
        /// </summary>
        private IEnumerator ReturnFromCombo(SentryBase sentry, Vector3 prePos, float duration = 0.5f)
        {
            if (sentry == null) yield break;

            Vector3 safeReturn = BattlePhysicsHelper.GetSafeTarget(
                sentry.transform.position, prePos, _sentryRadius, _wallLayer);

            yield return sentry.transform
                .DOMove(safeReturn, duration)
                .SetEase(Ease.OutElastic)
                .WaitForCompletion();
        }

        // ─────────────────────────────────────────
        //  2콤보 A: 타격 + 사격
        // ─────────────────────────────────────────

        private IEnumerator Combo_StrikeShoot(Enemy target)
        {
            _isComboPlaying = true;
            Debug.Log("<color=cyan>[2콤보 A] 타격 + 사격!</color>");

            Vector3 strikePrePos = _strikeSentry.transform.position;
            Vector3 shootPrePos = _shootSentry.transform.position;

            SetInvincible(true, _strikeSentry, _shootSentry);
            _strikeSentry.StopFollowing();
            _shootSentry.StopFollowing();

            var (strikeComboPos, shootComboPos, _) = CalcSafeComboPositions(target);

            // 집결 이동 — 이미 CalcSafeComboPositions에서 보정됨
            _strikeSentry.transform.DOMove(strikeComboPos, 0.5f).SetEase(Ease.OutBack);
            yield return _shootSentry.transform
                .DOMove(shootComboPos, 0.5f).SetEase(Ease.OutBack).WaitForCompletion();

            yield return new WaitForSeconds(0.2f);

            // Strike → Shoot 총구 방향 접근 (Y축 고정 + 벽 보정)
            Vector3 rawImpact = _shootSentry.transform.position + Vector3.right * 0.4f;
            rawImpact.y = _strikeSentry.transform.position.y; // Y 고정
            Vector3 safeImpact = BattlePhysicsHelper.GetSafeTarget(
                _strikeSentry.transform.position, rawImpact, _sentryRadius, _wallLayer);

            yield return _strikeSentry.transform
                .DOMove(safeImpact, 0.1f).SetEase(Ease.InExpo).WaitForCompletion();

            _strikeSentry.transform.DOShakePosition(0.15f, 0.25f, 15, 90f);
            _shootSentry.transform.DOShakePosition(0.2f, 0.2f, 12, 90f);

            yield return new WaitForSeconds(0.1f);

            if (target != null && _piercingBulletPrefab != null)
            {
                int comboDamage = Mathf.RoundToInt(20 * _combo2DamageMultiplier);
                GameObject bulletObj = Instantiate(
                    _piercingBulletPrefab, _shootSentry.transform.position, Quaternion.identity);
                SentryPiercingBullet pb = bulletObj.GetComponent<SentryPiercingBullet>();
                if (pb != null) { pb.damage = comboDamage; pb.Setup(target.transform); }
            }

            _shootSentry.transform.DOPunchPosition(Vector3.left * 0.35f, 0.15f, 8, 0.5f);
            yield return new WaitForSeconds(0.4f);

            yield return StartCoroutine(ReturnFromCombo(_strikeSentry, strikePrePos));
            yield return StartCoroutine(ReturnFromCombo(_shootSentry, shootPrePos));

            SetInvincible(false, _strikeSentry, _shootSentry);
            _strikeSentry.StartFollowing();
            _shootSentry.StartFollowing();

            Debug.Log("[2콤보 A] 종료");
            OnComboFinished();
        }

        // ─────────────────────────────────────────
        //  2콤보 B: 타격 + 벽
        // ─────────────────────────────────────────

        private IEnumerator Combo_StrikeWall(Enemy target)
        {
            _isComboPlaying = true;
            Debug.Log("<color=cyan>[2콤보 B] 타격 + 벽!</color>");

            Vector3 strikePrePos = _strikeSentry.transform.position;
            Vector3 wallPrePos = _wallSentry.transform.position;

            SetInvincible(true, _strikeSentry, _wallSentry);
            _strikeSentry.StopFollowing();
            _wallSentry.StopFollowing();

            var (strikeComboPos, _, wallComboPos) = CalcSafeComboPositions(target);

            _strikeSentry.transform.DOMove(strikeComboPos, 0.5f).SetEase(Ease.OutBack);
            yield return _wallSentry.transform
                .DOMove(wallComboPos, 0.5f).SetEase(Ease.OutBack).WaitForCompletion();

            yield return new WaitForSeconds(0.2f);

            // Strike → Wall 옆 타격 위치 (Y축 고정 + 벽 보정)
            Vector3 rawHit = _wallSentry.transform.position + Vector3.left * 0.3f;
            rawHit.y = _strikeSentry.transform.position.y; // Y 고정
            Vector3 safeHit = BattlePhysicsHelper.GetSafeTarget(
                _strikeSentry.transform.position, rawHit, _sentryRadius, _wallLayer);

            yield return _strikeSentry.transform
                .DOMove(safeHit, 0.1f).SetEase(Ease.InExpo).WaitForCompletion();

            _strikeSentry.transform.DOShakePosition(0.2f, 0.3f, 15, 90f);
            yield return new WaitForSeconds(0.08f);

            // Wall → 적 위치 돌진 (Y축 고정 + 벽 보정)
            if (target != null)
            {
                Vector3 rawCharge = new Vector3(
                    target.transform.position.x,
                    _wallSentry.transform.position.y,
                    target.transform.position.z);
                Vector3 safeCharge = BattlePhysicsHelper.GetSafeTarget(
                    _wallSentry.transform.position, rawCharge, _sentryRadius, _wallLayer);

                yield return _wallSentry.transform
                    .DOMove(safeCharge, 0.2f).SetEase(Ease.InQuart).WaitForCompletion();

                int comboDamage = Mathf.RoundToInt(15 * _combo2DamageMultiplier);
                target.TakeDamage(comboDamage, HitType.Strike, _wallSentry.transform.position);

                _wallSentry.transform.DOShakePosition(0.3f, 0.4f, 20, 90f);
                _wallSentry.transform.DOPunchScale(Vector3.one * 0.3f, 0.3f, 5, 0.5f);
            }

            yield return new WaitForSeconds(0.5f);

            yield return StartCoroutine(ReturnFromCombo(_strikeSentry, strikePrePos));
            yield return StartCoroutine(ReturnFromCombo(_wallSentry, wallPrePos));

            SetInvincible(false, _strikeSentry, _wallSentry);
            _strikeSentry.StartFollowing();
            _wallSentry.StartFollowing();

            Debug.Log("[2콤보 B] 종료");
            OnComboFinished();
        }

        // ─────────────────────────────────────────
        //  2콤보 C: 사격 + 벽
        // ─────────────────────────────────────────

        private IEnumerator Combo_ShootWall(Enemy target)
        {
            _isComboPlaying = true;
            Debug.Log("<color=cyan>[2콤보 C] 사격 + 벽!</color>");

            Vector3 shootPrePos = _shootSentry.transform.position;
            Vector3 wallPrePos = _wallSentry.transform.position;

            SetInvincible(true, _shootSentry, _wallSentry);
            _shootSentry.StopFollowing();
            _wallSentry.StopFollowing();

            var (_, shootComboPos, wallComboPos) = CalcSafeComboPositions(target);

            _shootSentry.transform.DOMove(shootComboPos, 0.5f).SetEase(Ease.OutBack);
            yield return _wallSentry.transform
                .DOMove(wallComboPos, 0.5f).SetEase(Ease.OutBack).WaitForCompletion();

            yield return new WaitForSeconds(0.2f);

            if (target != null)
            {
                int comboDamage = Mathf.RoundToInt(20 * _combo2DamageMultiplier);
                for (int i = 0; i < 2; i++)
                {
                    _shootSentry.FireBullet(target.transform, comboDamage);
                    _shootSentry.transform.DOPunchPosition(Vector3.left * 0.1f, 0.08f, 5, 0.3f);
                    yield return new WaitForSeconds(0.18f);
                }
            }

            yield return new WaitForSeconds(0.15f);

            // Wall 하늘 위 → 적 위치 낙하 (벽 보정)
            if (target != null)
            {
                Vector3 skyPos = target.transform.position + Vector3.up * 5f;

                // 하늘 위치도 벽 보정 (극히 드물지만 천장이 있는 경우 대비)
                Vector3 safeSky = BattlePhysicsHelper.GetSafeTarget(
                    _wallSentry.transform.position, skyPos, _sentryRadius, _wallLayer);

                _wallSentry.transform.position = safeSky;
                _wallSentry.transform.localScale = Vector3.zero;
                _wallSentry.transform.DOScale(Vector3.one, 0.15f).SetEase(Ease.OutBack);

                yield return new WaitForSeconds(0.2f);

                // 낙하 — 적 위치로 벽 보정
                Vector3 rawDrop = target.transform.position;
                Vector3 safeDrop = BattlePhysicsHelper.GetSafeTarget(
                    _wallSentry.transform.position, rawDrop, _sentryRadius, _wallLayer);

                yield return _wallSentry.transform
                    .DOMove(safeDrop, 0.22f).SetEase(Ease.InQuart).WaitForCompletion();

                int wallDamage = Mathf.RoundToInt(15 * _combo2DamageMultiplier);
                target.TakeDamage(wallDamage, HitType.Strike, _wallSentry.transform.position);

                _wallSentry.transform.DOShakePosition(0.3f, 0.35f, 18, 90f);
                _wallSentry.transform.DOPunchScale(Vector3.one * 0.4f, 0.3f, 6, 0.5f);
            }

            yield return new WaitForSeconds(0.5f);

            yield return StartCoroutine(ReturnFromCombo(_shootSentry, shootPrePos));
            yield return StartCoroutine(ReturnFromCombo(_wallSentry, wallPrePos));

            SetInvincible(false, _shootSentry, _wallSentry);
            _shootSentry.StartFollowing();
            _wallSentry.StartFollowing();

            Debug.Log("[2콤보 C] 종료");
            OnComboFinished();
        }

        // ─────────────────────────────────────────
        //  3콤보: 전원 연계
        // ─────────────────────────────────────────

        private IEnumerator Combo_AllThree(Enemy target)
        {
            _isComboPlaying = true;
            Debug.Log("<color=yellow>[3콤보] 전원 연계 발동!</color>");

            Vector3 strikePrePos = _strikeSentry.transform.position;
            Vector3 shootPrePos = _shootSentry.transform.position;
            Vector3 wallPrePos = _wallSentry.transform.position;

            SetInvincible(true, _strikeSentry, _shootSentry, _wallSentry);
            _strikeSentry.StopFollowing();
            _shootSentry.StopFollowing();
            _wallSentry.StopFollowing();

            var (strikeComboPos, shootComboPos, wallComboPos) = CalcSafeComboPositions(target);

            _strikeSentry.transform.DOMove(strikeComboPos, 0.5f).SetEase(Ease.OutBack);
            _shootSentry.transform.DOMove(shootComboPos, 0.5f).SetEase(Ease.OutBack);
            yield return _wallSentry.transform
                .DOMove(wallComboPos, 0.5f).SetEase(Ease.OutBack).WaitForCompletion();

            yield return new WaitForSeconds(0.2f);

            int combo3Damage = Mathf.RoundToInt(20 * _combo3DamageMultiplier);

            // 1단계: Strike → 날리기 (Y축 고정 + 벽 보정)
            Vector3 rawStrikeHit = new Vector3(
                target.transform.position.x - 0.3f,
                _strikeSentry.transform.position.y,
                target.transform.position.z);
            Vector3 safeStrikeHit = BattlePhysicsHelper.GetSafeTarget(
                _strikeSentry.transform.position, rawStrikeHit, _sentryRadius, _wallLayer);

            yield return _strikeSentry.transform
                .DOMove(safeStrikeHit, 0.12f).SetEase(Ease.InExpo).WaitForCompletion();

            if (target != null)
                target.TakeDamage(combo3Damage / 4, HitType.Strike, _strikeSentry.transform.position);

            _strikeSentry.transform.DOShakePosition(0.2f, 0.3f, 15, 90f);
            yield return new WaitForSeconds(0.15f);

            // 2단계: Wall → 붙잡기 (Y축 고정 + 벽 보정)
            if (target != null)
            {
                Vector3 rawWallGrab = new Vector3(
                    target.transform.position.x + 0.5f,
                    _wallSentry.transform.position.y,
                    target.transform.position.z);
                Vector3 safeWallGrab = BattlePhysicsHelper.GetSafeTarget(
                    _wallSentry.transform.position, rawWallGrab, _sentryRadius, _wallLayer);

                yield return _wallSentry.transform
                    .DOMove(safeWallGrab, 0.18f).SetEase(Ease.InQuad).WaitForCompletion();

                _wallSentry.transform.DOShakePosition(0.2f, 0.25f, 12, 90f);
            }

            yield return new WaitForSeconds(0.3f);

            // 3단계: Shoot 연속 발사
            int shootDamage = Mathf.RoundToInt(20 * _combo3DamageMultiplier);
            for (int i = 0; i < _combo3BulletCount; i++)
            {
                if (target == null) break;
                _shootSentry.FireBullet(target.transform, shootDamage);
                _shootSentry.transform.DOPunchPosition(
                    Vector3.left * 0.08f, _combo3BulletInterval * 0.5f, 3, 0.2f);
                yield return new WaitForSeconds(_combo3BulletInterval);
            }

            yield return new WaitForSeconds(0.2f);

            // 4단계: 최종 밀치기 — AddForce 제거, DOMove + GetSafeTarget으로 교체
            // [버그 수정] 기존 AddForce(Kinematic에서 무시됨)를
            //             DOMove + CircleCast 보정으로 교체합니다.
            if (target != null)
            {
                target.TakeDamage(combo3Damage / 3, HitType.Strike, _wallSentry.transform.position);
                target.Stun(1.5f);

                // [Y축 고정] FlatDirection으로 X축 방향만 계산
                Vector3 pushDir = BattlePhysicsHelper.FlatDirection(
                    _wallSentry.transform.position, target.transform.position);
                Vector3 rawPush = target.transform.position + pushDir * _combo3FinalPushDist;
                Vector3 safePush = BattlePhysicsHelper.GetSafeTarget(
                    target.transform.position, rawPush, _enemyRadius, _wallLayer);

                target.transform.DOMove(safePush, _combo3FinalPushDuration).SetEase(Ease.OutExpo);
            }

            _wallSentry.transform.DOShakePosition(0.3f, 0.45f, 20, 90f);
            yield return new WaitForSeconds(0.5f);

            yield return StartCoroutine(ReturnFromCombo(_strikeSentry, strikePrePos));
            yield return StartCoroutine(ReturnFromCombo(_shootSentry, shootPrePos));
            yield return StartCoroutine(ReturnFromCombo(_wallSentry, wallPrePos));

            SetInvincible(false, _strikeSentry, _shootSentry, _wallSentry);
            _strikeSentry.StartFollowing();
            _shootSentry.StartFollowing();
            _wallSentry.StartFollowing();

            Debug.Log("[3콤보] 전원 연계 종료");
            OnComboFinished();
        }

        // ─────────────────────────────────────────
        //  헬퍼
        // ─────────────────────────────────────────

        private Enemy FindNearestEnemy()
        {
            Enemy[] enemies = FindObjectsByType<Enemy>(FindObjectsSortMode.None);
            Enemy nearest = null;
            float minDist = float.MaxValue;
            foreach (Enemy e in enemies)
            {
                if (e.IsDead) continue;
                float d = Vector3.Distance(transform.position, e.transform.position);
                if (d < minDist) { minDist = d; nearest = e; }
            }
            return nearest;
        }

        private void SetInvincible(bool on, params SentryBase[] sentries)
        {
            foreach (var s in sentries)
                if (s != null) s.SetInvincible(on);
        }

        private float GetRatio(float timer, float max) =>
            max > 0f ? Mathf.Clamp01(1f - timer / max) : 1f;

        private float GetBest2ComboCooldownRatio()
        {
            float best = 0f;
            if (_strikeSentry != null && !_strikeSentry.IsKnockedOut &&
                _shootSentry != null && !_shootSentry.IsKnockedOut)
                best = Mathf.Max(best, GetRatio(_comboACooldownTimer, _comboACooldown));
            if (_strikeSentry != null && !_strikeSentry.IsKnockedOut &&
                _wallSentry != null && !_wallSentry.IsKnockedOut)
                best = Mathf.Max(best, GetRatio(_comboBCooldownTimer, _comboBCooldown));
            if (_shootSentry != null && !_shootSentry.IsKnockedOut &&
                _wallSentry != null && !_wallSentry.IsKnockedOut)
                best = Mathf.Max(best, GetRatio(_comboCCooldownTimer, _comboCCooldown));
            return best;
        }
    }
}