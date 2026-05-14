using DG.Tweening;
using System.Collections;
using Unity.Cinemachine;
using UnityEngine;

namespace SENTRY
{
    /// <summary>
    /// 매트로배니아 탐색 필드(2D 사이드뷰)와 배틀 필드(2.5D 쿼터뷰) 간의
    /// 전환을 관리하는 싱글턴 매니저.
    ///
    /// [카메라 전환 방식 — Cinemachine]
    /// - ExplorationVirtualCamera: 탐색 필드용 2D 횡스크롤 추적 카메라
    /// - BattleVirtualCamera: 배틀 필드용 2.5D 쿼터뷰 고정 카메라
    /// - Priority 값을 조절하여 활성 Virtual Camera를 전환합니다.
    ///   높은 Priority = 활성 카메라 (Brain이 자동으로 블렌드 처리)
    ///
    /// [복귀 위치 처리]
    /// - BattleTrigger 접촉 시점의 플레이어/센트리 위치를 저장합니다.
    /// - 배틀 종료 후 저장된 위치로 복귀합니다.
    ///   (고정된 ReturnPoint 오브젝트 불필요)
    ///
    /// [히어라키 위치]
    /// --- Managers ---
    ///   └── FieldManager (이 스크립트)
    ///
    /// [씬 구성]
    /// Main Camera (CinemachineBrain 부착)
    /// ExplorationVirtualCamera (CinemachineVirtualCamera)
    ///   └── Follow: Player, LookAt: Player
    /// BattleVirtualCamera (CinemachineVirtualCamera)
    ///   └── 배틀 필드 전체를 보는 고정 쿼터뷰 위치에 배치
    /// ExplorationField (루트 오브젝트)
    /// BattleField      (루트 오브젝트, 평소 비활성화)
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
        [Tooltip("2D 사이드뷰 탐색 필드 루트.\n배틀 진입 시 비활성화됩니다.")]
        [SerializeField] private GameObject _explorationFieldRoot;

        [Tooltip("2.5D 쿼터뷰 배틀 필드 루트.\n평소 비활성화 상태입니다.")]
        [SerializeField] private GameObject _battleFieldRoot;

        [Header("Cinemachine 카메라")]
        [Tooltip("탐색 필드용 Cinemachine Virtual Camera.\n" +
                 "2D 횡스크롤 — Follow/LookAt을 Player로 설정하세요.\n" +
                 "배틀 진입 시 Priority를 낮춰 비활성화합니다.")]
        [SerializeField] private CinemachineCamera _explorationVCam;

        [Tooltip("배틀 필드용 Cinemachine Virtual Camera.\n" +
                 "2.5D 쿼터뷰 고정 앵글 — 배틀 필드 전체가 보이는 위치에 배치하세요.\n" +
                 "배틀 진입 시 Priority를 높여 활성화합니다.")]
        [SerializeField] private CinemachineCamera _battleVCam;

        [Tooltip("탐색 카메라의 기본 Priority (높을수록 우선)")]
        [SerializeField] private int _explorationCamPriority = 10;

        [Tooltip("배틀 카메라의 활성 Priority.\n" +
                 "_explorationCamPriority보다 높게 설정하세요.")]
        [SerializeField] private int _battleCamPriority = 20;

        [Header("배틀 필드 플레이어 위치")]
        [Tooltip("배틀 진입 시 플레이어가 이동할 후방 관전 위치.\n" +
                 "쿼터뷰 기준 아군 진영 뒤쪽에 배치하세요.")]
        [SerializeField] private Transform _battlePlayerSpawnPoint;

        [Header("전환 연출")]
        [Tooltip("필드 전환 시 화면을 덮는 페이드 패널 (CanvasGroup).\n" +
                 "Canvas 하위에 전체화면 검은 Image + CanvasGroup을 추가하세요.")]
        [SerializeField] private CanvasGroup _fadePanel;

        [Tooltip("페이드 인/아웃 소요 시간 (초)")]
        [SerializeField] private float _fadeDuration = 0.4f;

        // ─────────────────────────────────────────
        //  내부 상태 변수
        // ─────────────────────────────────────────

