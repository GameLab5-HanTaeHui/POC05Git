using UnityEngine;
using System.Collections;
using DG.Tweening;

namespace SENTRY
{
    /// <summary>
    /// 플레이어가 사용하는 3가지 특수 능력을 관리하는 컴포넌트.
    ///
    /// [설계 의도]
    /// - 플레이어는 직접 전투하지 않고 센트리를 보조하는 역할입니다.
    /// - 각 능력에는 초기 대기시간(게임 시작 직후 바로 사용 불가)과
    ///   개별 쿨타임이 존재합니다.
    /// - 능력 1 [목표 지정]: 클릭한 적을 우선 공격 대상으로 설정합니다.
    ///   모든 살아있는 센트리가 해당 적을 우선 타겟으로 삼습니다.
    /// - 능력 2 [긴급 수리]: 선택한 센트리 1기의 HP를 일부 회복시킵니다.
    ///   기절(KO) 상태인 센트리는 수리 불가 (쉼터에서만 부활 가능).
    /// - 능력 3 [과부화]: 선택한 센트리 1기를 폭주 상태로 만듭니다.
    ///   폭주 중 공격력/속도가 대폭 상승하지만, 폭주 종료 후 기절 상태 돌입.
    ///
    /// [히어라키 위치]
    /// Player
    ///   ├── PlayerController
    ///   ├── PlayerHealth
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
        [Tooltip("능력 1 초기 대기 시간 (초). 게임 시작 후 이 시간이 지나야 사용 가능.")]
        [SerializeField] private float _ability1InitialDelay = 10f;

        [Tooltip("능력 1 쿨타임 (초)")]
        [SerializeField] private float _ability1Cooldown = 15f;

        [Tooltip("목표 지정 유지 시간 (초). 이 시간 후 지정이 해제됩니다.")]
        [SerializeField] private float _targetMarkDuration = 8f;

        [Tooltip("목표 지정 시 적 위에 표시할 마커 프리팹 (없으면 생략)")]
        [SerializeField] private GameObject _targetMarkerPrefab;

        [Header("능력 2 — 긴급 수리")]
        [Tooltip("능력 2 초기 대기 시간 (초)")]
        [SerializeField] private float _ability2InitialDelay = 15f;

        [Tooltip("능력 2 쿨타임 (초)")]
        [SerializeField] private float _ability2Cooldown = 20f;

        [Tooltip("긴급 수리로 회복할 HP 양")]
        [SerializeField] private int _repairHealAmount = 50;

        [Header("능력 3 — 과부화")]
        [Tooltip("능력 3 초기 대기 시간 (초)")]
        [SerializeField] private float _ability3InitialDelay = 20f;

        [Tooltip("능력 3 쿨타임 (초)")]
        [SerializeField] private float _ability3Cooldown = 40f;

        [Tooltip("폭주 지속 시간 (초). 이 시간 후 해당 센트리가 기절합니다.")]
        [SerializeField] private float _overloadDuration = 6f;

        [Tooltip("폭주 중 공격력 배율")]
        [SerializeField] private float _overloadDamageMultiplier = 2.5f;

        [Tooltip("폭주 중 이동 속도 배율")]
        [SerializeField] private float _overloadSpeedMultiplier = 1.8f;

        [Header("키 바인딩")]
        [Tooltip("능력 1 키 (기본: Q)")]
        [SerializeField] private KeyCode _ability1Key = KeyCode.Q;

        [Tooltip("능력 2 키 (기본: E)")]
        [SerializeField] private KeyCode _ability2Key = KeyCode.E;

        [Tooltip("능력 3 키 (기본: R)")]
        [SerializeField] private KeyCode _ability3Key = KeyCode.R;

        [Tooltip("능력 사용 시 센트리 선택 키 [0]=Strike [1]=Shoot [2]=Wall")]
        [SerializeField]
        private KeyCode[] _sentrySelectKeys =
        {
        KeyCode.Alpha1,
        KeyCode.Alpha2,
        KeyCode.Alpha3
    };

        // ─────────────────────────────────────────
        //  내부 상태 변수
        // ─────────────────────────────────────────

        /// <summary>능력 1 남은 쿨타임 (0이면 사용 가능)</summary>
        private float _ability1Timer = 0f;

        /// <summary>능력 2 남은 쿨타임 (0이면 사용 가능)</summary>
        private float _ability2Timer = 0f;

        /// <summary>능력 3 남은 쿨타임 (0이면 사용 가능)</summary>
        private float _ability3Timer = 0f;

        /// <summary>현재 목표 지정된 적</summary>
        private Enemy _designatedTarget = null;

        /// <summary>목표 지정 마커 오브젝트 (동적 생성)</summary>
        private GameObject _activeMarker = null;

