using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;

namespace SENTRY
{
    /// <summary>
    /// 센트리 간 콤보 연계 시스템을 관리하는 싱글턴 매니저.
    /// (구 ComboManager → SentryComboManager 이름 변경)
    ///
    /// [콤보 큐 시스템]
    /// 게이지가 찰 때마다 발동 가능한 콤보를 Queue에 추가합니다.
    /// 재생 중이면 대기, 끝나면 자동으로 다음 콤보를 꺼내 실행합니다.
    ///
    /// [큐 추가 우선순위 — 게이지 만참 시점]
    ///   1순위: 3기 생존 + 3콤보 쿨타임 OK → AllThree
    ///   2순위: 3콤보 불가일 때 → 가능한 2콤보 조합 목록 수집 후 랜덤 1개 선택
    ///          가능한 조합: 쿨타임 OK + 해당 센트리 2기 모두 생존
    ///          A (타격+사격) / B (타격+벽) / C (사격+벽)
    ///   발동 불가 시 큐 미추가 (게이지만 소비)
    ///
    /// [쿨타임 구조 — 4개 개별]
    ///   _combo3CooldownTimer : 3콤보 전용 공유 1개
    ///   _comboACooldownTimer : 2콤보A (타격+사격) 개별
    ///   _comboBCooldownTimer : 2콤보B (타격+벽) 개별
    ///   _comboCCooldownTimer : 2콤보C (사격+벽) 개별
    ///
    /// [UI 노출]
    ///   Combo2CooldownRatio : 가능한 2콤보 중 쿨타임이 가장 짧은 것의 비율 (UI 1개)
    ///   Combo3CooldownRatio : 3콤보 쿨타임 비율 (UI 1개)
    ///
    /// [히어라키 위치]
    /// --- Managers ---
    ///   └── SentryComboManager (이 스크립트)
    /// </summary>
    public class SentryComboManager : MonoBehaviour
    {
        // ─────────────────────────────────────────
        //  싱글턴
        // ─────────────────────────────────────────

        /// <summary>씬 어디서든 SentryComboManager.Instance로 접근합니다.</summary>
        public static SentryComboManager Instance { get; private set; }

        // ─────────────────────────────────────────
        //  Inspector — 센트리 참조
        // ─────────────────────────────────────────

        [Header("센트리 참조")]
        [Tooltip("타격 센트리")]
        [SerializeField] private StrikeSentry _strikeSentry;

        [Tooltip("사격 센트리")]
        [SerializeField] private ShootSentry _shootSentry;

        [Tooltip("벽 센트리")]
        [SerializeField] private WallSentry _wallSentry;

        // ─────────────────────────────────────────
        //  Inspector — 콤보 게이지 설정
        // ─────────────────────────────────────────

        [Header("콤보 게이지 설정")]
        [Tooltip("콤보 게이지 최대치")]
        [SerializeField] private float _maxComboGauge = 100f;

        [Tooltip("적 처치 1회당 콤보 게이지 충전량")]
        [SerializeField] private float _comboGaugePerKill = 20f;

        [Tooltip("센트리 스킬 발동 1회당 콤보 게이지 충전량")]
        [SerializeField] private float _comboGaugePerSkill = 15f;

        // ─────────────────────────────────────────
        //  Inspector — 콤보 쿨타임
        // ─────────────────────────────────────────

        [Header("콤보 쿨타임 (초)")]
        [Tooltip("3센트리 콤보 전용 쿨타임 (공유 1개)")]
        [SerializeField] private float _combo3Cooldown = 60f;

        [Tooltip("2콤보A (타격+사격) 개별 쿨타임")]
        [SerializeField] private float _comboACooldown = 25f;

        [Tooltip("2콤보B (타격+벽) 개별 쿨타임")]
        [SerializeField] private float _comboBCooldown = 25f;

        [Tooltip("2콤보C (사격+벽) 개별 쿨타임")]
        [SerializeField] private float _comboCCooldown = 25f;

        // ─────────────────────────────────────────
        //  Inspector — 콤보 포지션 오프셋
        // ─────────────────────────────────────────

        [Header("콤보 포지션 오프셋 (쿼터뷰, 적 위치 기준 상대 좌표)")]
        [Tooltip("타격 센트리 오프셋. 적 바로 앞(근접).")]
        [SerializeField] private Vector2 _strikeComboOffset = new Vector2(-1.5f, 0f);

