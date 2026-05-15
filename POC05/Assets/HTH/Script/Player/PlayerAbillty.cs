using UnityEngine;
using System.Collections;
using DG.Tweening;

namespace SENTRY
{
    /// <summary>
    /// 플레이어가 사용하는 3가지 특수 능력을 관리하는 컴포넌트.
    ///
    /// [UI 업데이트 흐름]
    ///   _abilityNTimer (감소) → AbilityNCooldownRatio (0→1)
    ///     → PlayerBattleUIManager.RefreshAbilitySlot()
    ///         → fill.fillAmount = 1f - ratio  (1→0, Radial 360 어두운 오버레이 줄어듦)
    ///         → text.text = 남은초            (15→0)
    ///
    /// [디버그]
    ///   Inspector의 _debugTimerLog를 체크하면 0.5초마다 타이머/ratio를 Console에 출력합니다.
    ///   값이 전혀 변하지 않으면 BattleManager에서 OnBattleStart()가 호출되지 않은 것입니다.
    /// </summary>
    public class PlayerAbility : MonoBehaviour
    {
        // ─────────────────────────────────────────
        //  Inspector — 센트리 참조
        // ─────────────────────────────────────────

        [Header("센트리 참조")]
        [SerializeField] private StrikeSentry _strikeSentry;
        [SerializeField] private ShootSentry _shootSentry;
        [SerializeField] private WallSentry _wallSentry;

        // ─────────────────────────────────────────
        //  Inspector — 능력 1: 목표 지정
        // ─────────────────────────────────────────

        [Header("능력 1 — 목표 지정 (Q)")]
        [Tooltip("전투 시작 후 첫 사용까지 대기 시간 (초). Cooldown보다 크면 Cooldown으로 클램프됩니다.")]
        [SerializeField] private float _ability1InitialDelay = 5f;

        [Tooltip("사용 후 쿨타임 (초)")]
        [SerializeField] private float _ability1Cooldown = 15f;

        [Tooltip("목표 지정 유지 시간 (초)")]
        [SerializeField] private float _targetMarkDuration = 8f;

        // ─────────────────────────────────────────
        //  Inspector — 능력 2: 긴급 수리
        // ─────────────────────────────────────────

        [Header("능력 2 — 긴급 수리 (W)")]
        [SerializeField] private float _ability2InitialDelay = 8f;
        [SerializeField] private float _ability2Cooldown = 20f;
        [SerializeField] private int _repairHealAmount = 50;

        // ─────────────────────────────────────────
        //  Inspector — 능력 3: 과부화
        // ─────────────────────────────────────────

        [Header("능력 3 — 과부화 (E)")]
        [SerializeField] private float _ability3InitialDelay = 10f;
        [SerializeField] private float _ability3Cooldown = 40f;
        [SerializeField] private float _overloadDuration = 6f;
        [SerializeField] private float _overloadDamageMultiplier = 2f;
        [SerializeField] private float _overloadSpeedMultiplier = 1.5f;

        // ─────────────────────────────────────────
        //  Inspector — 디버그
        // ─────────────────────────────────────────

        [Header("디버그")]
        [Tooltip("체크 시 0.5초마다 타이머/ratio 값을 Console에 출력합니다.\n" +
                 "값이 변하지 않으면 BattleManager에서 OnBattleStart()가 호출되지 않은 것입니다.")]
        [SerializeField] private bool _debugTimerLog = false;

        // ─────────────────────────────────────────
        //  내부 상태 변수
        // ─────────────────────────────────────────

        private float _ability1Timer;
        private float _ability2Timer;
        private float _ability3Timer;

        /// <summary>
        /// 배틀 활성 여부. false이면 타이머 감소 없음 → UI도 변하지 않음.
        /// BattleManager.BattleStartRoutine()에서 OnBattleStart()를 호출해야 true가 됩니다.
        /// </summary>
        private bool _isBattleActive = false;

        private bool _isSelectingTarget = false;
        private int _pendingAbilityNumber = 0;
        private int _selectedSlotIndex = 0;
        private int _maxEnemySlots = 0;
        private Enemy _designatedTarget;

        private float _debugLogTimer = 0f;

