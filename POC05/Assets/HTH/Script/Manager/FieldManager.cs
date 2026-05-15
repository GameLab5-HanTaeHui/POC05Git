using DG.Tweening;
using System.Collections;
using Unity.Cinemachine;
using UnityEngine;

namespace SENTRY
{
    /// <summary>
    /// 탐색 필드(2D 사이드뷰) ↔ 배틀 필드(2.5D 쿼터뷰) 전환을 관리하는 싱글턴 매니저.
    ///
    /// [새로운 배틀 진입 흐름]
    ///
    ///   EnterBattleSequence()
    ///     1. PlayerController 비활성화
    ///     2. 페이드 아웃 (FadeTo 1) — 검은화면 완료까지 대기
    ///     3. 블랙아웃 중:
    ///        - ExplorationFieldRoot OFF / BattleFieldRoot ON
    ///        - 카메라 쿼터뷰 전환
    ///        - 플레이어 → BattlePlayerSpawnPoint 순간이동
    ///        - 센트리 → 하늘 위 대기 위치로 순간이동
    ///     4. 페이드 인 (FadeTo 0) — 화면 밝아짐
    ///     5. 센트리 낙하 DOMove 연출 (순서대로, 완료 대기)
    ///     6. 적 소환 낙하 연출 (EnemySpawner.SpawnWithDropEffect)
    ///     7. 낙하 완료 → BattleUIManager.ShowEncounterPanel() 선택창 표시
    ///        - [전투하기] → 3초 카운트다운 → BattleManager.StartBattle()
    ///        - [도망가기] → ReturnToField() → 탐색 필드 복귀
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
        [Tooltip("배틀 중 플레이어가 이동할 관전 위치")]
        [SerializeField] private Transform _battlePlayerSpawnPoint;

        [Header("센트리 배틀 배치 위치")]
        [Tooltip("[0]=Strike / [1]=Shoot / [2]=Wall\n배틀 시작 시 센트리가 이 위치 위에서 낙하합니다.")]
        [SerializeField] private Transform[] _sentryBattleSpawnPoints;

        [Tooltip("센트리가 낙하를 시작하는 하늘 위 높이 오프셋 (Y축)")]
        [SerializeField] private float _sentryDropHeight = 8f;

        [Tooltip("센트리 낙하 소요 시간 (초)")]
        [SerializeField] private float _sentryDropDuration = 0.6f;

        [Tooltip("센트리 낙하 사이 간격 (초)")]
        [SerializeField] private float _sentryDropStagger = 0.2f;

        [Header("전환 연출")]
        [SerializeField] private float _fadeDuration = 0.5f;
        [SerializeField] private float _blackoutHoldDuration = 0.6f;
        [SerializeField] private CanvasGroup _fadePanel;

        [Header("카운트다운 설정")]
        [Tooltip("전투 시작 전 카운트다운 시간 (초)")]
        [SerializeField] private float _countdownDuration = 3f;

        [Header("플레이어 컨트롤러")]
        [Tooltip("미연결 시 Start()에서 Player 태그로 자동 탐색합니다.")]
        [SerializeField] private PlayerController _playerController;

        // ─────────────────────────────────────────
        //  내부 상태 변수
        // ─────────────────────────────────────────

        private bool _isInBattle = false;
        private Transform _playerTransform;
        private Vector3 _savedPlayerPosition;
        private Vector3[] _savedSentryPositions = new Vector3[3];

        /// <summary>현재 배틀 시퀀스에서 사용 중인 인카운터 데이터</summary>
        private BattleEncounterDataSO _pendingEncounterData;

        /// <summary>현재 배틀을 시작한 BattleTrigger (도망 시 콜백용)</summary>
        private BattleTrigger _currentTrigger;

        /// <summary>현재 배틀 필드의 센트리 Transform 캐시</summary>
        private Transform[] _sentryCached = new Transform[3];

        // ─────────────────────────────────────────
        //  외부 공개 프로퍼티
        // ─────────────────────────────────────────