        [Tooltip("사격 센트리 오프셋. 적 후방(원거리).")]
        [SerializeField] private Vector2 _shootComboOffset = new Vector2(-4f, 0f);

        [Tooltip("벽 센트리 오프셋. 적 옆(압착/낙하).")]
        [SerializeField] private Vector2 _wallComboOffset = new Vector2(1f, 0f);

        // ─────────────────────────────────────────
        //  Inspector — 3콤보 전용 설정
        // ─────────────────────────────────────────

        [Header("3콤보 전용 설정")]
        [Tooltip("3콤보 사격 단계 연속 발사 수")]
        [SerializeField] private int _combo3BulletCount = 10;

        [Tooltip("3콤보 탄환 발사 간격 (초)")]
        [SerializeField] private float _combo3BulletInterval = 0.1f;

        [Tooltip("3콤보 데미지 배율")]
        [SerializeField] private float _combo3DamageMultiplier = 4f;

        // ─────────────────────────────────────────
        //  Inspector — 2콤보 설정
        // ─────────────────────────────────────────

        [Header("2콤보 설정")]
        [Tooltip("2콤보 데미지 배율")]
        [SerializeField] private float _combo2DamageMultiplier = 2.5f;

        [Tooltip("타격+사격 콤보용 관통탄 프리팹")]
        [SerializeField] private GameObject _piercingBulletPrefab;

        // ─────────────────────────────────────────
        //  콤보 타입 열거형
        // ─────────────────────────────────────────

        /// <summary>
        /// 큐에 저장되어 실행 순서를 보장하는 콤보 종류 열거형.
        /// </summary>
        private enum ComboType
        {
            /// <summary>3센트리 전원 연계</summary>
            AllThree,
            /// <summary>2콤보A: 타격 + 사격</summary>
            StrikeShoot,
            /// <summary>2콤보B: 타격 + 벽</summary>
            StrikeWall,
            /// <summary>2콤보C: 사격 + 벽</summary>
            ShootWall
        }

        // ─────────────────────────────────────────
        //  내부 상태 변수
        // ─────────────────────────────────────────

        /// <summary>현재 콤보 게이지</summary>
        private float _currentComboGauge = 0f;

        /// <summary>콤보 연출 재생 중 여부</summary>
        private bool _isComboPlaying = false;

        /// <summary>배틀 진행 중 여부</summary>
        private bool _isInBattle = false;

        /// <summary>3콤보 쿨타임 타이머</summary>
        private float _combo3CooldownTimer = 0f;

        /// <summary>2콤보A 쿨타임 타이머</summary>
        private float _comboACooldownTimer = 0f;

        /// <summary>2콤보B 쿨타임 타이머</summary>
        private float _comboBCooldownTimer = 0f;

        /// <summary>2콤보C 쿨타임 타이머</summary>
        private float _comboCCooldownTimer = 0f;

        /// <summary>발동 대기 중인 콤보 타입 큐</summary>
        private Queue<ComboType> _comboQueue = new Queue<ComboType>();

        // ─────────────────────────────────────────
        //  외부 공개 프로퍼티
        // ─────────────────────────────────────────

        /// <summary>현재 콤보 게이지</summary>
        public float ComboGauge => _currentComboGauge;

        /// <summary>콤보 게이지 최대치</summary>
        public float MaxComboGauge => _maxComboGauge;

        /// <summary>
        /// 3콤보 쿨타임 비율 (0=쿨중 / 1=사용가능).
        /// UI: 3콤보 전용 쿨타임 바 1개에 연결합니다.
        /// </summary>
        public float Combo3CooldownRatio =>
            _combo3Cooldown > 0f
                ? Mathf.Clamp01(1f - (_combo3CooldownTimer / _combo3Cooldown))
                : 1f;

