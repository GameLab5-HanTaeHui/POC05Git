using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;

namespace SENTRY
{
    /// <summary>
    /// 센트리 간 콤보 연계 시스템을 관리하는 싱글턴 매니저.
    ///
    /// [변경 사항 - Revision2: 실시간 위치 기반 콤보 포지션]
    ///
    /// 기존 문제:
    ///   - 콤보 포지션을 Inspector에서 고정 Transform으로 지정했음
    ///   - 센트리들이 전투 중 실시간으로 이동하기 때문에
    ///     고정 포지션으로 이동 시 전투 흐름이 부자연스럽고
    ///     실제 적의 위치와 동떨어진 곳에서 콤보가 발동될 수 있음
    ///
    /// 변경 후:
    ///   - 콤보 발동 시점의 적 위치와 각 센트리 위치를 기준으로
    ///     콤보 포지션을 동적으로 계산합니다.
    ///   - 콤보 연출 포지션 계산 기준:
    ///       · 집결 중심점: 타겟 적의 현재 위치
    ///       · 각 센트리는 중심점 기준 오프셋만큼 이동
    ///       · 오프셋은 배틀 필드(쿼터뷰) 기준 벡터로 설정
    ///   - Inspector의 고정 포지션 Transform 필드를 제거했습니다.
    ///
    /// [콤보 포지션 오프셋 설계 (쿼터뷰 기준)]
    ///   타겟 적 위치를 기준으로:
    ///   · Strike: 적 바로 앞 (근접 타격)
    ///   · Shoot:  적에서 일정 거리 후방 (원거리 사격)
    ///   · Wall:   적 옆 또는 위 (압착/낙하)
    /// </summary>
    public class ComboManager : MonoBehaviour
    {
        // ─────────────────────────────────────────
        //  싱글턴
        // ─────────────────────────────────────────

        public static ComboManager Instance { get; private set; }

        // ─────────────────────────────────────────
        //  Inspector 노출 필드
        // ─────────────────────────────────────────

        [Header("센트리 참조")]
        [Tooltip("타격 센트리")]
        [SerializeField] private StrikeSentry _strikeSentry;

        [Tooltip("사격 센트리")]
        [SerializeField] private ShootSentry _shootSentry;

        [Tooltip("벽 센트리")]
        [SerializeField] private WallSentry _wallSentry;

        [Header("콤보 게이지 설정")]
        [Tooltip("콤보 게이지 최대치")]
        [SerializeField] private float _maxComboGauge = 100f;

        [Tooltip("적 처치 1회당 콤보 게이지 충전량")]
        [SerializeField] private float _comboGaugePerKill = 20f;

        [Tooltip("센트리 스킬 발동 1회당 콤보 게이지 충전량")]
        [SerializeField] private float _comboGaugePerSkill = 15f;

        [Header("콤보 쿨타임")]
        [Tooltip("3센트리 콤보 쿨타임 (초)")]
        [SerializeField] private float _combo3Cooldown = 60f;

        [Tooltip("2센트리 콤보 쿨타임 (초)")]
        [SerializeField] private float _combo2Cooldown = 25f;

        [Header("콤보 포지션 오프셋 (쿼터뷰 기준, 적 위치 기준 상대 좌표)")]
        [Tooltip("타격 센트리가 이동할 오프셋.\n" +
                 "적 바로 앞(근접)에 배치합니다.\n" +
                 "예) (-1.5f, 0f) = 적의 왼쪽 1.5 유닛")]
        [SerializeField] private Vector2 _strikeComboOffset = new Vector2(-1.5f, 0f);

        [Tooltip("사격 센트리가 이동할 오프셋.\n" +
                 "적에서 후방 거리에 배치합니다.\n" +
                 "예) (-4f, 0f) = 적의 왼쪽 4 유닛 (원거리)")]
        [SerializeField] private Vector2 _shootComboOffset = new Vector2(-4f, 0f);

        [Tooltip("벽 센트리가 이동할 오프셋.\n" +
                 "적 옆에 배치합니다.\n" +
                 "예) (1f, 0f) = 적의 오른쪽 1 유닛 (압착용)")]
        [SerializeField] private Vector2 _wallComboOffset = new Vector2(1f, 0f);

        [Header("3콤보 전용 설정")]
        [Tooltip("3콤보 사격 단계 연속 발사 수")]
        [SerializeField] private int _combo3BulletCount = 10;

