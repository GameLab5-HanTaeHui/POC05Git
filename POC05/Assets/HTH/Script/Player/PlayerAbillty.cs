using UnityEngine;
using System.Collections;
using DG.Tweening;

namespace SENTRY
{
    /// <summary>
    /// 플레이어가 사용하는 3가지 특수 능력을 관리하는 컴포넌트.
    ///
    /// [변경 사항]
    /// - PlayerHealth 의존성 완전 제거 (플레이어 HP 시스템 삭제)
    /// - 배틀 필드에서만 능력 사용 가능하도록 BattleManager.IsInBattle 체크 추가
    ///   탐색 필드에서 능력키를 누르면 조용히 무시합니다.
    ///
    /// [능력 설명]
    /// - 능력 1 [목표 지정]: 마우스 클릭 위치의 적을 모든 센트리의 우선 타겟으로 지정
    /// - 능력 2 [긴급 수리]: 숫자키로 선택한 센트리 1기 HP 일부 회복
    /// - 능력 3 [과부화]:    숫자키로 선택한 센트리 1기 폭주 → 종료 후 기절
    ///
    /// [히어라키 위치]
    /// Player
    ///   ├── PlayerController
    ///   └── PlayerAbility (이 스크립트)
    /// </summary>
    public class PlayerAbility : MonoBehaviour
    {
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

        [Header("능력 1 — 목표 지정")]
        [Tooltip("능력 1 키")]
        [SerializeField] private KeyCode _ability1Key = KeyCode.Q;

        [Tooltip("능력 1 초기 대기 시간 (초). 배틀 진입 후 이 시간이 지나야 사용 가능.")]
        [SerializeField] private float _ability1InitialDelay = 5f;

        [Tooltip("능력 1 쿨타임 (초)")]
        [SerializeField] private float _ability1Cooldown = 15f;

        [Tooltip("목표 지정 유지 시간 (초). 이 시간 후 지정이 해제됩니다.")]
        [SerializeField] private float _targetMarkDuration = 8f;

        [Tooltip("목표 지정 시 적 위에 표시할 마커 프리팹 (없으면 생략)")]
        [SerializeField] private GameObject _targetMarkerPrefab;

        [Header("능력 2 — 긴급 수리")]
        [Tooltip("능력 2 키")]
        [SerializeField] private KeyCode _ability2Key = KeyCode.W;

        [Tooltip("능력 2 초기 대기 시간 (초)")]
        [SerializeField] private float _ability2InitialDelay = 8f;

        [Tooltip("능력 2 쿨타임 (초)")]
        [SerializeField] private float _ability2Cooldown = 20f;

        [Tooltip("긴급 수리로 회복할 HP 양")]
        [SerializeField] private int _repairHealAmount = 50;

        [Header("능력 3 — 과부화")]
        [Tooltip("능력 3 키")]
        [SerializeField] private KeyCode _ability3Key = KeyCode.E;

        [Tooltip("능력 3 초기 대기 시간 (초)")]
        [SerializeField] private float _ability3InitialDelay = 10f;

        [Tooltip("능력 3 쿨타임 (초)")]
        [SerializeField] private float _ability3Cooldown = 40f;

        [Tooltip("폭주 지속 시간 (초). 이 시간 후 해당 센트리가 기절합니다.")]
        [SerializeField] private float _overloadDuration = 6f;

        [Tooltip("폭주 중 공격력 배율")]
        [SerializeField] private float _overloadDamageMultiplier = 2f;

        [Tooltip("폭주 중 이동 속도 배율")]
        [SerializeField] private float _overloadSpeedMultiplier = 1.5f;

        [Header("센트리 선택 키")]
        [Tooltip("능력 2/3 사용 시 센트리를 선택하는 키 [0]=타격 [1]=사격 [2]=벽")]
        [SerializeField]
        private KeyCode[] _sentrySelectKeys =
            { KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3 };

        // ─────────────────────────────────────────
        //  내부 상태 변수
        // ─────────────────────────────────────────

        /// <summary>능력 1 남은 쿨타임 (초)</summary>
        private float _ability1Timer;

        /// <summary>능력 2 남은 쿨타임 (초)</summary>
        private float _ability2Timer;

        /// <summary>능력 3 남은 쿨타임 (초)</summary>
        private float _ability3Timer;

        /// <summary>센트리 선택 대기 중 여부</summary>
        private bool _isSelectingTarget = false;

        /// <summary>선택 대기 중인 능력 번호 (2 또는 3)</summary>
        private int _pendingAbilityNumber = 0;

        /// <summary>현재 지정된 목표 적</summary>
        private Enemy _designatedTarget;

        /// <summary>목표 지정 마커 인스턴스</summary>
        private GameObject _currentMarker;

        // ─────────────────────────────────────────
        //  외부 공개 프로퍼티
        // ─────────────────────────────────────────

        /// <summary>능력 1 쿨타임 비율 0~1 (UI fillAmount용)</summary>
        public float Ability1CooldownRatio =>
            _ability1Cooldown > 0f
                ? Mathf.Clamp01(1f - (_ability1Timer / _ability1Cooldown))
                : 1f;