        /// <summary>현재 배틀 필드 상태 여부</summary>
        private bool _isInBattle = false;

        /// <summary>플레이어 Transform</summary>
        private Transform _playerTransform;

        /// <summary>
        /// 배틀 트리거 접촉 시점에 저장한 플레이어 위치.
        /// 배틀 종료 후 이 위치로 복귀합니다.
        /// </summary>
        private Vector3 _savedPlayerPosition;

        /// <summary>
        /// 배틀 트리거 접촉 시점에 저장한 센트리 위치 배열.
        /// [0]=Strike / [1]=Shoot / [2]=Wall
        /// 배틀 종료 후 이 위치로 복귀합니다.
        /// </summary>
        private Vector3[] _savedSentryPositions = new Vector3[3];

        // ─────────────────────────────────────────
        //  외부 공개 프로퍼티
        // ─────────────────────────────────────────

        /// <summary>현재 배틀 필드 상태 여부</summary>
        public bool IsInBattle => _isInBattle;

        /// <summary>
        /// 배틀 종료 후 플레이어가 복귀할 위치.
        /// 접촉 시점에 저장된 위치입니다.
        /// </summary>
        public Vector3 SavedPlayerPosition => _savedPlayerPosition;

        /// <summary>
        /// 배틀 종료 후 센트리들이 복귀할 위치 배열.
        /// [0]=Strike / [1]=Shoot / [2]=Wall
        /// </summary>
        public Vector3[] SavedSentryPositions => _savedSentryPositions;

        // ─────────────────────────────────────────
        //  유니티 생명주기
        // ─────────────────────────────────────────

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else { Destroy(gameObject); return; }

            // 초기 상태: 탐색 필드 활성 / 배틀 필드 비활성
            if (_explorationFieldRoot != null) _explorationFieldRoot.SetActive(true);
            if (_battleFieldRoot != null) _battleFieldRoot.SetActive(false);

            // 초기 카메라: 탐색 VCam 우선 (높은 Priority)
            SetCameraToExploration();