        /// <summary>
        /// 2콤보 대표 쿨타임 비율 (0=쿨중 / 1=사용가능).
        /// A/B/C 중 현재 생존 조합에서 쿨타임이 가장 많이 찬(Ratio가 큰) 것을 대표값으로 반환합니다.
        /// UI: 2콤보 통합 쿨타임 바 1개에 연결합니다.
        /// </summary>
        public float Combo2CooldownRatio
        {
            get
            {
                bool strikeAlive = _strikeSentry != null && !_strikeSentry.IsKnockedOut;
                bool shootAlive = _shootSentry != null && !_shootSentry.IsKnockedOut;
                bool wallAlive = _wallSentry != null && !_wallSentry.IsKnockedOut;

                float best = 0f;

                // 생존 조합에 해당하는 쿨타임 비율만 비교
                if (strikeAlive && shootAlive)
                    best = Mathf.Max(best, GetRatio(_comboACooldownTimer, _comboACooldown));
                if (strikeAlive && wallAlive)
                    best = Mathf.Max(best, GetRatio(_comboBCooldownTimer, _comboBCooldown));
                if (shootAlive && wallAlive)
                    best = Mathf.Max(best, GetRatio(_comboCCooldownTimer, _comboCCooldown));

                return best;
            }
        }

        /// <summary>현재 대기 중인 콤보 수 (UI 대기 표시용)</summary>
        public int QueuedComboCount => _comboQueue.Count;

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
            // 4개 쿨타임 타이머 감소
            if (_combo3CooldownTimer > 0f) _combo3CooldownTimer -= Time.deltaTime;
            if (_comboACooldownTimer > 0f) _comboACooldownTimer -= Time.deltaTime;
            if (_comboBCooldownTimer > 0f) _comboBCooldownTimer -= Time.deltaTime;
            if (_comboCCooldownTimer > 0f) _comboCCooldownTimer -= Time.deltaTime;
        }

        // ─────────────────────────────────────────
        //  배틀 상태
        // ─────────────────────────────────────────

        /// <summary>
        /// 배틀 시작 시 BattleManager에서 호출합니다.
        /// 게이지·큐·쿨타임을 초기화합니다.
        /// </summary>
        public void OnBattleStart()
        {
            _isInBattle = true;
            _currentComboGauge = 0f;
            _comboQueue.Clear();
            _combo3CooldownTimer = 0f;
            _comboACooldownTimer = 0f;
            _comboBCooldownTimer = 0f;
            _comboCCooldownTimer = 0f;

            Debug.Log("[SentryComboManager] 배틀 시작 — 초기화 완료");
        }

        /// <summary>
        /// 배틀 종료 시 BattleManager에서 호출합니다.
        /// </summary>
        public void OnBattleEnd()
        {
            _isInBattle = false;
            _comboQueue.Clear();
        }

        // ─────────────────────────────────────────
        //  게이지 충전
        // ─────────────────────────────────────────

        /// <summary>
        /// 적 처치 시 BattleManager.OnEnemyDied()에서 호출합니다.
        /// </summary>
        public void OnEnemyKilled()
        {
            if (!_isInBattle) return;
            ChargeGauge(_comboGaugePerKill);
        }

        /// <summary>
        /// 센트리 스킬 발동 시 각 SentryType에서 호출합니다.
        /// </summary>
        public void OnSentrySkillUsed()
        {
            if (!_isInBattle) return;
            ChargeGauge(_comboGaugePerSkill);
        }

        /// <summary>
        /// 게이지를 충전하고 최대치 도달 시 TryEnqueueCombo()를 호출합니다.
        /// </summary>
        /// <param name="amount">충전량</param>
        private void ChargeGauge(float amount)
        {
            _currentComboGauge = Mathf.Min(_currentComboGauge + amount, _maxComboGauge);

            if (_currentComboGauge >= _maxComboGauge)
            {
                _currentComboGauge = 0f;
                TryEnqueueCombo();
            }
        }

        // ─────────────────────────────────────────
        //  콤보 큐 추가 판단
        // ─────────────────────────────────────────

