using DG.Tweening;
using System.Collections;
using Unity.Cinemachine;
using UnityEngine;

namespace SENTRY
{
    public class FieldManager : MonoBehaviour
    {
        public static FieldManager Instance { get; private set; }

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
        [SerializeField] private float _fadeDuration = 0.5f;
        [Tooltip("블랙아웃 유지 시간(초). 카메라 전환이 이 시간 내에 끝나야 합니다. 기본 0.8")]
        [SerializeField] private float _blackoutHoldDuration = 0.8f;
        [SerializeField] private CanvasGroup _fadePanel;

        private bool _isInBattle = false;
        private Transform _playerTransform;
        private Vector3 _savedPlayerPosition;
        private Vector3[] _savedSentryPositions = new Vector3[3];

        public bool IsInBattle => _isInBattle;
        public Vector3 SavedPlayerPosition => _savedPlayerPosition;
        public Vector3[] SavedSentryPositions => _savedSentryPositions;

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
            if (pObj != null) _playerTransform = pObj.transform;
        }

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

        public void SaveReturnPositions(Transform player,
            Transform strike, Transform shoot, Transform wall)
        {
            if (player != null) _savedPlayerPosition = player.position;
            _savedSentryPositions[0] = strike != null ? strike.position : Vector3.zero;
            _savedSentryPositions[1] = shoot != null ? shoot.position : Vector3.zero;
            _savedSentryPositions[2] = wall != null ? wall.position : Vector3.zero;
        }

        public void EnterBattle(Transform player)
        {
            if (_isInBattle) return;
            _playerTransform = player;
            _isInBattle = true;
            StartCoroutine(EnterBattleRoutine());
        }

        private IEnumerator EnterBattleRoutine()
        {
            yield return FadeTo(1f);
            if (_explorationFieldRoot != null) _explorationFieldRoot.SetActive(false);
            if (_battleFieldRoot != null) _battleFieldRoot.SetActive(true);
            SetCameraToBattle();
            if (_playerTransform != null && _battlePlayerSpawnPoint != null)
                _playerTransform.position = _battlePlayerSpawnPoint.position;
            yield return new WaitForSeconds(_blackoutHoldDuration);
            UIManager.Instance?.SetBattleHudActive(true);
            yield return FadeTo(0f);
            Debug.Log("[FieldManager] 배틀 필드 전환 완료 (2D -> 2.5D 쿼터뷰)");
        }

        public void ReturnToField(Transform strike, Transform shoot, Transform wall)
        {
            if (!_isInBattle) return;
            _isInBattle = false;
            StartCoroutine(ReturnToFieldRoutine(strike, shoot, wall));
        }

        private IEnumerator ReturnToFieldRoutine(
            Transform strike, Transform shoot, Transform wall)
        {
            UIManager.Instance?.SetBattleHudActive(false);
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
            Debug.Log("[FieldManager] 탐색 필드 복귀 완료");
        }

        private IEnumerator FadeTo(float targetAlpha)
        {
            if (_fadePanel == null) yield break;
            yield return _fadePanel.DOFade(targetAlpha, _fadeDuration)
                .SetEase(Ease.InOutSine).WaitForCompletion();
        }
    }
}