        /// <summary>능력 2/3 사용 시 선택 대기 중인지 여부</summary>
        private bool _isSelectingTarget = false;

        /// <summary>현재 선택 대기 중인 능력 번호 (2 또는 3)</summary>
        private int _pendingAbilityNumber = 0;

        // ─────────────────────────────────────────
        //  외부 공개 프로퍼티 (UI 표시용)
        // ─────────────────────────────────────────

        /// <summary>능력 1 쿨타임 비율 0~1 (1 = 사용 가능)</summary>
        public float Ability1CooldownRatio =>
            _ability1Cooldown > 0f ? Mathf.Clamp01(1f - (_ability1Timer / _ability1Cooldown)) : 1f;

        /// <summary>능력 2 쿨타임 비율 0~1</summary>
        public float Ability2CooldownRatio =>
            _ability2Cooldown > 0f ? Mathf.Clamp01(1f - (_ability2Timer / _ability2Cooldown)) : 1f;

        /// <summary>능력 3 쿨타임 비율 0~1</summary>
        public float Ability3CooldownRatio =>
            _ability3Cooldown > 0f ? Mathf.Clamp01(1f - (_ability3Timer / _ability3Cooldown)) : 1f;

        /// <summary>현재 지정된 목표 적 (없으면 null, StrikeSentry 등에서 참조)</summary>
        public Enemy DesignatedTarget => _designatedTarget;

        // ─────────────────────────────────────────
        //  유니티 생명주기
        // ─────────────────────────────────────────

        private void Start()
        {
            // 초기 대기 시간을 쿨타임 타이머에 설정 (시작 직후 사용 불가)
            _ability1Timer = _ability1InitialDelay;
            _ability2Timer = _ability2InitialDelay;
            _ability3Timer = _ability3InitialDelay;
        }

        private void Update()
        {
            // 쿨타임 카운트다운
            if (_ability1Timer > 0f) _ability1Timer -= Time.deltaTime;
            if (_ability2Timer > 0f) _ability2Timer -= Time.deltaTime;
            if (_ability3Timer > 0f) _ability3Timer -= Time.deltaTime;

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
        /// </summary>
        private void HandleAbilityInput()
        {
            // 이미 센트리 선택 대기 중이면 능력 키 입력 무시
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
            Enemy target = FindEnemyNearPosition(mouseWorld, 2f);

            if (target == null)
            {
                // 마우스 근처에 적이 없으면 씬 전체에서 가장 가까운 적 선택
                target = FindNearestEnemy();
            }

            if (target == null)
            {
                Debug.Log("[능력1] 지정할 적이 없습니다.");
                return;
            }

            // 기존 마커 제거
            ClearTargetMarker();

            _designatedTarget = target;
            _ability1Timer = _ability1Cooldown;

            // 마커 생성
            SpawnTargetMarker(target.transform);

            // 지정 시간 후 자동 해제
            StartCoroutine(ClearDesignatedTargetAfter(_targetMarkDuration));

            Debug.Log($"<color=cyan>[능력1] 목표 지정: {target.name}</color>");
        }

        /// <summary>지정 시간이 지나면 목표 지정을 자동 해제합니다.</summary>
        private IEnumerator ClearDesignatedTargetAfter(float delay)
        {
            yield return new WaitForSeconds(delay);
            ClearTargetMarker();
            _designatedTarget = null;
            Debug.Log("[능력1] 목표 지정 해제");
        }

        /// <summary>목표 마커를 생성합니다.</summary>
        private void SpawnTargetMarker(Transform target)
        {
            if (_targetMarkerPrefab == null) return;

            _activeMarker = Instantiate(_targetMarkerPrefab, target.position, Quaternion.identity);
            _activeMarker.transform.SetParent(target); // 적을 따라다니도록 자식으로 설정

            // 마커 등장 연출
            _activeMarker.transform.localScale = Vector3.zero;
            _activeMarker.transform.DOScale(Vector3.one, 0.25f).SetEase(Ease.OutBack);

            // 마커 회전 애니메이션
            _activeMarker.transform
                .DORotate(new Vector3(0f, 0f, 360f), 1.5f, RotateMode.FastBeyond360)
                .SetLoops(-1, LoopType.Restart)
                .SetEase(Ease.Linear);
        }

        /// <summary>목표 마커를 제거합니다.</summary>
        private void ClearTargetMarker()
        {
            if (_activeMarker == null) return;
            _activeMarker.transform.DOKill();
            _activeMarker.transform.DOScale(Vector3.zero, 0.15f)
                .OnComplete(() =>
                {
                    if (_activeMarker != null) Destroy(_activeMarker);
                    _activeMarker = null;
                });
        }

        // ─────────────────────────────────────────
        //  능력 2: 긴급 수리
        // ─────────────────────────────────────────

        /// <summary>
        /// 능력 2 [긴급 수리] 사용을 시도합니다.
        /// 발동 후 숫자키(1/2/3)로 수리할 센트리를 선택합니다.
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
            Debug.Log("<color=yellow>[능력2] 긴급 수리 — 센트리 선택 (1/2/3)</color>");
        }