        public bool IsInBattle => _isInBattle;

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
            var pObj = GameObject.FindGameObjectWithTag("Player");
            if (pObj != null)
            {
                _playerTransform = pObj.transform;
                if (_playerController == null)
                    _playerController = pObj.GetComponent<PlayerController>();
            }
        }

        // ─────────────────────────────────────────
        //  카메라 전환
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
        //  배틀 진입 시퀀스 (새로운 흐름)
        // ─────────────────────────────────────────

        /// <summary>
        /// BattleTrigger.OnTriggerEnter2D에서 호출합니다.
        /// 모든 배틀 진입 연출과 선택창을 이 메서드가 전담합니다.
        /// </summary>
        public void EnterBattleSequence(
            Transform player,
            BattleEncounterDataSO encounterData,
            Transform strike, Transform shoot, Transform wall)
        {
            if (_isInBattle) return;

            _playerTransform = player;
            _pendingEncounterData = encounterData;
            _isInBattle = true;
            _sentryCached[0] = strike;
            _sentryCached[1] = shoot;
            _sentryCached[2] = wall;

            StartCoroutine(BattleSequenceRoutine());
        }

        private IEnumerator BattleSequenceRoutine()
        {
            // ── 1. 플레이어 입력 차단 ──
            SetPlayerControllerActive(false);

            // ── 2. 페이드 아웃 — 검은화면 완전히 될 때까지 대기 ──
            yield return FadeTo(1f);

            // ── 3. 블랙아웃 중 전환 ──
            if (_explorationFieldRoot != null) _explorationFieldRoot.SetActive(false);
            if (_battleFieldRoot != null) _battleFieldRoot.SetActive(true);

            SetCameraToBattle();

            // 플레이어 순간이동
            if (_playerTransform != null && _battlePlayerSpawnPoint != null)
                _playerTransform.position = _battlePlayerSpawnPoint.position;

            // 센트리 → 스폰 위치 위 하늘로 순간이동 (낙하 준비)
            // SetupForBattle은 여기서 호출 — _isInBattleField = true (Y 보정 시작)
            SetupSentriesForDrop();

            // 블랙아웃 유지 (카메라 블렌드 완료 대기)
            yield return new WaitForSeconds(_blackoutHoldDuration);

            // ── 4. 페이드 인 — 화면 밝아짐 ──
            // 이 시점부터 플레이어는 배틀 필드를 보게 됩니다.
            BattleUIManager.Instance?.SetBattleHudActive(true);
            yield return FadeTo(0f);

            // ── 5. 센트리 낙하 연출 ──
            yield return DropSentriesRoutine();

            // ── 6. 적 소환 낙하 연출 ──
            var spawner = FindFirstObjectByType<EnemySpawner>();
            if (spawner != null)
                yield return spawner.SpawnWithDropEffect(_pendingEncounterData);

            // ── 7. 선택창 표시 (배틀 필드에서) ──
            BattleUIManager.Instance?.ShowEncounterPanel(_pendingEncounterData, null);

            // 이후 흐름:
            // [전투하기] → OnFightChosen() → CountdownAndStart()
            // [도망가기] → OnFleeChosen()  → ReturnToField()
        }

        // ─────────────────────────────────────────
        //  센트리 낙하 준비
        // ─────────────────────────────────────────

        /// <summary>
        /// 센트리를 배틀 스폰 위치 위 하늘에 배치합니다.
        /// 페이드 아웃 중(화면이 검을 때) 실행됩니다.
        /// </summary>
        private void SetupSentriesForDrop()
        {
            if (_sentryBattleSpawnPoints == null) return;

            for (int i = 0; i < 3; i++)
            {
                if (_sentryCached[i] == null) continue;
                if (i >= _sentryBattleSpawnPoints.Length) continue;

                // 배틀 물리 전환 (중력 OFF + Kinematic)
                SentryBase sentry = _sentryCached[i].GetComponent<SentryBase>();
                sentry?.SetupForBattle(_playerTransform);
                sentry?.EnterBattlePhysics();

                // 스폰 위치 위 하늘로 순간이동
                Vector3 dropStart = _sentryBattleSpawnPoints[i].position
                                    + Vector3.up * _sentryDropHeight;
                _sentryCached[i].position = dropStart;
            }
        }

        // ─────────────────────────────────────────
        //  센트리 낙하 연출
        // ─────────────────────────────────────────

        /// <summary>
        /// 센트리를 순서대로 스폰 위치로 낙하시킵니다.
        /// Strike → Shoot → Wall 순서로 _sentryDropStagger 간격을 두고 낙하합니다.
        /// 마지막 센트리 착지까지 대기 후 반환합니다.
        /// </summary>
        private IEnumerator DropSentriesRoutine()
        {
            if (_sentryBattleSpawnPoints == null) yield break;

            float lastDropTime = 0f;

            for (int i = 0; i < 3; i++)
            {
                if (_sentryCached[i] == null) continue;
                if (i >= _sentryBattleSpawnPoints.Length) continue;

                Vector3 landPos = _sentryBattleSpawnPoints[i].position;

                // 순서대로 낙하 시작 (stagger 간격)
                _sentryCached[i]
                    .DOMove(landPos, _sentryDropDuration)
                    .SetEase(Ease.InQuad)
                    .SetDelay(i * _sentryDropStagger);

                lastDropTime = _sentryDropDuration + i * _sentryDropStagger;

                // 착지 시 충격 연출 (Punch)
                _sentryCached[i].DOPunchScale(
                    new Vector3(0.3f, -0.3f, 0f),
                    0.3f, 5, 0.5f)
                    .SetDelay(lastDropTime);
            }

            // 마지막 센트리 착지 완료까지 대기
            yield return new WaitForSeconds(lastDropTime + 0.3f);
        }

        // ─────────────────────────────────────────
        //  선택창 결과 콜백
        // ─────────────────────────────────────────

        /// <summary>
        /// [전투하기] 버튼 클릭 시 BattleUIManager가 호출합니다.
        /// 3초 카운트다운 후 전투를 시작합니다.
        /// </summary>
        public void OnFightChosen()
        {
            StartCoroutine(CountdownAndStartBattle());
        }

        private IEnumerator CountdownAndStartBattle()
        {
            // 카운트다운 UI 표시 (BattleUIManager가 전담)
            yield return BattleUIManager.Instance?.PlayCountdown(_countdownDuration);

            // 전투 시작
            if (BattleManager.Instance != null && _pendingEncounterData != null)
                BattleManager.Instance.StartBattle(_playerTransform, _pendingEncounterData);

            Debug.Log("[FieldManager] 배틀 시작!");
        }

        /// <summary>
        /// [도망가기] 버튼 클릭 시 BattleUIManager가 호출합니다.
        /// 탐색 필드로 복귀합니다.
        /// </summary>
        public void OnFleeChosen()
        {
            StartCoroutine(FleeRoutine());
        }

        private IEnumerator FleeRoutine()
        {
            BattleUIManager.Instance?.HideEncounterPanel();
            BattleUIManager.Instance?.SetBattleHudActive(false);

            yield return FadeTo(1f);

            // 배틀 필드 → 탐색 필드 전환
            if (_battleFieldRoot != null) _battleFieldRoot.SetActive(false);
            if (_explorationFieldRoot != null) _explorationFieldRoot.SetActive(true);
            SetCameraToExploration();

            // 위치 복귀
            if (_playerTransform != null) _playerTransform.position = _savedPlayerPosition;
            for (int i = 0; i < 3; i++)
            {
                if (_sentryCached[i] != null)
                    _sentryCached[i].position = _savedSentryPositions[i];
            }

            // 센트리 물리 복원
            for (int i = 0; i < 3; i++)
            {
                SentryBase s = _sentryCached[i]?.GetComponent<SentryBase>();
                s?.ExitBattlePhysics();
                s?.StartFollowing();
            }

            yield return new WaitForSeconds(_blackoutHoldDuration);
            yield return FadeTo(0f);

            _isInBattle = false;
            SetPlayerControllerActive(true);

            Debug.Log("[FieldManager] 도망 — 탐색 필드 복귀 완료");
        }

        // ─────────────────────────────────────────
        //  탐색 필드 복귀 (전투 종료 후)
        // ─────────────────────────────────────────

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
            BattleUIManager.Instance?.SetBattleHudActive(false);

            yield return FadeTo(1f);

            if (_battleFieldRoot != null) _battleFieldRoot.SetActive(false);
            if (_explorationFieldRoot != null) _explorationFieldRoot.SetActive(true);
            SetCameraToExploration();

            if (_playerTransform != null) _playerTransform.position = _savedPlayerPosition;
            if (strike != null) strike.position = _savedSentryPositions[0];
            if (shoot != null) shoot.position = _savedSentryPositions[1];
            if (wall != null) wall.position = _savedSentryPositions[2];

            yield return new WaitForSeconds(_blackoutHoldDuration);
            yield return FadeTo(0f);

            SetPlayerControllerActive(true);

            Debug.Log("[FieldManager] 전투 종료 — 탐색 필드 복귀 완료");
        }

        // ─────────────────────────────────────────
        //  헬퍼
        // ─────────────────────────────────────────

        private void SetPlayerControllerActive(bool active)
        {
            if (_playerController != null)
                _playerController.enabled = active;
        }

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