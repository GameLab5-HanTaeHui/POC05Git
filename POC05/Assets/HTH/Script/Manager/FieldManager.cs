using DG.Tweening;
using System.Collections;
using Unity.Cinemachine;
using UnityEngine;

namespace SENTRY
{
    /// <summary>
    /// 탐색 필드(2D 사이드뷰) ↔ 배틀 필드(2.5D 쿼터뷰) 전환을 관리하는 싱글턴 매니저.
    ///
    /// [타이밍 문제 수정]
    ///
    /// ▶ 수정 전 문제
    ///   BattleTrigger가 FieldManager.EnterBattle()과 BattleManager.StartBattle()을
    ///   동시에 호출 → 전투가 페이드 인 도중에 시작됨
    ///   PlayerController가 살아있어 전환 중 이동 모습이 보임
    ///
    /// ▶ 수정 후 구조
    ///   BattleTrigger → FieldManager.EnterBattle(player, encounterData) 만 호출
    ///   FieldManager  → 페이드 인 완전히 끝난 후 BattleManager.StartBattle() 호출
    ///   페이드 아웃 직전 PlayerController 비활성화 → 페이드 인 후 재활성화
    ///
    /// [EnterBattleRoutine 순서]
    ///   1. PlayerController 비활성화
    ///   2. 페이드 아웃
    ///   3. 블랙아웃 중: 오브젝트 전환 + 카메라 + 플레이어 순간이동
    ///   4. 블랙아웃 유지 (_blackoutHoldDuration)
    ///   5. HUD 슬라이드 인
    ///   6. 페이드 인 ← 이 시점부터 화면 보임
    ///   7. BattleManager.StartBattle() ← 화면이 완전히 밝아진 후 전투 시작
    /// </summary>
    public class FieldManager : MonoBehaviour
    {
        public static FieldManager Instance { get; private set; }

        // ─────────────────────────────────────────
        //  Inspector 노출 필드
        // ─────────────────────────────────────────

        [Header("필드 루트 오브젝트")]
        [SerializeField] private GameObject _explorationFieldRoot;
        [SerializeField] private GameObject _battleFieldRoot;

        [Header("Cinemachine 카메라")]
        [SerializeField] private CinemachineCamera _explorationVCam;
        [SerializeField] private CinemachineCamera _battleVCam;
        [SerializeField] private int _explorationCamPriority = 10;
        [SerializeField] private int _battleCamPriority = 20;

        [Header("배틀 필드 플레이어 위치")]
        [SerializeField] private Transform _battlePlayerSpawnPoint;

        [Header("전환 연출")]
        [Tooltip("페이드 인/아웃 소요 시간 (초)")]
        [SerializeField] private float _fadeDuration = 0.5f;

        [Tooltip("블랙아웃 유지 시간 (초). 기본값 0.8초\n" +
                 "카메라 블렌드 + 오브젝트 전환이 이 시간 내에 완료됩니다.")]
        [SerializeField] private float _blackoutHoldDuration = 0.8f;

        [Tooltip("페이드 패널 CanvasGroup")]
        [SerializeField] private CanvasGroup _fadePanel;

        [Header("플레이어 컨트롤러")]
        [Tooltip("전환 중 입력 차단을 위해 비활성화할 PlayerController.\n" +
                 "미연결 시 Start()에서 Player 태그로 자동 탐색합니다.")]
        [SerializeField] private PlayerController _playerController;

        // ─────────────────────────────────────────
        //  내부 상태 변수
        // ─────────────────────────────────────────

        private bool _isInBattle = false;
        private Transform _playerTransform;
        private Vector3 _savedPlayerPosition;
        private Vector3[] _savedSentryPositions = new Vector3[3];

        /// <summary>페이드 인 완료 후 StartBattle()에 전달할 인카운터 데이터</summary>
        private BattleEncounterDataSO _pendingEncounterData;

        // ─────────────────────────────────────────
        //  외부 공개 프로퍼티
        // ─────────────────────────────────────────

        public bool IsInBattle => _isInBattle;
        public Vector3 SavedPlayerPosition => _savedPlayerPosition;
        public Vector3[] SavedSentryPositions => _savedSentryPositions;

        // ─────────────────────────────────────────
        //  유니티 생명주기
        // ─────────────────────────────────────────

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else { Destroy(gameObject); return; }

            if (_explorationFieldRoot != null) _explorationFieldRoot.SetActive(true);
            if (_battleFieldRoot != null) _battleFieldRoot.SetActive(false);
            SetCameraToExploration();
            if (_fadePanel != null) _fadePanel.alpha = 0f;
        }

        private void Start()
        {
            GameObject pObj = GameObject.FindGameObjectWithTag("Player");
            if (pObj != null)
            {
                _playerTransform = pObj.transform;
                if (_playerController == null)
                    _playerController = pObj.GetComponent<PlayerController>();
            }
        }

        // ─────────────────────────────────────────
        //  카메라 전환 헬퍼
        // ─────────────────────────────────────────

        private void SetCameraToExploration()
        {
            if (_explorationVCam != null) _explorationVCam.Priority = _explorationCamPriority;
            if (_battleVCam != null) _battleVCam.Priority = _explorationCamPriority - 1;
        }

        private void SetCameraToBattle()
        {
            if (_battleVCam != null) _battleVCam.Priority = _battleCamPriority;
            if (_explorationVCam != null) _explorationVCam.Priority = _battleCamPriority - 1;
        }

        // ─────────────────────────────────────────
        //  위치 저장
        // ─────────────────────────────────────────