        [Tooltip("3콤보 탄환 발사 간격 (초)")]
        [SerializeField] private float _combo3BulletInterval = 0.1f;

        [Tooltip("3콤보 데미지 배율")]
        [SerializeField] private float _combo3DamageMultiplier = 4f;

        [Header("2콤보 설정")]
        [Tooltip("2콤보 데미지 배율")]
        [SerializeField] private float _combo2DamageMultiplier = 2.5f;

        [Tooltip("타격+사격 콤보용 관통탄 프리팹")]
        [SerializeField] private GameObject _piercingBulletPrefab;

        // ─────────────────────────────────────────
        //  내부 상태 변수
        // ─────────────────────────────────────────

        private float _currentComboGauge = 0f;
        private bool _isComboPlaying = false;
        private float _combo3CooldownTimer = 0f;
        private float _combo2CooldownTimer = 0f;
        private bool _isInBattle = false;

        // ─────────────────────────────────────────
        //  외부 공개 프로퍼티
        // ─────────────────────────────────────────

        public float ComboGauge => _currentComboGauge;
        public float MaxComboGauge => _maxComboGauge;

        public float Combo3CooldownRatio =>
            _combo3Cooldown > 0f
                ? Mathf.Clamp01(1f - (_combo3CooldownTimer / _combo3Cooldown))
                : 1f;

        public float Combo2CooldownRatio =>
            _combo2Cooldown > 0f
                ? Mathf.Clamp01(1f - (_combo2CooldownTimer / _combo2Cooldown))
                : 1f;

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
            if (_combo3CooldownTimer > 0f) _combo3CooldownTimer -= Time.deltaTime;
            if (_combo2CooldownTimer > 0f) _combo2CooldownTimer -= Time.deltaTime;
        }

        // ─────────────────────────────────────────
        //  배틀 상태
        // ─────────────────────────────────────────

        public void OnBattleStart()
        {
            _isInBattle = true;
            _currentComboGauge = 0f;
        }

        public void OnBattleEnd()
        {
            _isInBattle = false;
        }

        // ─────────────────────────────────────────
        //  게이지 충전
        // ─────────────────────────────────────────

        public void OnEnemyKilled()
        {
            if (!_isInBattle || _isComboPlaying) return;
            ChargeGauge(_comboGaugePerKill);
        }

        public void OnSentrySkillUsed()
        {
            if (!_isInBattle || _isComboPlaying) return;
            ChargeGauge(_comboGaugePerSkill);
        }

        private void ChargeGauge(float amount)
        {
            _currentComboGauge = Mathf.Min(_currentComboGauge + amount, _maxComboGauge);
            if (_currentComboGauge >= _maxComboGauge)
                TryTriggerCombo();
        }

        // ─────────────────────────────────────────
        //  콤보 발동 판단
        // ─────────────────────────────────────────

        private void TryTriggerCombo()
        {
            if (_isComboPlaying) return;

            Enemy target = FindNearestEnemy();
            if (target == null) return;

            bool strikeAlive = _strikeSentry != null && !_strikeSentry.IsKnockedOut;
            bool shootAlive = _shootSentry != null && !_shootSentry.IsKnockedOut;
            bool wallAlive = _wallSentry != null && !_wallSentry.IsKnockedOut;

            // 3콤보 우선
            if (strikeAlive && shootAlive && wallAlive && _combo3CooldownTimer <= 0f)
            {
                _currentComboGauge = 0f;
                _combo3CooldownTimer = _combo3Cooldown;
                StartCoroutine(Combo_AllThree(target));
                return;
            }

            // 2콤보
            if (_combo2CooldownTimer > 0f) return;

            if (strikeAlive && shootAlive)
            {
                _currentComboGauge = 0f;
                _combo2CooldownTimer = _combo2Cooldown;
                StartCoroutine(Combo_StrikeShoot(target));
            }
            else if (strikeAlive && wallAlive)
            {
                _currentComboGauge = 0f;
                _combo2CooldownTimer = _combo2Cooldown;
                StartCoroutine(Combo_StrikeWall(target));
            }
            else if (shootAlive && wallAlive)
            {
                _currentComboGauge = 0f;
                _combo2CooldownTimer = _combo2Cooldown;
                StartCoroutine(Combo_ShootWall(target));
            }
        }

        // ─────────────────────────────────────────
        //  콤보 포지션 동적 계산
        // ─────────────────────────────────────────

