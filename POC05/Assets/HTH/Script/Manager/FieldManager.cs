using DG.Tweening;
using System.Collections;
using Unity.Cinemachine;
using UnityEngine;

namespace SENTRY
{
    /// <summary>
    /// 탐색 필드(2D 사이드뷰) ↔ 배틀 필드(2.5D 쿼터뷰) 전환을 관리하는 싱글턴 매니저.
    ///
    /// [버그 수정]
    ///
    /// 1. 센트리 Kinematic 전환 순서 수정
    ///    기존: SetupForBattle() → EnterBattlePhysics()
    ///          SetupForBattle() 내부에서 _isBattlePhysics = false 리셋 → Kinematic 전환 무효화
    ///    수정: EnterBattlePhysics() → SetupForBattle()
    ///          Kinematic 전환 후 SetupForBattle()이 호출되어도 _isBattlePhysics는 true 유지
    ///
    /// 2. 배틀 필드 진입 시 캐릭터 X 회전 보정 (_battleCharacterRotationX = 50f)
    ///    카메라 X Rotation -50에 맞춰 플레이어 / 센트리 / 적 오브젝트를 X 50으로 회전합니다.
    ///    탐색 필드 복귀 시 X 0으로 원상 복구합니다.
    ///
    /// [히어라키 위치]
    /// --- Managers ---
    ///   └── FieldManager (이 스크립트)
    /// </summary>
    public class FieldManager : MonoBehaviour
    {
        // ─────────────────────────────────────────
        //  싱글턴
        // ─────────────────────────────────────────

        /// <summary>씬 어디서든 FieldManager.Instance로 접근합니다.</summary>
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
        [Tooltip("[0]=Strike / [1]=Shoot / [2]=Wall\n" +
                 "배틀 시작 시 센트리가 이 위치 위에서 낙하합니다.")]
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

        [Header("쿼터뷰 캐릭터 회전 보정")]
        [Tooltip("배틀 필드 진입 시 캐릭터(플레이어/센트리/적)에 적용할 X 회전값.\n" +
                 "카메라 X Rotation이 -50이면 이 값을 50으로 설정하세요.\n" +
                 "탐색 필드 복귀 시 자동으로 0으로 복구됩니다.")]
        [SerializeField] private float _battleCharacterRotationX = -50f;

        // ─────────────────────────────────────────
        //  내부 상태 변수
        // ─────────────────────────────────────────

        private bool _isInBattle = false;
        private Transform _playerTransform;
        private Vector3 _savedPlayerPosition;
        private Vector3[] _savedSentryPositions = new Vector3[3];
        private BattleEncounterDataSO _pendingEncounterData;
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
        //  배틀 진입 시퀀스
        // ─────────────────────────────────────────

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
            // 1. 플레이어 입력 차단 + 관성 제거
            SetPlayerControllerActive(false);

            // 2. 페이드 아웃
            yield return FadeTo(1f);

            // 3. 블랙아웃 중 전환
            if (_explorationFieldRoot != null) _explorationFieldRoot.SetActive(false);
            if (_battleFieldRoot != null) _battleFieldRoot.SetActive(true);

            SetCameraToBattle();

            if (_playerTransform != null && _battlePlayerSpawnPoint != null)
                _playerTransform.position = _battlePlayerSpawnPoint.position;

            // [버그 수정 6] 플레이어 X 회전 보정 적용
            ApplyBattleRotation(_playerTransform, _battleCharacterRotationX);

            // [버그 수정 1] EnterBattlePhysics → SetupForBattle 순서로 변경
            // (기존 SetupForBattle 내 _isBattlePhysics = false 리셋이 Kinematic 무효화하던 문제 수정)
            SetupSentriesForDrop();

            yield return new WaitForSeconds(_blackoutHoldDuration);

            // 4. 페이드 인
            BattleUIManager.Instance?.SetBattleHudActive(true);
            yield return FadeTo(0f);

            // 5. 센트리 낙하 연출
            yield return DropSentriesRoutine();