        /// <summary>능력 2 쿨타임 비율 0~1</summary>
        public float Ability2CooldownRatio =>
            _ability2Cooldown > 0f
                ? Mathf.Clamp01(1f - (_ability2Timer / _ability2Cooldown))
                : 1f;

        /// <summary>능력 3 쿨타임 비율 0~1</summary>
        public float Ability3CooldownRatio =>
            _ability3Cooldown > 0f
                ? Mathf.Clamp01(1f - (_ability3Timer / _ability3Cooldown))
                : 1f;

        /// <summary>현재 지정된 목표 적 (센트리 AI에서 참조)</summary>
        public Enemy DesignatedTarget => _designatedTarget;

        // ─────────────────────────────────────────
        //  유니티 생명주기
        // ─────────────────────────────────────────

        private void Start()
        {
            // 초기 대기 시간 설정 (배틀 진입 직후 바로 사용 불가)
            _ability1Timer = _ability1InitialDelay;
            _ability2Timer = _ability2InitialDelay;
            _ability3Timer = _ability3InitialDelay;
        }

        private void Update()
        {
            // 쿨타임 카운트다운 (배틀 필드 여부 무관하게 항상 감소)
            if (_ability1Timer > 0f) _ability1Timer -= Time.deltaTime;
            if (_ability2Timer > 0f) _ability2Timer -= Time.deltaTime;
            if (_ability3Timer > 0f) _ability3Timer -= Time.deltaTime;

            // ── 배틀 필드에서만 능력 사용 가능 ──
            if (BattleManager.Instance == null || !BattleManager.Instance.IsInBattle)
            {
                // 탐색 필드: 선택 대기 상태 초기화 후 입력 무시
                if (_isSelectingTarget)
                {
                    _isSelectingTarget = false;
                    _pendingAbilityNumber = 0;
                }
                return;
            }

            HandleAbilityInput();

            // 센트리 선택 대기 중일 때 숫자키 입력 처리
            if (_isSelectingTarget)
                HandleSentrySelection();
        }

        // ─────────────────────────────────────────
        //  입력 처리
        // ─────────────────────────────────────────

        /// <summary>
        /// 능력 키 입력을 감지하고 각 능력 발동을 시도합니다.
        /// 배틀 필드에서만 호출됩니다.
        /// </summary>
        private void HandleAbilityInput()
        {
            if (_isSelectingTarget) return;

            if (Input.GetKeyDown(_ability1Key)) TryUseAbility1();
            if (Input.GetKeyDown(_ability2Key)) TryUseAbility2();
            if (Input.GetKeyDown(_ability3Key)) TryUseAbility3();
        }

        // ─────────────────────────────────────────
        //  능력 1: 목표 지정
        // ─────────────────────────────────────────

        /// <summary>
        /// 능력 1 [목표 지정] 사용을 시도합니다.
        /// 마우스 클릭 위치에 가장 가까운 적을 우선 타겟으로 지정합니다.
        /// </summary>
        private void TryUseAbility1()
        {
            if (_ability1Timer > 0f)
            {
                Debug.Log($"[능력1] 쿨타임 중 ({_ability1Timer:F1}초 남음)");
                return;
            }

            // 마우스 위치 근처의 적을 탐색
            Vector2 mouseWorld = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            Enemy target = FindEnemyNearPosition(mouseWorld, 2f)
                           ?? FindNearestEnemy();

            if (target == null)
            {
                Debug.Log("[능력1] 지정할 적이 없습니다.");
                return;
            }

            _designatedTarget = target;
            _ability1Timer = _ability1Cooldown;

            // 마커 연출
            if (_currentMarker != null) Destroy(_currentMarker);
            if (_targetMarkerPrefab != null)
                _currentMarker = Instantiate(_targetMarkerPrefab, target.transform.position,
                    Quaternion.identity, target.transform);

            Debug.Log($"<color=cyan>[능력1] 목표 지정: {target.name}</color>");

            StartCoroutine(ClearTargetAfterDuration(_targetMarkDuration));
        }

        /// <summary>목표 지정을 duration초 후 해제합니다.</summary>
        private IEnumerator ClearTargetAfterDuration(float duration)
        {
            yield return new WaitForSeconds(duration);
            _designatedTarget = null;
            if (_currentMarker != null) Destroy(_currentMarker);
            Debug.Log("[능력1] 목표 지정 해제");
        }

        // ─────────────────────────────────────────
        //  능력 2: 긴급 수리
        // ─────────────────────────────────────────

        /// <summary>
        /// 능력 2 [긴급 수리] 사용을 시도합니다.
        /// 숫자키로 센트리를 선택하는 대기 상태로 전환합니다.
        /// </summary>
        private void TryUseAbility2()
        {
            if (_ability2Timer > 0f)
            {
                Debug.Log($"[능력2] 쿨타임 중 ({_ability2Timer:F1}초 남음)");
                return;
            }

            _isSelectingTarget = true;
            _pendingAbilityNumber = 2;
            Debug.Log("<color=lime>[능력2] 긴급 수리 — 센트리 선택 (1=타격 / 2=사격 / 3=벽)</color>");
        }