        /// <summary>
        /// 배틀 트리거 접촉 시점의 위치를 저장합니다.
        /// BattleTrigger.TriggerBattle()에서 EnterBattle() 직전에 호출합니다.
        /// </summary>
        public void SaveReturnPositions(
            Transform player,
            Transform strike, Transform shoot, Transform wall)
        {
            if (player != null) _savedPlayerPosition = player.position;
            _savedSentryPositions[0] = strike != null ? strike.position : Vector3.zero;
            _savedSentryPositions[1] = shoot != null ? shoot.position : Vector3.zero;
            _savedSentryPositions[2] = wall != null ? wall.position : Vector3.zero;
        }

        // ─────────────────────────────────────────
        //  배틀 필드 진입
        // ─────────────────────────────────────────

        /// <summary>
        /// 탐색 필드 → 배틀 필드로 전환합니다.
        ///
        /// [변경] BattleTrigger에서 BattleManager.StartBattle()을 직접 호출하지 않습니다.
        ///        대신 encounterData를 파라미터로 받아 저장하고,
        ///        페이드 인이 완전히 끝난 후 FieldManager가 직접 StartBattle()을 호출합니다.
        /// </summary>
        /// <param name="player">플레이어 Transform</param>
        /// <param name="encounterData">배틀에 사용할 인카운터 구성 SO</param>
        public void EnterBattle(Transform player, BattleEncounterDataSO encounterData)
        {
            if (_isInBattle) return;
            _playerTransform = player;
            _pendingEncounterData = encounterData;
            _isInBattle = true;
            StartCoroutine(EnterBattleRoutine());
        }

        private IEnumerator EnterBattleRoutine()
        {
            // ── 1. 플레이어 컨트롤러 비활성화 ──
            // 전환 중 이동 입력이 들어오지 않게 합니다.
            SetPlayerControllerActive(false);

            // ── 2. 페이드 아웃 ──
            yield return FadeTo(1f);

            // ── 3. 블랙아웃 중 전환 (화면이 검으므로 보이지 않음) ──
            if (_explorationFieldRoot != null) _explorationFieldRoot.SetActive(false);
            if (_battleFieldRoot != null) _battleFieldRoot.SetActive(true);

            // 카메라 Priority 즉시 전환
            SetCameraToBattle();

            // 플레이어 관전 위치로 순간이동
            if (_playerTransform != null && _battlePlayerSpawnPoint != null)
                _playerTransform.position = _battlePlayerSpawnPoint.position;

            // ── 4. 블랙아웃 유지 ──
            // Cinemachine 블렌드가 완료될 때까지 대기합니다.
            yield return new WaitForSeconds(_blackoutHoldDuration);

            // ── 5. HUD 슬라이드 인 ──
            BattleUIManager.Instance?.SetBattleHudActive(true);

            // ── 6. 페이드 인 ──
            yield return FadeTo(0f);

            // ── 7. 전투 시작 ──
            // 페이드 인이 완전히 끝난 후에 전투가 시작됩니다.
            // 센트리 등장 DOMove와 적 소환이 화면이 밝아진 상태에서 진행됩니다.
            if (BattleManager.Instance != null && _pendingEncounterData != null)
                BattleManager.Instance.StartBattle(_playerTransform, _pendingEncounterData);

            Debug.Log("[FieldManager] 배틀 필드 전환 완료 — 전투 시작");
        }

        // ─────────────────────────────────────────
        //  탐색 필드 복귀
        // ─────────────────────────────────────────

        /// <summary>
        /// 배틀 필드 → 탐색 필드로 복귀합니다.
        /// BattleManager.ReturnToFieldFromResult()에서 호출합니다.
        /// </summary>
        public void ReturnToField(
            Transform strike, Transform shoot, Transform wall)
        {
            if (!_isInBattle) return;
            _isInBattle = false;
            StartCoroutine(ReturnToFieldRoutine(strike, shoot, wall));
        }

        private IEnumerator ReturnToFieldRoutine(
            Transform strike, Transform shoot, Transform wall)
        {
            // HUD 슬라이드 아웃
            BattleUIManager.Instance?.SetBattleHudActive(false);

            // 1. 페이드 아웃
            yield return FadeTo(1f);

            // 2. 블랙아웃 중 전환
            if (_battleFieldRoot != null) _battleFieldRoot.SetActive(false);
            if (_explorationFieldRoot != null) _explorationFieldRoot.SetActive(true);
            SetCameraToExploration();

            if (_playerTransform != null) _playerTransform.position = _savedPlayerPosition;
            if (strike != null) strike.position = _savedSentryPositions[0];
            if (shoot != null) shoot.position = _savedSentryPositions[1];
            if (wall != null) wall.position = _savedSentryPositions[2];

            // 3. 블랙아웃 유지
            yield return new WaitForSeconds(_blackoutHoldDuration);

            // 4. 페이드 인
            yield return FadeTo(0f);

            // 5. 플레이어 컨트롤러 재활성화
            SetPlayerControllerActive(true);

            Debug.Log("[FieldManager] 탐색 필드 복귀 완료");
        }

        // ─────────────────────────────────────────
        //  플레이어 컨트롤러 제어 헬퍼
        // ─────────────────────────────────────────

        /// <summary>
        /// 전환 중 플레이어 입력을 막기 위해 PlayerController를 활성/비활성화합니다.
        /// </summary>
        private void SetPlayerControllerActive(bool active)
        {
            if (_playerController != null)
                _playerController.enabled = active;
        }

        // ─────────────────────────────────────────
        //  페이드 헬퍼
        // ─────────────────────────────────────────

        private IEnumerator FadeTo(float targetAlpha)
        {
            if (_fadePanel == null) yield break;
            yield return _fadePanel
                .DOFade(targetAlpha, _fadeDuration)
                .SetEase(Ease.InOutSine)
                .WaitForCompletion();
        }
    }
}