            // 6. 적 소환 낙하 연출
            var spawner = FindFirstObjectByType<EnemySpawner>();
            if (spawner != null)
                yield return spawner.SpawnWithDropEffect(_pendingEncounterData);

            // 7. 선택창 표시
            BattleUIManager.Instance?.ShowEncounterPanel(_pendingEncounterData, null);
        }

        // ─────────────────────────────────────────
        //  센트리 낙하 준비
        // ─────────────────────────────────────────

        /// <summary>
        /// 센트리를 배틀 스폰 위치 위 하늘에 배치합니다.
        ///
        /// [버그 수정 1 — Kinematic 전환 순서]
        /// EnterBattlePhysics()를 먼저 호출해 Kinematic으로 전환한 뒤
        /// SetupForBattle()을 호출합니다.
        /// 기존 순서(SetupForBattle → EnterBattlePhysics)에서는
        /// SetupForBattle() 내부의 _isBattlePhysics = false 리셋이
        /// 이후 EnterBattlePhysics()를 무효화하지 않았지만,
        /// BattleManager.StartBattle()에서 다시 SetupForBattle()을 호출할 때
        /// _isBattlePhysics가 리셋되는 타이밍 문제가 발생했습니다.
        /// → 이제 EnterBattlePhysics 후 SetupForBattle 순서로 안전하게 처리합니다.
        ///
        /// [버그 수정 6 — 센트리 X 회전 보정]
        /// 배틀 필드 쿼터뷰 카메라 X -50에 맞게 센트리 X 회전을 보정합니다.
        /// </summary>
        private void SetupSentriesForDrop()
        {
            if (_sentryBattleSpawnPoints == null) return;

            for (int i = 0; i < 3; i++)
            {
                if (_sentryCached[i] == null) continue;
                if (i >= _sentryBattleSpawnPoints.Length) continue;

                SentryBase sentry = _sentryCached[i].GetComponent<SentryBase>();

                // [버그 수정 1] Kinematic 전환을 먼저 수행
                sentry?.EnterBattlePhysics();

                // Kinematic 전환 후 배틀 상태 설정
                sentry?.SetupForBattle(_playerTransform);

                // [버그 수정 6] 센트리 X 회전 보정
                ApplyBattleRotation(_sentryCached[i], _battleCharacterRotationX);

                // 하늘 위로 순간이동
                Vector3 dropStart = _sentryBattleSpawnPoints[i].position
                                    + Vector3.up * _sentryDropHeight;
                _sentryCached[i].position = dropStart;
            }
        }

        // ─────────────────────────────────────────
        //  센트리 낙하 연출
        // ─────────────────────────────────────────

        private IEnumerator DropSentriesRoutine()
        {
            if (_sentryBattleSpawnPoints == null) yield break;

            float lastDropTime = 0f;

            for (int i = 0; i < 3; i++)
            {
                if (_sentryCached[i] == null) continue;
                if (i >= _sentryBattleSpawnPoints.Length) continue;

                Vector3 landPos = _sentryBattleSpawnPoints[i].position;

                _sentryCached[i]
                    .DOMove(landPos, _sentryDropDuration)
                    .SetEase(Ease.InQuad)
                    .SetDelay(i * _sentryDropStagger);

                lastDropTime = _sentryDropDuration + i * _sentryDropStagger;

                _sentryCached[i]
                    .DOPunchScale(new Vector3(0.3f, -0.3f, 0f), 0.3f, 5, 0.5f)
                    .SetDelay(lastDropTime);
            }

            yield return new WaitForSeconds(lastDropTime + 0.3f);
        }

        // ─────────────────────────────────────────
        //  선택창 결과 콜백
        // ─────────────────────────────────────────

        public void OnFightChosen()
        {
            StartCoroutine(CountdownAndStartBattle());
        }