        // ─────────────────────────────────────────
        //  외부 공개 프로퍼티
        // ─────────────────────────────────────────

        /// <summary>
        /// 능력 1 쿨타임 비율 (0 = 쿨타임 중 / 1 = 사용 가능).
        /// UI Fill: fillAmount = 1f - ratio (1→0 Radial 360 줄어드는 방향).
        /// UI Text: (1f - ratio) * Cooldown 초 (15→0).
        /// </summary>
        public float Ability1CooldownRatio => CalcRatio(_ability1Timer, _ability1Cooldown);
        public float Ability2CooldownRatio => CalcRatio(_ability2Timer, _ability2Cooldown);
        public float Ability3CooldownRatio => CalcRatio(_ability3Timer, _ability3Cooldown);

        /// <summary>남은 시간 텍스트 계산용 최대 쿨타임 값.</summary>
        public float Ability1Cooldown => _ability1Cooldown;
        public float Ability2Cooldown => _ability2Cooldown;
        public float Ability3Cooldown => _ability3Cooldown;

        /// <summary>현재 지정된 목표 적.</summary>
        public Enemy DesignatedTarget => _designatedTarget;

        private static float CalcRatio(float timer, float cooldown)
        {
            if (timer <= 0f) return 1f;
            if (cooldown <= 0f) return 1f;
            return Mathf.Clamp01(1f - timer / cooldown);
        }

        // ─────────────────────────────────────────
        //  유니티 생명주기
        // ─────────────────────────────────────────

        private void Start()
        {
            // 배틀 전: timer = cooldown → ratio = 0 → UI 게이지 비어있음
            _ability1Timer = _ability1Cooldown;
            _ability2Timer = _ability2Cooldown;
            _ability3Timer = _ability3Cooldown;
        }

        private void Update()
        {
            // ── 타이머 감소 (_isBattleActive = true일 때만) ──
            if (_isBattleActive)
            {
                if (_ability1Timer > 0f) _ability1Timer -= Time.deltaTime;
                if (_ability2Timer > 0f) _ability2Timer -= Time.deltaTime;
                if (_ability3Timer > 0f) _ability3Timer -= Time.deltaTime;
            }

            // ── 디버그 로그 ──
            if (_debugTimerLog)
            {
                _debugLogTimer += Time.deltaTime;
                if (_debugLogTimer >= 0.5f)
                {
                    _debugLogTimer = 0f;
                    Debug.Log(
                        $"[PlayerAbility] active={_isBattleActive} | " +
                        $"T1={_ability1Timer:F1}s ratio={Ability1CooldownRatio:F2} fill={1f - Ability1CooldownRatio:F2} | " +
                        $"T2={_ability2Timer:F1}s ratio={Ability2CooldownRatio:F2} | " +
                        $"T3={_ability3Timer:F1}s ratio={Ability3CooldownRatio:F2}");
                }
            }

            // ── 배틀 필드에서만 입력 처리 ──
            if (BattleManager.Instance == null || !BattleManager.Instance.IsInBattle)
            {
                if (_isSelectingTarget) CancelSelection();
                return;
            }

            if (_isSelectingTarget)
                HandleSelectionInput();
            else
                HandleAbilityKeyInput();
        }

        // ─────────────────────────────────────────
        //  배틀 상태 이벤트
        // ─────────────────────────────────────────

        /// <summary>
        /// BattleManager.BattleStartRoutine()에서 반드시 호출해야 합니다.
        /// 이 메서드가 호출되지 않으면 _isBattleActive = false로 유지되어
        /// 타이머가 감소하지 않고 UI도 변하지 않습니다.
        ///
        /// [체크리스트]
        /// □ BattleManager Inspector에 _playerAbility 연결 확인
        /// □ BattleStartRoutine() 내에 _playerAbility?.OnBattleStart(); 코드 확인
        /// </summary>
        public void OnBattleStart()
        {
            _isBattleActive = true;

            // InitialDelay를 Cooldown으로 클램프 → ratio = 1 - delay/cooldown 으로 시작
            // 완전히 빈 게이지에서 시작하려면 InitialDelay = Cooldown 으로 설정하세요.
            _ability1Timer = Mathf.Min(_ability1InitialDelay, _ability1Cooldown);
            _ability2Timer = Mathf.Min(_ability2InitialDelay, _ability2Cooldown);
            _ability3Timer = Mathf.Min(_ability3InitialDelay, _ability3Cooldown);

            Debug.Log($"<color=cyan>[PlayerAbility] OnBattleStart 호출 — " +
                      $"T1={_ability1Timer:F1} T2={_ability2Timer:F1} T3={_ability3Timer:F1}</color>");
        }