        /// <summary>
        /// 게이지가 찰 때마다 호출됩니다.
        /// 생존 조합과 쿨타임을 확인해 발동 가능한 콤보를 큐에 추가합니다.
        ///
        /// [우선순위]
        ///   1순위: 3기 생존 + 3콤보 쿨타임 OK → AllThree
        ///   2순위: 3콤보 불가 시 → 가능한 2콤보 조합 수집 후 랜덤 1개 선택
        ///          (쿨타임 OK + 해당 2기 생존 조합만 후보)
        ///   발동 불가 시 큐 미추가
        /// </summary>
        private void TryEnqueueCombo()
        {
            bool strikeAlive = _strikeSentry != null && !_strikeSentry.IsKnockedOut;
            bool shootAlive = _shootSentry != null && !_shootSentry.IsKnockedOut;
            bool wallAlive = _wallSentry != null && !_wallSentry.IsKnockedOut;

            // ── 1순위: 3콤보 ──
            if (strikeAlive && shootAlive && wallAlive && _combo3CooldownTimer <= 0f)
            {
                _comboQueue.Enqueue(ComboType.AllThree);
                Debug.Log($"[SentryComboManager] 큐 추가: AllThree (대기 {_comboQueue.Count}개)");
                TryDequeueAndPlay();
                return;
            }

            // ── 2순위: 2콤보 — 가능한 조합 수집 후 랜덤 선택 ──
            List<ComboType> candidates = new List<ComboType>();

            if (strikeAlive && shootAlive && _comboACooldownTimer <= 0f)
                candidates.Add(ComboType.StrikeShoot);
            if (strikeAlive && wallAlive && _comboBCooldownTimer <= 0f)
                candidates.Add(ComboType.StrikeWall);
            if (shootAlive && wallAlive && _comboCCooldownTimer <= 0f)
                candidates.Add(ComboType.ShootWall);

            if (candidates.Count > 0)
            {
                // 후보 중 랜덤 1개 선택
                ComboType chosen = candidates[Random.Range(0, candidates.Count)];
                _comboQueue.Enqueue(chosen);
                Debug.Log($"[SentryComboManager] 큐 추가: {chosen} " +
                          $"(후보 {candidates.Count}개 중 랜덤 선택 / 대기 {_comboQueue.Count}개)");
                TryDequeueAndPlay();
                return;
            }

            Debug.Log("[SentryComboManager] 발동 가능한 콤보 없음 — 게이지만 소비");
        }

        // ─────────────────────────────────────────
        //  콤보 큐 실행
        // ─────────────────────────────────────────