        private IEnumerator CountdownAndStartBattle()
        {
            if (BattleUIManager.Instance != null)
                yield return BattleUIManager.Instance.PlayCountdown(_countdownDuration);
            else
                yield return new WaitForSeconds(_countdownDuration);

            if (BattleManager.Instance != null && _pendingEncounterData != null)
                BattleManager.Instance.StartBattle(_playerTransform, _pendingEncounterData);

            Debug.Log("[FieldManager] 배틀 시작!");
        }

        public void OnFleeChosen()
        {
            StartCoroutine(FleeRoutine());
        }

        private IEnumerator FleeRoutine()
        {
            BattleUIManager.Instance?.HideEncounterPanel();
            BattleUIManager.Instance?.SetBattleHudActive(false);

            yield return FadeTo(1f);

            if (_battleFieldRoot != null) _battleFieldRoot.SetActive(false);
            if (_explorationFieldRoot != null) _explorationFieldRoot.SetActive(true);
            SetCameraToExploration();

            if (_playerTransform != null)
                _playerTransform.position = _savedPlayerPosition;

            // 플레이어 회전 복구
            ApplyBattleRotation(_playerTransform, 0f);

            for (int i = 0; i < 3; i++)
            {
                if (_sentryCached[i] != null)
                {
                    _sentryCached[i].position = _savedSentryPositions[i];

                    // 센트리 회전 복구
                    ApplyBattleRotation(_sentryCached[i], 0f);

                    SentryBase s = _sentryCached[i].GetComponent<SentryBase>();
                    s?.ExitBattlePhysics();
                    s?.StartFollowing();
                }
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

            if (_playerTransform != null)
            {
                _playerTransform.position = _savedPlayerPosition;
                // 플레이어 회전 복구
                ApplyBattleRotation(_playerTransform, 0f);
            }

            if (strike != null)
            {
                strike.position = _savedSentryPositions[0];
                ApplyBattleRotation(strike, 0f);
            }
            if (shoot != null)
            {
                shoot.position = _savedSentryPositions[1];
                ApplyBattleRotation(shoot, 0f);
            }
            if (wall != null)
            {
                wall.position = _savedSentryPositions[2];
                ApplyBattleRotation(wall, 0f);
            }

            yield return new WaitForSeconds(_blackoutHoldDuration);
            yield return FadeTo(0f);

            SetPlayerControllerActive(true);

            Debug.Log("[FieldManager] 전투 종료 — 탐색 필드 복귀 완료");
        }

        // ─────────────────────────────────────────
        //  헬퍼
        // ─────────────────────────────────────────

        /// <summary>
        /// PlayerController 활성화/비활성화 + Rigidbody2D 관성 제거.
        /// 비활성화 시 linearVelocity / angularVelocity를 강제로 0으로 초기화합니다.
        /// </summary>
        private void SetPlayerControllerActive(bool active)
        {
            if (_playerController == null) return;

            _playerController.enabled = active;

            if (!active)
            {
                Rigidbody2D rb = _playerController.GetComponent<Rigidbody2D>();
                if (rb != null)
                {
                    rb.linearVelocity = Vector2.zero;
                    rb.angularVelocity = 0f;
                }
            }
        }

        /// <summary>
        /// 쿼터뷰 카메라 기울기에 맞게 오브젝트의 X 회전을 보정합니다.
        ///
        /// [버그 수정 6]
        /// 카메라 X Rotation = -50일 때 캐릭터 X Rotation = 50으로 설정해야
        /// 화면에서 캐릭터가 서 있는 것처럼 보입니다.
        /// 탐색 필드 복귀 시 rotationX = 0으로 호출해 원상 복구합니다.
        /// </summary>
        /// <param name="target">회전을 적용할 Transform</param>
        /// <param name="rotationX">적용할 X 회전값 (배틀: _battleCharacterRotationX / 탐색: 0)</param>
        private void ApplyBattleRotation(Transform target, float rotationX)
        {
            if (target == null) return;
            Vector3 euler = target.eulerAngles;
            target.eulerAngles = new Vector3(rotationX, euler.y, euler.z);
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