        /// <summary>BattleManager.EndBattle()에서 호출합니다.</summary>
        public void OnBattleEnd()
        {
            _isBattleActive = false;
            CancelSelection();
            ClearDesignatedTarget();

            _ability1Timer = _ability1Cooldown;
            _ability2Timer = _ability2Cooldown;
            _ability3Timer = _ability3Cooldown;

            Debug.Log("[PlayerAbility] OnBattleEnd 호출 — 타이머 초기화");
        }

        // ─────────────────────────────────────────
        //  능력 키 입력
        // ─────────────────────────────────────────

        private void HandleAbilityKeyInput()
        {
            if (Input.GetKeyDown(KeyCode.Q)) TryEnterSelectionMode(1);
            if (Input.GetKeyDown(KeyCode.W)) TryEnterSelectionMode(2);
            if (Input.GetKeyDown(KeyCode.E)) TryEnterSelectionMode(3);
        }

        private void TryEnterSelectionMode(int abilityNumber)
        {
            float timer = abilityNumber switch
            {
                1 => _ability1Timer,
                2 => _ability2Timer,
                3 => _ability3Timer,
                _ => float.MaxValue
            };

            if (timer > 0f)
            {
                Debug.Log($"[능력{abilityNumber}] 쿨타임 중 ({timer:F1}초 남음)");
                return;
            }

            _pendingAbilityNumber = abilityNumber;
            _selectedSlotIndex = 0;
            _isSelectingTarget = true;

            if (abilityNumber == 1)
            {
                _maxEnemySlots = EnemyBattleUIManager.Instance?.ActiveSlotCount ?? 0;
                if (_maxEnemySlots == 0)
                {
                    Debug.Log("[능력1] 선택 가능한 적 없음");
                    CancelSelection();
                    return;
                }
            }

            UpdateMarkers();
            string name = abilityNumber switch
            {
                1 => "목표 지정",
                2 => "긴급 수리",
                _ => "과부화"
            };
            Debug.Log($"[능력{abilityNumber}] {name} — ← → 선택, Space 확정");
        }

        // ─────────────────────────────────────────
        //  선택 대기 중 입력
        // ─────────────────────────────────────────

        private void HandleSelectionInput()
        {
            if (Input.GetKeyDown(KeyCode.Escape)) { CancelSelection(); return; }
            if (Input.GetKeyDown(KeyCode.LeftArrow)) MoveSlot(-1);
            if (Input.GetKeyDown(KeyCode.RightArrow)) MoveSlot(+1);
            if (Input.GetKeyDown(KeyCode.Space)) ConfirmSelection();
        }

        private void MoveSlot(int delta)
        {
            int maxSlots = _pendingAbilityNumber == 1 ? _maxEnemySlots : 3;
            _selectedSlotIndex = (_selectedSlotIndex + delta + maxSlots) % maxSlots;
            UpdateMarkers();
        }

        private void ConfirmSelection()
        {
            int ability = _pendingAbilityNumber;
            int slot = _selectedSlotIndex;

            AbilityMarkerManager.Instance?.HideAll();
            _isSelectingTarget = false;
            _pendingAbilityNumber = 0;

            switch (ability)
            {
                case 1: ExecuteDesignateTarget(slot); break;
                case 2: ExecuteRepair(GetSentryBySlot(slot)); break;
                case 3: ExecuteOverload(GetSentryBySlot(slot)); break;
            }
        }

        private void CancelSelection()
        {
            AbilityMarkerManager.Instance?.HideAll();
            _isSelectingTarget = false;
            _pendingAbilityNumber = 0;
            Debug.Log("[능력] 선택 취소");
        }