            if (_fadePanel != null) _fadePanel.alpha = 0f;
        }

        private void Start()
        {
            GameObject pObj = GameObject.FindGameObjectWithTag("Player");
            if (pObj != null) _playerTransform = pObj.transform;
        }

        // ─────────────────────────────────────────
        //  카메라 전환 헬퍼 (Cinemachine Priority 방식)
        // ─────────────────────────────────────────

        /// <summary>
        /// 탐색 카메라를 활성화합니다.
        /// ExplorationVCam Priority를 높이고, BattleVCam Priority를 낮춥니다.
        /// CinemachineBrain이 블렌드 전환을 자동 처리합니다.
        /// </summary>
        private void SetCameraToExploration()
        {
            if (_explorationVCam != null) _explorationVCam.Priority = _explorationCamPriority;
            if (_battleVCam != null) _battleVCam.Priority = _explorationCamPriority - 1;
        }

        /// <summary>
        /// 배틀 카메라를 활성화합니다.
        /// BattleVCam Priority를 높이고, ExplorationVCam Priority를 낮춥니다.
        /// </summary>
        private void SetCameraToBattle()
        {
            if (_battleVCam != null) _battleVCam.Priority = _battleCamPriority;
            if (_explorationVCam != null) _explorationVCam.Priority = _battleCamPriority - 1;
        }

        // ─────────────────────────────────────────
        //  위치 저장
        // ─────────────────────────────────────────

        /// <summary>
        /// 배틀 트리거 접촉 시점의 플레이어 및 센트리 위치를 저장합니다.
        /// BattleTrigger.TriggerBattle()에서 EnterBattle() 직전에 호출합니다.
        /// </summary>
        /// <param name="player">플레이어 Transform</param>
        /// <param name="strikeSentry">타격 센트리 Transform</param>
        /// <param name="shootSentry">사격 센트리 Transform</param>
        /// <param name="wallSentry">벽 센트리 Transform</param>
        public void SaveReturnPositions(
            Transform player,
            Transform strikeSentry,
            Transform shootSentry,
            Transform wallSentry)
        {
            // 플레이어 위치 저장
            if (player != null)
                _savedPlayerPosition = player.position;

            // 센트리 위치 저장 ([0]=Strike / [1]=Shoot / [2]=Wall)
            _savedSentryPositions[0] = strikeSentry != null
                ? strikeSentry.position : Vector3.zero;
            _savedSentryPositions[1] = shootSentry != null
                ? shootSentry.position : Vector3.zero;
            _savedSentryPositions[2] = wallSentry != null
                ? wallSentry.position : Vector3.zero;

            Debug.Log($"[FieldManager] 복귀 위치 저장 — " +
                      $"Player: {_savedPlayerPosition}");
        }

        // ─────────────────────────────────────────
        //  배틀 필드 진입
        // ─────────────────────────────────────────

        /// <summary>
        /// 탐색 필드(2D 사이드뷰) → 배틀 필드(2.5D 쿼터뷰)로 전환합니다.
        /// BattleTrigger가 SaveReturnPositions() 호출 후 이 메서드를 호출합니다.
        /// </summary>
        public void EnterBattle(Transform player)
        {
            if (_isInBattle) return;
            _playerTransform = player;
            _isInBattle = true;
            StartCoroutine(EnterBattleRoutine());
        }

        private IEnumerator EnterBattleRoutine()
        {
            // 1. 페이드 아웃
            yield return FadeTo(1f);

            // 2. 오브젝트 전환
            if (_explorationFieldRoot != null) _explorationFieldRoot.SetActive(false);
            if (_battleFieldRoot != null) _battleFieldRoot.SetActive(true);

            // 3. Cinemachine 카메라 전환 (탐색 → 배틀 쿼터뷰)
            SetCameraToBattle();

            // 4. 플레이어를 후방 관전 위치로 이동
            if (_playerTransform != null && _battlePlayerSpawnPoint != null)
                _playerTransform.position = _battlePlayerSpawnPoint.position;

            // 5. 페이드 인
            yield return FadeTo(0f);

            Debug.Log("[FieldManager] 배틀 필드 전환 완료 (2D → 2.5D 쿼터뷰)");
        }

        // ─────────────────────────────────────────
        //  탐색 필드 복귀
        // ─────────────────────────────────────────

        /// <summary>
        /// 배틀 필드(2.5D 쿼터뷰) → 탐색 필드(2D 사이드뷰)로 복귀합니다.
        /// BattleManager.EndBattle(isVictory=true)에서 호출합니다.
        /// 저장된 위치(SaveReturnPositions)로 플레이어/센트리를 복귀시킵니다.
        /// </summary>
        /// <param name="strikeSentry">타격 센트리 Transform</param>
        /// <param name="shootSentry">사격 센트리 Transform</param>
        /// <param name="wallSentry">벽 센트리 Transform</param>
        public void ReturnToField(
            Transform strikeSentry,
            Transform shootSentry,
            Transform wallSentry)
        {
            if (!_isInBattle) return;
            _isInBattle = false;
            StartCoroutine(ReturnToFieldRoutine(strikeSentry, shootSentry, wallSentry));
        }

        private IEnumerator ReturnToFieldRoutine(
            Transform strikeSentry,
            Transform shootSentry,
            Transform wallSentry)
        {
            // 1. 페이드 아웃
            yield return FadeTo(1f);

            // 2. 오브젝트 전환
            if (_battleFieldRoot != null) _battleFieldRoot.SetActive(false);
            if (_explorationFieldRoot != null) _explorationFieldRoot.SetActive(true);

            // 3. Cinemachine 카메라 전환 (배틀 쿼터뷰 → 탐색)
            SetCameraToExploration();

            // 4. 플레이어를 저장된 위치로 복귀
            if (_playerTransform != null)
                _playerTransform.position = _savedPlayerPosition;

            // 5. 센트리를 저장된 위치로 복귀
            if (strikeSentry != null) strikeSentry.position = _savedSentryPositions[0];
            if (shootSentry != null) shootSentry.position = _savedSentryPositions[1];
            if (wallSentry != null) wallSentry.position = _savedSentryPositions[2];

            // 6. 페이드 인
            yield return FadeTo(0f);

            Debug.Log("[FieldManager] 탐색 필드 복귀 완료 (저장 위치로 복귀)");
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