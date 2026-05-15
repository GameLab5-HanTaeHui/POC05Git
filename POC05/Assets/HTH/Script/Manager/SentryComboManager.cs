using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;

namespace SENTRY
{
    /// <summary>
    /// 센트리 콤보 시스템을 관리하는 싱글턴 매니저.
    ///
    /// [Z값 오염 수정 목록]
    ///
    ///   1. CalcSafeComboPositions()
    ///      기존: rawShoot/rawWall의 Z에 offset.y가 들어가 Z 오염
    ///      수정: Z는 항상 0으로 고정. 오프셋은 X에만 적용
    ///
    ///   2. DOShakePosition — float 오버로드 → Z 흔들림 발생
    ///      수정: BattlePhysicsHelper.ShakeStrength(float) → Vector3(x, y, 0) 사용
    ///
    ///   3. DOPunchPosition — Z 포함 방향 벡터 사용
    ///      수정: BattlePhysicsHelper.PunchDir(dir) → Vector3(x, 0, 0) 사용
    ///
    ///   4. DOMove — OnUpdate ClampZ 미적용
    ///      수정: 모든 이동성 DOMove에 .OnUpdate(() => ClampZ(t)) 추가
    ///
    ///   5. ReturnFromCombo — DOMove OnUpdate ClampZ 미적용
    ///      수정: .OnUpdate(() => ClampZ(sentry.transform)) 추가
    ///
    ///   6. Combo_ShootWall Wall 낙하 skyPos — Vector3.up * 5f → Z 오염
    ///      수정: skyPos Z = 0 강제 고정
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
        //  Inspector — 물리 보정
        // ─────────────────────────────────────────

        [Header("물리 보정 — 벽 뚫림 / Z 오염 방지")]
        [Tooltip("TilemapCollider가 있는 레이어 (보통 Ground 또는 Wall)")]
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