        // ─────────────────────────────────────────
        //  능력 3: 과부화
        // ─────────────────────────────────────────

        /// <summary>
        /// 능력 3 [과부화] 사용을 시도합니다.
        /// 숫자키로 센트리를 선택하는 대기 상태로 전환합니다.
        /// </summary>
        private void TryUseAbility3()
        {
            if (_ability3Timer > 0f)
            {
                Debug.Log($"[능력3] 쿨타임 중 ({_ability3Timer:F1}초 남음)");
                return;
            }

            _isSelectingTarget = true;
            _pendingAbilityNumber = 3;
            Debug.Log("<color=red>[능력3] 과부화 — 센트리 선택 (1=타격 / 2=사격 / 3=벽)</color>");
        }

        // ─────────────────────────────────────────
        //  센트리 선택 처리
        // ─────────────────────────────────────────

        /// <summary>
        /// 숫자키 입력으로 능력 2/3에 사용할 센트리를 선택합니다.
        /// Escape로 취소 가능합니다.
        /// </summary>
        private void HandleSentrySelection()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                _isSelectingTarget = false;
                _pendingAbilityNumber = 0;
                Debug.Log("[능력] 선택 취소");
                return;
            }

            SentryBase selected = null;

            if (Input.GetKeyDown(_sentrySelectKeys[0])) selected = _strikeSentry;
            else if (Input.GetKeyDown(_sentrySelectKeys[1])) selected = _shootSentry;
            else if (Input.GetKeyDown(_sentrySelectKeys[2])) selected = _wallSentry;

            if (selected == null) return;

            _isSelectingTarget = false;

            if (_pendingAbilityNumber == 2) ExecuteRepair(selected);
            else if (_pendingAbilityNumber == 3) ExecuteOverload(selected);

            _pendingAbilityNumber = 0;
        }

        /// <summary>능력 2 [긴급 수리] 실행. KO 상태면 쿨타임 소모 없이 반환.</summary>
        private void ExecuteRepair(SentryBase sentry)
        {
            if (sentry.IsKnockedOut)
            {
                Debug.Log($"[능력2] {sentry.SentryName}은 KO 상태 — 쉼터에서만 부활 가능");
                return;
            }

            sentry.Heal(_repairHealAmount);
            _ability2Timer = _ability2Cooldown;
            sentry.transform.DOPunchScale(Vector3.one * 0.2f, 0.3f, 5, 0.5f);

            Debug.Log($"<color=lime>[능력2] {sentry.SentryName} 긴급 수리 +{_repairHealAmount} HP</color>");
        }

        /// <summary>능력 3 [과부화] 실행. KO 또는 이미 과부화 중이면 반환.</summary>
        private void ExecuteOverload(SentryBase sentry)
        {
            if (sentry.IsKnockedOut)
            {
                Debug.Log($"[능력3] {sentry.SentryName}은 KO 상태 — 과부화 불가");
                return;
            }

            if (sentry.IsOverloaded)
            {
                Debug.Log($"[능력3] {sentry.SentryName}은 이미 과부화 중");
                return;
            }

            _ability3Timer = _ability3Cooldown;
            StartCoroutine(OverloadRoutine(sentry));
        }

        /// <summary>
        /// 과부화 코루틴.
        /// _overloadDuration 초 동안 폭주 상태를 유지한 뒤 강제 기절시킵니다.
        /// </summary>
        private IEnumerator OverloadRoutine(SentryBase sentry)
        {
            sentry.SetOverload(true, _overloadDamageMultiplier, _overloadSpeedMultiplier);
            Debug.Log($"<color=red>[능력3] {sentry.SentryName} 과부화 시작!</color>");

            yield return new WaitForSeconds(_overloadDuration);

            sentry.SetOverload(false, 1f, 1f);
            sentry.ForceKnockOut();
            Debug.Log($"[능력3] {sentry.SentryName} 과부화 종료 → 기절");
        }

        // ─────────────────────────────────────────
        //  적 탐색 헬퍼
        // ─────────────────────────────────────────

        /// <summary>지정 위치 주변 radius 안의 가장 가까운 적을 반환합니다.</summary>
        private Enemy FindEnemyNearPosition(Vector2 pos, float radius)
        {
            Enemy[] all = FindObjectsByType<Enemy>(FindObjectsSortMode.None);
            Enemy best = null;
            float min = radius;

            foreach (var e in all)
            {
                float d = Vector2.Distance(pos, e.transform.position);
                if (d < min) { min = d; best = e; }
            }

            return best;
        }

        /// <summary>씬 내 가장 가까운 적을 반환합니다.</summary>
        private Enemy FindNearestEnemy()
        {
            Enemy[] all = FindObjectsByType<Enemy>(FindObjectsSortMode.None);
            Enemy best = null;
            float min = float.MaxValue;

            foreach (var e in all)
            {
                float d = Vector2.Distance(transform.position, e.transform.position);
                if (d < min) { min = d; best = e; }
            }

            return best;
        }
    }
}