        /// <summary>
        /// 타겟 적의 현재 위치를 기준으로 각 센트리의 콤보 집결 위치를 계산합니다.
        /// 전투 중 적의 위치가 항상 바뀌므로 콤보 발동 시점에 매번 새로 계산합니다.
        /// </summary>
        /// <param name="target">콤보 대상 적</param>
        /// <returns>Strike / Shoot / Wall 콤보 위치 (월드 좌표)</returns>
        private (Vector3 strike, Vector3 shoot, Vector3 wall) CalcComboPositions(Enemy target)
        {
            Vector3 basePos = target.transform.position;

            // 적 위치를 기준으로 오프셋을 더해 각 센트리 위치를 계산
            // 오프셋은 쿼터뷰 기준 (X: 좌우, Y: 상하 원근감)
            Vector3 strikePos = basePos + (Vector3)_strikeComboOffset;
            Vector3 shootPos = basePos + (Vector3)_shootComboOffset;
            Vector3 wallPos = basePos + (Vector3)_wallComboOffset;

            return (strikePos, shootPos, wallPos);
        }

        // ─────────────────────────────────────────
        //  헬퍼: 가장 가까운 적 탐색
        // ─────────────────────────────────────────

        private Enemy FindNearestEnemy()
        {
            Enemy[] enemies = FindObjectsByType<Enemy>(FindObjectsSortMode.None);
            if (enemies.Length == 0) return null;

            Enemy nearest = null;
            float minDist = float.MaxValue;
            foreach (Enemy e in enemies)
            {
                float d = Vector3.Distance(transform.position, e.transform.position);
                if (d < minDist) { minDist = d; nearest = e; }
            }
            return nearest;
        }

        // ─────────────────────────────────────────
        //  헬퍼: 무적 ON / OFF
        // ─────────────────────────────────────────

        private void SetInvincible(bool on, params SentryBase[] sentries)
        {
            foreach (var s in sentries)
                if (s != null) s.SetInvincible(on);
        }

        // ─────────────────────────────────────────
        //  헬퍼: 포메이션 복귀 (배틀 종료 후 탐색 필드 복귀와 다름)
        //  콤보 종료 후 전투 위치로 되돌아가는 것
        //  → 현재 전투 중이므로 전투 AI가 바로 이동을 재개하면 됨
        //  → 별도 위치 복귀 불필요 (AI가 자율 이동)
        // ─────────────────────────────────────────

        /// <summary>
        /// 콤보 종료 후 센트리를 콤보 직전 위치로 복귀시킵니다.
        /// 콤보 중 이동한 거리만큼 원래 전투 위치 근처로 돌아갑니다.
        /// 이후 전투 AI가 다시 자율적으로 움직입니다.
        /// </summary>
        private IEnumerator ReturnFromCombo(
            SentryBase sentry,
            Vector3 preComboPosition,
            float duration = 0.5f)
        {
            if (sentry == null) yield break;
            yield return sentry.transform
                .DOMove(preComboPosition, duration)
                .SetEase(Ease.OutElastic)
                .WaitForCompletion();
        }

        // ─────────────────────────────────────────
        //  2콤보 A: 타격 + 사격
        // ─────────────────────────────────────────