        /// <summary>
        /// 큐에서 콤보를 꺼내 실행합니다.
        /// 콤보 종료 콜백(OnComboFinished)에서도 호출되어 연속 실행을 보장합니다.
        /// </summary>
        private void TryDequeueAndPlay()
        {
            if (_isComboPlaying || _comboQueue.Count == 0) return;

            Enemy target = FindNearestEnemy();
            if (target == null)
            {
                _comboQueue.Clear();
                Debug.Log("[SentryComboManager] 타겟 없음 — 큐 비움");
                return;
            }

            ComboType next = _comboQueue.Dequeue();

            Debug.Log($"<color=cyan>[SentryComboManager] 실행: {next}" +
                      $" / 남은 대기 {_comboQueue.Count}개</color>");

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

        /// <summary>
        /// 모든 콤보 코루틴 종료 시 호출합니다.
        /// 큐에 남은 콤보가 있으면 자동으로 다음 콤보를 실행합니다.
        /// </summary>
        private void OnComboFinished()
        {
            _isComboPlaying = false;

            if (_comboQueue.Count > 0)
            {
                Debug.Log($"[SentryComboManager] 다음 콤보 실행 준비 (대기 {_comboQueue.Count}개)");
                TryDequeueAndPlay();
            }
        }

        // ─────────────────────────────────────────
        //  콤보 포지션 동적 계산
        // ─────────────────────────────────────────

        /// <summary>
        /// 타겟 적의 현재 위치를 기준으로 각 센트리의 콤보 집결 위치를 계산합니다.
        /// 콤보 발동 시점에 매번 새로 계산합니다.
        /// </summary>
        /// <param name="target">콤보 대상 적</param>
        /// <returns>Strike / Shoot / Wall 콤보 위치 (월드 좌표)</returns>
        private (Vector3 strike, Vector3 shoot, Vector3 wall) CalcComboPositions(Enemy target)
        {
            Vector3 basePos = target.transform.position;
            return (
                basePos + (Vector3)_strikeComboOffset,
                basePos + (Vector3)_shootComboOffset,
                basePos + (Vector3)_wallComboOffset
            );
        }

        // ─────────────────────────────────────────
        //  헬퍼
        // ─────────────────────────────────────────

        /// <summary>씬 내 생존 중인 가장 가까운 적을 반환합니다.</summary>
        private Enemy FindNearestEnemy()
        {
            Enemy[] enemies = FindObjectsByType<Enemy>(FindObjectsSortMode.None);
            if (enemies.Length == 0) return null;

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

        /// <summary>지정 센트리들의 무적 상태를 일괄 설정합니다.</summary>
        private void SetInvincible(bool on, params SentryBase[] sentries)
        {
            foreach (var s in sentries)
                if (s != null) s.SetInvincible(on);
        }

        /// <summary>콤보 종료 후 센트리를 콤보 직전 위치로 복귀시킵니다.</summary>
        private IEnumerator ReturnFromCombo(
            SentryBase sentry, Vector3 preComboPosition, float duration = 0.5f)
        {
            if (sentry == null) yield break;
            yield return sentry.transform
                .DOMove(preComboPosition, duration)
                .SetEase(Ease.OutElastic)
                .WaitForCompletion();
        }

        /// <summary>쿨타임 타이머와 최대치로 비율(0~1)을 계산합니다.</summary>
        private float GetRatio(float timer, float max) =>
            max > 0f ? Mathf.Clamp01(1f - (timer / max)) : 1f;

        // ─────────────────────────────────────────
        //  2콤보 A: 타격 + 사격
        // ─────────────────────────────────────────

        /// <summary>
        /// [2콤보 A] 타격 + 사격.
        /// 타격 센트리가 사격 센트리 총구를 쳐 관통탄을 발사합니다.
        /// </summary>
        private IEnumerator Combo_StrikeShoot(Enemy target)
        {
            _isComboPlaying = true;
            Debug.Log("<color=cyan>[2콤보 A] 타격 + 사격!</color>");

            Vector3 strikePrePos = _strikeSentry.transform.position;
            Vector3 shootPrePos = _shootSentry.transform.position;

            SetInvincible(true, _strikeSentry, _shootSentry);
            _strikeSentry.StopFollowing();
            _shootSentry.StopFollowing();

            var (strikeComboPos, shootComboPos, _) = CalcComboPositions(target);

            _strikeSentry.transform.DOMove(strikeComboPos, 0.5f).SetEase(Ease.OutBack);
            yield return _shootSentry.transform
                .DOMove(shootComboPos, 0.5f).SetEase(Ease.OutBack).WaitForCompletion();

            yield return new WaitForSeconds(0.2f);

            Vector3 impactPoint = _shootSentry.transform.position + Vector3.right * 0.4f;
            yield return _strikeSentry.transform
                .DOMove(impactPoint, 0.1f).SetEase(Ease.InExpo).WaitForCompletion();

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

        /// <summary>
        /// [2콤보 B] 타격 + 벽.
        /// 타격 센트리가 벽 센트리를 쳐내 고속 돌진 압착 공격을 합니다.
        /// </summary>
        private IEnumerator Combo_StrikeWall(Enemy target)
        {
            _isComboPlaying = true;
            Debug.Log("<color=cyan>[2콤보 B] 타격 + 벽!</color>");

            Vector3 strikePrePos = _strikeSentry.transform.position;
            Vector3 wallPrePos = _wallSentry.transform.position;

            SetInvincible(true, _strikeSentry, _wallSentry);
            _strikeSentry.StopFollowing();
            _wallSentry.StopFollowing();

            var (strikeComboPos, _, wallComboPos) = CalcComboPositions(target);

            _strikeSentry.transform.DOMove(strikeComboPos, 0.5f).SetEase(Ease.OutBack);
            yield return _wallSentry.transform
                .DOMove(wallComboPos, 0.5f).SetEase(Ease.OutBack).WaitForCompletion();

            yield return new WaitForSeconds(0.2f);

            Vector3 hitPoint = _wallSentry.transform.position + Vector3.left * 0.3f;
            yield return _strikeSentry.transform
                .DOMove(hitPoint, 0.1f).SetEase(Ease.InExpo).WaitForCompletion();

            _strikeSentry.transform.DOShakePosition(0.2f, 0.3f, 15, 90f);
            yield return new WaitForSeconds(0.08f);

            if (target != null)
            {
                yield return _wallSentry.transform
                    .DOMove(target.transform.position, 0.2f)
                    .SetEase(Ease.InQuart).WaitForCompletion();

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

        /// <summary>
        /// [2콤보 C] 사격 + 벽.
        /// 사격 후 벽 센트리가 하늘에서 낙하 압착합니다.
        /// </summary>
        private IEnumerator Combo_ShootWall(Enemy target)
        {
            _isComboPlaying = true;
            Debug.Log("<color=cyan>[2콤보 C] 사격 + 벽!</color>");

            Vector3 shootPrePos = _shootSentry.transform.position;
            Vector3 wallPrePos = _wallSentry.transform.position;

            SetInvincible(true, _shootSentry, _wallSentry);
            _shootSentry.StopFollowing();
            _wallSentry.StopFollowing();

            var (_, shootComboPos, wallComboPos) = CalcComboPositions(target);

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

            if (target != null)
            {
                Vector3 skyPos = target.transform.position + Vector3.up * 5f;
                _wallSentry.transform.position = skyPos;
                _wallSentry.transform.localScale = Vector3.zero;
                _wallSentry.transform.DOScale(Vector3.one, 0.15f).SetEase(Ease.OutBack);

                yield return new WaitForSeconds(0.2f);

                yield return _wallSentry.transform
                    .DOMove(target.transform.position, 0.22f)
                    .SetEase(Ease.InQuart).WaitForCompletion();

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

        /// <summary>
        /// [3콤보] 전원 연계.
        /// 타격이 날림 → 벽이 붙잡음 → 사격 연발 → 최종 밀치기
        /// </summary>
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

            var (strikeComboPos, shootComboPos, wallComboPos) = CalcComboPositions(target);

            _strikeSentry.transform.DOMove(strikeComboPos, 0.5f).SetEase(Ease.OutBack);
            _shootSentry.transform.DOMove(shootComboPos, 0.5f).SetEase(Ease.OutBack);
            yield return _wallSentry.transform
                .DOMove(wallComboPos, 0.5f).SetEase(Ease.OutBack).WaitForCompletion();

            yield return new WaitForSeconds(0.2f);

            int combo3Damage = Mathf.RoundToInt(20 * _combo3DamageMultiplier);

            // 1단계: 타격 → 날리기
            yield return _strikeSentry.transform
                .DOMove(target.transform.position + Vector3.left * 0.3f, 0.12f)
                .SetEase(Ease.InExpo).WaitForCompletion();

            if (target != null)
                target.TakeDamage(combo3Damage / 4, HitType.Strike, _strikeSentry.transform.position);

            _strikeSentry.transform.DOShakePosition(0.2f, 0.3f, 15, 90f);
            yield return new WaitForSeconds(0.15f);

            // 2단계: 벽 센트리 → 붙잡기
            Rigidbody2D enemyRb = target != null ? target.GetComponent<Rigidbody2D>() : null;

            if (target != null)
            {
                yield return _wallSentry.transform
                    .DOMove(target.transform.position + Vector3.right * 0.5f, 0.18f)
                    .SetEase(Ease.InQuad).WaitForCompletion();

                _wallSentry.transform.DOShakePosition(0.2f, 0.25f, 12, 90f);
            }

            yield return new WaitForSeconds(0.3f);

            // 3단계: 사격 연속 발사
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

            // 4단계: 최종 밀치기
            if (target != null)
            {
                target.TakeDamage(combo3Damage / 3, HitType.Strike, _wallSentry.transform.position);
                target.Stun(1.5f);

                if (enemyRb != null)
                {
                    enemyRb.bodyType = RigidbodyType2D.Dynamic;
                    enemyRb.AddForce(Vector2.right * 12f, ForceMode2D.Impulse);
                }
            }

            _wallSentry.transform.DOShakePosition(0.3f, 0.45f, 20, 90f);
            yield return new WaitForSeconds(0.5f);

            // 복귀
            _strikeSentry.transform.DOMove(strikePrePos, 0.6f).SetEase(Ease.OutElastic);
            _shootSentry.transform.DOMove(shootPrePos, 0.6f).SetEase(Ease.OutElastic);
            yield return _wallSentry.transform
                .DOMove(wallPrePos, 0.6f).SetEase(Ease.OutElastic).WaitForCompletion();

            SetInvincible(false, _strikeSentry, _shootSentry, _wallSentry);
            _strikeSentry.StartFollowing();
            _shootSentry.StartFollowing();
            _wallSentry.StartFollowing();

            Debug.Log("[3콤보] 전원 연계 종료");
            OnComboFinished();
        }
    }
}