        // ─────────────────────────────────────────
        //  마커 갱신
        // ─────────────────────────────────────────

        private void UpdateMarkers()
        {
            var marker = AbilityMarkerManager.Instance;
            if (marker == null) return;

            bool isFirst = !marker.IsUIMarkerVisible;

            if (_pendingAbilityNumber == 1)
            {
                RectTransform slotRect =
                    EnemyBattleUIManager.Instance?.GetSlotRect(_selectedSlotIndex);
                Transform enemyTr =
                    EnemyBattleUIManager.Instance?.GetEnemyBySlot(_selectedSlotIndex)?.transform;

                marker.ShowUIMarker(slotRect, instant: isFirst, isEnemySlot: true);
                marker.ShowWorldMarker(enemyTr, instant: isFirst);
            }
            else
            {
                RectTransform slotRect =
                    PlayerBattleUIManager.Instance?.GetSentrySlotRect(_selectedSlotIndex);
                Transform sentryTr = GetSentryBySlot(_selectedSlotIndex)?.transform;

                marker.ShowUIMarker(slotRect, instant: isFirst, isEnemySlot: false);
                marker.ShowWorldMarker(sentryTr, instant: isFirst);
            }
        }

        // ─────────────────────────────────────────
        //  능력 실행
        // ─────────────────────────────────────────

        private void ExecuteDesignateTarget(int slotIndex)
        {
            Enemy target = EnemyBattleUIManager.Instance?.GetEnemyBySlot(slotIndex);
            if (target == null || target.IsDead)
            {
                Debug.Log("[능력1] 대상이 이미 사망했습니다.");
                return;
            }

            _ability1Timer = _ability1Cooldown;
            ClearDesignatedTarget();
            _designatedTarget = target;

            StartCoroutine(DesignationExpireRoutine(target));
            Debug.Log($"<color=yellow>[능력1] 목표 지정: {target.name} ({_targetMarkDuration}초)</color>");
        }

        private IEnumerator DesignationExpireRoutine(Enemy target)
        {
            yield return new WaitForSeconds(_targetMarkDuration);
            if (_designatedTarget == target) ClearDesignatedTarget();
        }

        private void ClearDesignatedTarget() => _designatedTarget = null;

        private void ExecuteRepair(SentryBase sentry)
        {
            if (sentry == null) return;
            if (sentry.IsKnockedOut) { Debug.Log($"[능력2] {sentry.SentryName}은 KO 상태"); return; }

            sentry.Heal(_repairHealAmount);
            _ability2Timer = _ability2Cooldown;
            sentry.transform.DOPunchScale(Vector3.one * 0.2f, 0.3f, 5, 0.5f);
            Debug.Log($"<color=lime>[능력2] {sentry.SentryName} 긴급 수리 +{_repairHealAmount} HP</color>");
        }

        private void ExecuteOverload(SentryBase sentry)
        {
            if (sentry == null) return;
            if (sentry.IsKnockedOut) { Debug.Log($"[능력3] {sentry.SentryName}은 KO 상태"); return; }
            if (sentry.IsOverloaded) { Debug.Log($"[능력3] {sentry.SentryName}은 이미 과부화 중"); return; }

            _ability3Timer = _ability3Cooldown;
            StartCoroutine(OverloadRoutine(sentry));
        }

        private IEnumerator OverloadRoutine(SentryBase sentry)
        {
            sentry.SetOverload(true, _overloadDamageMultiplier, _overloadSpeedMultiplier);
            Debug.Log($"<color=red>[능력3] {sentry.SentryName} 과부화 시작!</color>");

            yield return new WaitForSeconds(_overloadDuration);

            if (sentry == null || sentry.IsKnockedOut) yield break;

            sentry.SetOverload(false, 1f, 1f);
            sentry.ForceKnockOut();
            Debug.Log($"[능력3] {sentry.SentryName} 과부화 종료 — KO");
        }

        // ─────────────────────────────────────────
        //  헬퍼
        // ─────────────────────────────────────────

        private SentryBase GetSentryBySlot(int slotIndex) => slotIndex switch
        {
            0 => _strikeSentry,
            1 => _shootSentry,
            2 => _wallSentry,
            _ => null
        };
    }
}