        // ─────────────────────────────────────────
        //  능력 3: 과부화
        // ─────────────────────────────────────────

        /// <summary>
        /// 능력 3 [과부화] 사용을 시도합니다.
        /// 발동 후 숫자키(1/2/3)로 과부화할 센트리를 선택합니다.
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
        /// Escape 또는 잘못된 키 입력 시 선택을 취소합니다.
        /// </summary>
        private void HandleSentrySelection()
        {
            // 취소
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                _isSelectingTarget = false;
                _pendingAbilityNumber = 0;
                Debug.Log("[능력] 선택 취소");
                return;
            }

            SentryBase selectedSentry = null;

            if (Input.GetKeyDown(_sentrySelectKeys[0])) selectedSentry = _strikeSentry;
            else if (Input.GetKeyDown(_sentrySelectKeys[1])) selectedSentry = _shootSentry;
            else if (Input.GetKeyDown(_sentrySelectKeys[2])) selectedSentry = _wallSentry;

            if (selectedSentry == null) return;

            _isSelectingTarget = false;

            if (_pendingAbilityNumber == 2)
                ExecuteRepair(selectedSentry);
            else if (_pendingAbilityNumber == 3)
                ExecuteOverload(selectedSentry);

            _pendingAbilityNumber = 0;
        }

        /// <summary>
        /// 능력 2 [긴급 수리] 실행. KO 상태 센트리는 수리 불가.
        /// </summary>
        private void ExecuteRepair(SentryBase sentry)
        {
            if (sentry.IsKnockedOut)
            {
                Debug.Log($"[능력2] {sentry.SentryName}은 기절 상태 — 쉼터에서만 부활 가능");
                // 쿨타임 소모 없이 반환
                return;
            }

            sentry.Heal(_repairHealAmount);
            _ability2Timer = _ability2Cooldown;

            // 수리 연출: 초록색 펄스
            sentry.transform.DOPunchScale(Vector3.one * 0.2f, 0.3f, 5, 0.5f);

            Debug.Log($"<color=lime>[능력2] {sentry.SentryName} 긴급 수리 +{_repairHealAmount} HP</color>");
        }

        /// <summary>
        /// 능력 3 [과부화] 실행. 이미 과부화 중이거나 KO 상태면 사용 불가.
        /// </summary>
        private void ExecuteOverload(SentryBase sentry)
        {
            if (sentry.IsKnockedOut)
            {
                Debug.Log($"[능력3] {sentry.SentryName}은 기절 상태 — 과부화 불가");
                return;
            }

            _ability3Timer = _ability3Cooldown;
            StartCoroutine(OverloadRoutine(sentry));
        }

        /// <summary>
        /// 과부화 코루틴. _overloadDuration 초 동안 폭주 상태를 유지한 뒤 강제 기절시킵니다.
        /// 스탯 변경은 SentryBase.SetOverload()를 통해 처리합니다.
        /// </summary>
        private IEnumerator OverloadRoutine(SentryBase sentry)
        {
            Debug.Log($"<color=red>[능력3] {sentry.SentryName} 과부화 시작!</color>");

            sentry.SetOverload(true, _overloadDamageMultiplier, _overloadSpeedMultiplier);

            // 폭주 대기
            yield return new WaitForSeconds(_overloadDuration);

            // 폭주 종료 → 강제 기절
            sentry.SetOverload(false, 1f, 1f);
            sentry.ForceKnockOut();

            Debug.Log($"<color=red>[능력3] {sentry.SentryName} 과부화 종료 → 기절</color>");
        }

        // ─────────────────────────────────────────
        //  탐색 헬퍼
        // ─────────────────────────────────────────

        /// <summary>지정 위치 근처에서 가장 가까운 적을 반환합니다.</summary>
        private Enemy FindEnemyNearPosition(Vector2 pos, float radius)
        {
            Collider2D[] hits = Physics2D.OverlapCircleAll(pos, radius);
            Enemy nearest = null;
            float minDist = float.MaxValue;

            foreach (var col in hits)
            {
                Enemy e = col.GetComponent<Enemy>();
                if (e == null) continue;

                float d = Vector2.Distance(pos, col.transform.position);
                if (d < minDist) { minDist = d; nearest = e; }
            }
            return nearest;
        }

        /// <summary>씬 전체에서 가장 가까운 적을 반환합니다.</summary>
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
    }
}