        [Header("콤보 포지션 오프셋 (적 위치 기준 X축 오프셋만 사용)")]
        [Tooltip("X = 좌우 오프셋만 사용합니다. Y(Z 오염 원인)는 무시됩니다.")]
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
        [SerializeField] private float _combo3FinalPushDist = 5f;
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
            _currentComboGauge = Mathf.Min(
                _currentComboGauge + _comboGaugePerKill, _maxComboGauge);
            if (_currentComboGauge >= _maxComboGauge)
            {
                _currentComboGauge = 0f;
                TryEnqueueCombo();
            }
        }

        public void OnSentrySkillUsed()
        {
            _currentComboGauge = Mathf.Min(
                _currentComboGauge + _comboGaugePerSkill, _maxComboGauge);
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
            if (strikeAlive && shootAlive && _comboACooldownTimer <= 0f)
                candidates.Add(ComboType.StrikeShoot);
            if (strikeAlive && wallAlive && _comboBCooldownTimer <= 0f)
                candidates.Add(ComboType.StrikeWall);
            if (shootAlive && wallAlive && _comboCCooldownTimer <= 0f)
                candidates.Add(ComboType.ShootWall);

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
        //  콤보 집결 위치 계산
        // ─────────────────────────────────────────

        /// <summary>
        /// 각 센트리의 콤보 집결 위치를 계산합니다.
        ///
        /// [Z 오염 수정]
        /// 기존: Z에 offset.y 값이 들어가 Z 오염 발생
        ///   rawShoot = new Vector3(x + offset.x, y, z + offset.y)  ← z에 offset.y 오염
        /// 수정: X에만 오프셋을 적용, Z는 항상 0으로 강제 고정
        ///   rawShoot = new Vector3(x + offset.x, y, 0f)            ← Z=0 고정
        /// </summary>
        private (Vector3 strike, Vector3 shoot, Vector3 wall) CalcSafeComboPositions(Enemy target)
        {
            Vector3 basePos = target.transform.position;

            // [Z 수정] Z는 항상 0 고정. 오프셋은 X에만 적용
            Vector3 rawStrike = new Vector3(basePos.x + _strikeComboOffset.x, basePos.y, 0f);
            Vector3 rawShoot = new Vector3(basePos.x + _shootComboOffset.x, basePos.y, 0f);
            Vector3 rawWall = new Vector3(basePos.x + _wallComboOffset.x, basePos.y, 0f);

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
        //  복귀 DOMove (Z 고정 + 벽 충돌 보정)
        // ─────────────────────────────────────────

        /// <summary>
        /// 콤보 종료 후 센트리를 원래 위치로 복귀시킵니다.
        ///
        /// [Z 수정] OnUpdate에서 ClampZ를 호출해 복귀 이동 중 Z 오염을 차단합니다.
        /// </summary>
        private IEnumerator ReturnFromCombo(
            SentryBase sentry, Vector3 prePos, float duration = 0.5f)
        {
            if (sentry == null) yield break;

            // prePos의 Z도 0으로 강제 고정
            prePos.z = 0f;

            Vector3 safeReturn = BattlePhysicsHelper.GetSafeTarget(
                sentry.transform.position, prePos, _sentryRadius, _wallLayer);

            Transform t = sentry.transform;
            yield return t.DOMove(safeReturn, duration)
                .SetEase(Ease.OutElastic)
                .OnUpdate(() => BattlePhysicsHelper.ClampZ(t))  // [Z 수정] 이동 중 Z 실시간 고정
                .WaitForCompletion();

            // 복귀 완료 후 Z 최종 보정
            BattlePhysicsHelper.ClampZ(t);
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

            // ── 집결 이동 ──
            Transform st = _strikeSentry.transform;
            Transform sh = _shootSentry.transform;

            st.DOMove(strikeComboPos, 0.5f).SetEase(Ease.OutBack)
                .OnUpdate(() => BattlePhysicsHelper.ClampZ(st));

            yield return sh.DOMove(shootComboPos, 0.5f).SetEase(Ease.OutBack)
                .OnUpdate(() => BattlePhysicsHelper.ClampZ(sh))
                .WaitForCompletion();

            BattlePhysicsHelper.ClampZ(st);
            BattlePhysicsHelper.ClampZ(sh);

            yield return new WaitForSeconds(0.2f);

            // ── Strike → Shoot 총구 방향 접근 ──
            // [Z 수정] rawImpact.z = 0 강제 고정
            Vector3 rawImpact = new Vector3(
                _shootSentry.transform.position.x + 0.4f,
                _strikeSentry.transform.position.y,
                0f);
            Vector3 safeImpact = BattlePhysicsHelper.GetSafeTarget(
                st.position, rawImpact, _sentryRadius, _wallLayer);

            yield return st.DOMove(safeImpact, 0.1f).SetEase(Ease.InExpo)
                .OnUpdate(() => BattlePhysicsHelper.ClampZ(st))
                .WaitForCompletion();

            BattlePhysicsHelper.ClampZ(st);

            // [Z 수정] ShakeStrength — Z 축 흔들림 차단
            st.DOShakePosition(0.15f, BattlePhysicsHelper.ShakeStrength(0.25f), 15, 90f);
            sh.DOShakePosition(0.2f, BattlePhysicsHelper.ShakeStrength(0.2f), 12, 90f);

            yield return new WaitForSeconds(0.1f);

            if (target != null && _piercingBulletPrefab != null)
            {
                int comboDamage = Mathf.RoundToInt(20 * _combo2DamageMultiplier);
                GameObject bulletObj = Instantiate(
                    _piercingBulletPrefab, sh.position, Quaternion.identity);
                SentryPiercingBullet pb = bulletObj.GetComponent<SentryPiercingBullet>();
                if (pb != null) { pb.damage = comboDamage; pb.Setup(target.transform); }
            }

            // [Z 수정] PunchDir — Z 성분 차단
            sh.DOPunchPosition(
                BattlePhysicsHelper.PunchDir(Vector3.left * 0.35f), 0.15f, 8, 0.5f);
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

            Transform st = _strikeSentry.transform;
            Transform wl = _wallSentry.transform;

            st.DOMove(strikeComboPos, 0.5f).SetEase(Ease.OutBack)
                .OnUpdate(() => BattlePhysicsHelper.ClampZ(st));

            yield return wl.DOMove(wallComboPos, 0.5f).SetEase(Ease.OutBack)
                .OnUpdate(() => BattlePhysicsHelper.ClampZ(wl))
                .WaitForCompletion();

            BattlePhysicsHelper.ClampZ(st);
            BattlePhysicsHelper.ClampZ(wl);

            yield return new WaitForSeconds(0.2f);

            // ── Strike → Wall 옆 타격 ──
            // [Z 수정] z = 0 강제
            Vector3 rawHit = new Vector3(
                wl.position.x - 0.3f,
                st.position.y,
                0f);
            Vector3 safeHit = BattlePhysicsHelper.GetSafeTarget(
                st.position, rawHit, _sentryRadius, _wallLayer);

            yield return st.DOMove(safeHit, 0.1f).SetEase(Ease.InExpo)
                .OnUpdate(() => BattlePhysicsHelper.ClampZ(st))
                .WaitForCompletion();

            BattlePhysicsHelper.ClampZ(st);

            // [Z 수정] ShakeStrength
            st.DOShakePosition(0.2f, BattlePhysicsHelper.ShakeStrength(0.3f), 15, 90f);
            yield return new WaitForSeconds(0.08f);

            // ── Wall → 적 위치 돌진 ──
            if (target != null)
            {
                // [Z 수정] z = 0 강제
                Vector3 rawCharge = new Vector3(
                    target.transform.position.x,
                    wl.position.y,
                    0f);
                Vector3 safeCharge = BattlePhysicsHelper.GetSafeTarget(
                    wl.position, rawCharge, _sentryRadius, _wallLayer);

                yield return wl.DOMove(safeCharge, 0.2f).SetEase(Ease.InQuart)
                    .OnUpdate(() => BattlePhysicsHelper.ClampZ(wl))
                    .WaitForCompletion();

                BattlePhysicsHelper.ClampZ(wl);

                int comboDamage = Mathf.RoundToInt(15 * _combo2DamageMultiplier);
                target.TakeDamage(comboDamage, HitType.Strike, wl.position);

                // [Z 수정] ShakeStrength
                wl.DOShakePosition(0.3f, BattlePhysicsHelper.ShakeStrength(0.4f), 20, 90f);
                wl.DOPunchScale(Vector3.one * 0.3f, 0.3f, 5, 0.5f);
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

            Transform sh = _shootSentry.transform;
            Transform wl = _wallSentry.transform;

            sh.DOMove(shootComboPos, 0.5f).SetEase(Ease.OutBack)
                .OnUpdate(() => BattlePhysicsHelper.ClampZ(sh));

            yield return wl.DOMove(wallComboPos, 0.5f).SetEase(Ease.OutBack)
                .OnUpdate(() => BattlePhysicsHelper.ClampZ(wl))
                .WaitForCompletion();

            BattlePhysicsHelper.ClampZ(sh);
            BattlePhysicsHelper.ClampZ(wl);

            yield return new WaitForSeconds(0.2f);

            if (target != null)
            {
                int comboDamage = Mathf.RoundToInt(20 * _combo2DamageMultiplier);
                for (int i = 0; i < 2; i++)
                {
                    _shootSentry.FireBullet(target.transform, comboDamage);
                    // [Z 수정] PunchDir
                    sh.DOPunchPosition(
                        BattlePhysicsHelper.PunchDir(Vector3.left * 0.1f), 0.08f, 5, 0.3f);
                    yield return new WaitForSeconds(0.18f);
                }
            }

            yield return new WaitForSeconds(0.15f);

            // ── Wall 낙하 ──
            if (target != null)
            {
                // [Z 수정] skyPos.z = 0 강제. Vector3.up은 Y만 변경하므로 Z는 0 유지
                Vector3 skyPos = new Vector3(
                    target.transform.position.x,
                    target.transform.position.y + 5f,
                    0f);    // ← Z 명시적 0 고정

                Vector3 safeSky = BattlePhysicsHelper.GetSafeTarget(
                    wl.position, skyPos, _sentryRadius, _wallLayer);

                wl.position = safeSky;
                wl.localScale = Vector3.zero;
                wl.DOScale(Vector3.one, 0.15f).SetEase(Ease.OutBack);

                yield return new WaitForSeconds(0.2f);

                // [Z 수정] rawDrop.z = 0 강제
                Vector3 rawDrop = new Vector3(
                    target.transform.position.x,
                    target.transform.position.y,
                    0f);
                Vector3 safeDrop = BattlePhysicsHelper.GetSafeTarget(
                    wl.position, rawDrop, _sentryRadius, _wallLayer);

                yield return wl.DOMove(safeDrop, 0.22f).SetEase(Ease.InQuart)
                    .OnUpdate(() => BattlePhysicsHelper.ClampZ(wl))
                    .WaitForCompletion();

                BattlePhysicsHelper.ClampZ(wl);

                int wallDamage = Mathf.RoundToInt(15 * _combo2DamageMultiplier);
                target.TakeDamage(wallDamage, HitType.Strike, wl.position);

                // [Z 수정] ShakeStrength
                wl.DOShakePosition(0.3f, BattlePhysicsHelper.ShakeStrength(0.35f), 18, 90f);
                wl.DOPunchScale(Vector3.one * 0.4f, 0.3f, 6, 0.5f);
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

            Transform st = _strikeSentry.transform;
            Transform sh = _shootSentry.transform;
            Transform wl = _wallSentry.transform;

            st.DOMove(strikeComboPos, 0.5f).SetEase(Ease.OutBack)
                .OnUpdate(() => BattlePhysicsHelper.ClampZ(st));
            sh.DOMove(shootComboPos, 0.5f).SetEase(Ease.OutBack)
                .OnUpdate(() => BattlePhysicsHelper.ClampZ(sh));

            yield return wl.DOMove(wallComboPos, 0.5f).SetEase(Ease.OutBack)
                .OnUpdate(() => BattlePhysicsHelper.ClampZ(wl))
                .WaitForCompletion();

            BattlePhysicsHelper.ClampZ(st);
            BattlePhysicsHelper.ClampZ(sh);
            BattlePhysicsHelper.ClampZ(wl);

            yield return new WaitForSeconds(0.2f);

            int combo3Damage = Mathf.RoundToInt(20 * _combo3DamageMultiplier);

            // ── 1단계: Strike → 날리기 ──
            // [Z 수정] z = 0 강제
            Vector3 rawStrikeHit = new Vector3(
                target.transform.position.x - 0.3f,
                st.position.y,
                0f);
            Vector3 safeStrikeHit = BattlePhysicsHelper.GetSafeTarget(
                st.position, rawStrikeHit, _sentryRadius, _wallLayer);

            yield return st.DOMove(safeStrikeHit, 0.12f).SetEase(Ease.InExpo)
                .OnUpdate(() => BattlePhysicsHelper.ClampZ(st))
                .WaitForCompletion();

            BattlePhysicsHelper.ClampZ(st);

            if (target != null)
                target.TakeDamage(
                    combo3Damage / 4, HitType.Strike, st.position);

            // [Z 수정] ShakeStrength
            st.DOShakePosition(0.2f, BattlePhysicsHelper.ShakeStrength(0.3f), 15, 90f);
            yield return new WaitForSeconds(0.15f);

            // ── 2단계: Wall → 붙잡기 ──
            if (target != null)
            {
                // [Z 수정] z = 0 강제
                Vector3 rawWallGrab = new Vector3(
                    target.transform.position.x + 0.5f,
                    wl.position.y,
                    0f);
                Vector3 safeWallGrab = BattlePhysicsHelper.GetSafeTarget(
                    wl.position, rawWallGrab, _sentryRadius, _wallLayer);

                yield return wl.DOMove(safeWallGrab, 0.18f).SetEase(Ease.InQuad)
                    .OnUpdate(() => BattlePhysicsHelper.ClampZ(wl))
                    .WaitForCompletion();

                BattlePhysicsHelper.ClampZ(wl);

                // [Z 수정] ShakeStrength
                wl.DOShakePosition(0.2f, BattlePhysicsHelper.ShakeStrength(0.25f), 12, 90f);
            }

            yield return new WaitForSeconds(0.3f);

            // ── 3단계: Shoot 연속 발사 ──
            int shootDamage = Mathf.RoundToInt(20 * _combo3DamageMultiplier);
            for (int i = 0; i < _combo3BulletCount; i++)
            {
                if (target == null) break;
                _shootSentry.FireBullet(target.transform, shootDamage);
                // [Z 수정] PunchDir
                sh.DOPunchPosition(
                    BattlePhysicsHelper.PunchDir(Vector3.left * 0.08f),
                    _combo3BulletInterval * 0.5f, 3, 0.2f);
                yield return new WaitForSeconds(_combo3BulletInterval);
            }

            yield return new WaitForSeconds(0.2f);

            // ── 4단계: 최종 밀치기 ──
            if (target != null)
            {
                target.TakeDamage(
                    combo3Damage / 3, HitType.Strike, wl.position);
                target.Stun(1.5f);

                // [Z 수정] FlatDirection(X방향만) + z=0 보장된 GetSafeTarget
                Vector3 pushDir = BattlePhysicsHelper.FlatDirection(
                    wl.position, target.transform.position);
                Vector3 rawPush = new Vector3(
                    target.transform.position.x + pushDir.x * _combo3FinalPushDist,
                    target.transform.position.y,
                    0f);
                Vector3 safePush = BattlePhysicsHelper.GetSafeTarget(
                    target.transform.position, rawPush, _enemyRadius, _wallLayer);

                Transform et = target.transform;
                et.DOMove(safePush, _combo3FinalPushDuration).SetEase(Ease.OutExpo)
                    .OnUpdate(() => BattlePhysicsHelper.ClampZ(et));
            }

            // [Z 수정] ShakeStrength
            wl.DOShakePosition(0.3f, BattlePhysicsHelper.ShakeStrength(0.45f), 20, 90f);
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
            bool strikeAlive = _strikeSentry != null && !_strikeSentry.IsKnockedOut;
            bool shootAlive = _shootSentry != null && !_shootSentry.IsKnockedOut;
            bool wallAlive = _wallSentry != null && !_wallSentry.IsKnockedOut;

            if (strikeAlive && shootAlive)
                best = Mathf.Max(best, GetRatio(_comboACooldownTimer, _comboACooldown));
            if (strikeAlive && wallAlive)
                best = Mathf.Max(best, GetRatio(_comboBCooldownTimer, _comboBCooldown));
            if (shootAlive && wallAlive)
                best = Mathf.Max(best, GetRatio(_comboCCooldownTimer, _comboCCooldown));

            return best;
        }
    }
}