        /// <summary>
        /// [2콤보 A] 타격 + 사격.
        /// 콤보 발동 시점의 적 위치를 기준으로 집결 위치를 동적 계산합니다.
        /// </summary>
        private IEnumerator Combo_StrikeShoot(Enemy target)
        {
            _isComboPlaying = true;
            Debug.Log("<color=cyan>[2콤보 A] 타격 + 사격!</color>");

            // 콤보 전 현재 위치 저장 (복귀용)
            Vector3 strikePrePos = _strikeSentry.transform.position;
            Vector3 shootPrePos = _shootSentry.transform.position;

            // 무적 ON + 추종 중단
            SetInvincible(true, _strikeSentry, _shootSentry);
            _strikeSentry.StopFollowing();
            _shootSentry.StopFollowing();

            // ── 적 위치 기준 콤보 포지션 동적 계산 ──
            var (strikeComboPos, shootComboPos, _) = CalcComboPositions(target);

            // 집결 이동
            _strikeSentry.transform.DOMove(strikeComboPos, 0.5f).SetEase(Ease.OutBack);
            yield return _shootSentry.transform
                .DOMove(shootComboPos, 0.5f)
                .SetEase(Ease.OutBack)
                .WaitForCompletion();

            yield return new WaitForSeconds(0.2f);

            // 타격 센트리 → 사격 센트리 총구 타격 모션
            Vector3 impactPoint = _shootSentry.transform.position + Vector3.right * 0.4f;

            yield return _strikeSentry.transform
                .DOMove(impactPoint, 0.1f)
                .SetEase(Ease.InExpo)
                .WaitForCompletion();

            _strikeSentry.transform.DOShakePosition(0.15f, 0.25f, 15, 90f);
            _shootSentry.transform.DOShakePosition(0.2f, 0.2f, 12, 90f);

            yield return new WaitForSeconds(0.1f);

            // 관통탄 발사
            if (target != null && _piercingBulletPrefab != null)
            {
                int comboDamage = Mathf.RoundToInt(20 * _combo2DamageMultiplier);
                GameObject bulletObj = Instantiate(
                    _piercingBulletPrefab,
                    _shootSentry.transform.position,
                    Quaternion.identity);

                SentryPiercingBullet pb = bulletObj.GetComponent<SentryPiercingBullet>();
                if (pb != null)
                {
                    pb.damage = comboDamage;
                    pb.Setup(target.transform);
                }
            }

            _shootSentry.transform.DOPunchPosition(Vector3.left * 0.35f, 0.15f, 8, 0.5f);
            yield return new WaitForSeconds(0.4f);

            // 콤보 전 위치로 복귀 → AI 재개
            yield return StartCoroutine(ReturnFromCombo(_strikeSentry, strikePrePos));
            yield return StartCoroutine(ReturnFromCombo(_shootSentry, shootPrePos));

            SetInvincible(false, _strikeSentry, _shootSentry);
            _strikeSentry.StartFollowing();
            _shootSentry.StartFollowing();

            _isComboPlaying = false;
            Debug.Log("[2콤보 A] 종료");
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

            var (strikeComboPos, _, wallComboPos) = CalcComboPositions(target);

            _strikeSentry.transform.DOMove(strikeComboPos, 0.5f).SetEase(Ease.OutBack);
            yield return _wallSentry.transform
                .DOMove(wallComboPos, 0.5f)
                .SetEase(Ease.OutBack)
                .WaitForCompletion();

            yield return new WaitForSeconds(0.2f);

            // 타격 → 벽 방향 강타
            Vector3 hitPoint = _wallSentry.transform.position + Vector3.left * 0.3f;

            yield return _strikeSentry.transform
                .DOMove(hitPoint, 0.1f)
                .SetEase(Ease.InExpo)
                .WaitForCompletion();

            _strikeSentry.transform.DOShakePosition(0.2f, 0.3f, 15, 90f);

            yield return new WaitForSeconds(0.08f);

            // 벽 센트리 고속 돌진 → 압착
            if (target != null)
            {
                yield return _wallSentry.transform
                    .DOMove(target.transform.position, 0.2f)
                    .SetEase(Ease.InQuart)
                    .WaitForCompletion();

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

            _isComboPlaying = false;
            Debug.Log("[2콤보 B] 종료");
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

            var (_, shootComboPos, wallComboPos) = CalcComboPositions(target);

            _shootSentry.transform.DOMove(shootComboPos, 0.5f).SetEase(Ease.OutBack);
            yield return _wallSentry.transform
                .DOMove(wallComboPos, 0.5f)
                .SetEase(Ease.OutBack)
                .WaitForCompletion();

            yield return new WaitForSeconds(0.2f);

            // 사격 2발
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

            // 벽 센트리 하늘 → 낙하 압착
            if (target != null)
            {
                Vector3 skyPos = target.transform.position + Vector3.up * 5f;
                _wallSentry.transform.position = skyPos;
                _wallSentry.transform.localScale = Vector3.zero;
                _wallSentry.transform.DOScale(Vector3.one, 0.15f).SetEase(Ease.OutBack);

                yield return new WaitForSeconds(0.2f);

                yield return _wallSentry.transform
                    .DOMove(target.transform.position, 0.22f)
                    .SetEase(Ease.InQuart)
                    .WaitForCompletion();

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

            _isComboPlaying = false;
            Debug.Log("[2콤보 C] 종료");
        }

        // ─────────────────────────────────────────
        //  3콤보: 전원 연계
        // ─────────────────────────────────────────

        private IEnumerator Combo_AllThree(Enemy target)
        {
            _isComboPlaying = true;
            Debug.Log("<color=yellow>[3콤보] 전원 연계 발동!</color>");

            // 콤보 전 위치 저장
            Vector3 strikePrePos = _strikeSentry.transform.position;
            Vector3 shootPrePos = _shootSentry.transform.position;
            Vector3 wallPrePos = _wallSentry.transform.position;

            SetInvincible(true, _strikeSentry, _shootSentry, _wallSentry);
            _strikeSentry.StopFollowing();
            _shootSentry.StopFollowing();
            _wallSentry.StopFollowing();

            // ── 적 위치 기준 동적 포지션 계산 ──
            var (strikeComboPos, shootComboPos, wallComboPos) = CalcComboPositions(target);

            // 시간차 집결
            _strikeSentry.transform.DOMove(strikeComboPos, 0.6f).SetEase(Ease.OutBack);
            yield return new WaitForSeconds(0.1f);
            _shootSentry.transform.DOMove(shootComboPos, 0.6f).SetEase(Ease.OutBack);
            yield return new WaitForSeconds(0.1f);
            yield return _wallSentry.transform
                .DOMove(wallComboPos, 0.6f)
                .SetEase(Ease.OutBack)
                .WaitForCompletion();

            yield return new WaitForSeconds(0.3f);

            if (target == null)
            {
                yield return StartCoroutine(EndCombo_AllThree(
                    strikePrePos, shootPrePos, wallPrePos));
                yield break;
            }

            // 타격 돌진 + 날리기
            yield return _strikeSentry.transform
                .DOMove(target.transform.position + Vector3.left * 0.3f, 0.12f)
                .SetEase(Ease.InExpo)
                .WaitForCompletion();

            int combo3Damage = Mathf.RoundToInt(30 * _combo3DamageMultiplier);
            target.TakeDamage(combo3Damage / 3, HitType.Strike, _strikeSentry.transform.position);
            _strikeSentry.transform.DOShakePosition(0.2f, 0.3f, 15, 90f);

            yield return new WaitForSeconds(0.1f);

            // 적을 벽 센트리 위치로 DOMove (키네마틱 처리)
            Rigidbody2D enemyRb = target.GetComponent<Rigidbody2D>();
            if (enemyRb != null) enemyRb.bodyType = RigidbodyType2D.Kinematic;

            yield return target.transform
                .DOMove(wallComboPos + Vector3.left * 0.5f, 0.3f)
                .SetEase(Ease.InQuart)
                .WaitForCompletion();

            // 타격 원위치 (콤보 전 위치 근처)
            _strikeSentry.transform.DOMove(strikeComboPos, 0.2f).SetEase(Ease.OutQuad);

            // 벽 붙잡기 연출
            _wallSentry.transform.DOPunchScale(Vector3.one * 0.4f, 0.25f, 6, 0.5f);
            _wallSentry.transform.DOShakePosition(0.2f, 0.15f, 10, 90f);
            target.TakeDamage(combo3Damage / 3, HitType.Strike, _wallSentry.transform.position);

            yield return new WaitForSeconds(0.3f);

            // 사격 연속 발사
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

            // 최종 밀치기
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

            yield return StartCoroutine(EndCombo_AllThree(
                strikePrePos, shootPrePos, wallPrePos));
        }

        /// <summary>3콤보 종료 공통 정리: 콤보 전 위치 복귀 + 무적 해제 + AI 재개</summary>
        private IEnumerator EndCombo_AllThree(
            Vector3 strikePrePos,
            Vector3 shootPrePos,
            Vector3 wallPrePos)
        {
            // 세 센트리 동시 복귀
            _strikeSentry.transform.DOMove(strikePrePos, 0.6f).SetEase(Ease.OutElastic);
            _shootSentry.transform.DOMove(shootPrePos, 0.6f).SetEase(Ease.OutElastic);
            yield return _wallSentry.transform
                .DOMove(wallPrePos, 0.6f)
                .SetEase(Ease.OutElastic)
                .WaitForCompletion();

            SetInvincible(false, _strikeSentry, _shootSentry, _wallSentry);
            _strikeSentry.StartFollowing();
            _shootSentry.StartFollowing();
            _wallSentry.StartFollowing();

            _isComboPlaying = false;
            Debug.Log("[3콤보] 전원 연계 종료");
        }
    }
}