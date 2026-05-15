using UnityEngine;
using DG.Tweening;

namespace SENTRY
{
    /// <summary>
    /// 플레이어 능력 선택 시 표시되는 마커를 전담하는 싱글턴 컴포넌트.
    ///
    /// [수정 사항]
    ///
    ///   1. 월드 마커 실시간 추적
    ///      Space 확정 전까지 LateUpdate에서 대상 캐릭터 위치를 매 프레임 따라다닙니다.
    ///      기존: DOMove로 1회 이동 후 고정 → 캐릭터가 움직이면 마커가 어긋남
    ///      수정: _worldTrackTarget에 추적 대상을 저장하고 LateUpdate에서 위치 갱신
    ///
    ///   2. UI 마커 Y 오프셋 Inspector 조정
    ///      센트리 HUD가 화면 하단에 있어 슬롯 중심 기준 위치가 어긋납니다.
    ///      _sentryUIMarkerYOffset (Inspector) — W/E 능력 슬롯 기준 Y 오프셋
    ///      양수 = 슬롯 위쪽 / 음수 = 슬롯 아래쪽
    ///
    ///   3. Q 능력 UI 마커 Y 반전
    ///      적 HUD가 화면 상단에 있어 Y 오프셋 부호가 반대로 들어가야 합니다.
    ///      _enemyUIMarkerYOffset (Inspector) — Q 능력 슬롯 기준 Y 오프셋
    ///      보통 _sentryUIMarkerYOffset의 부호를 반전한 값으로 설정합니다.
    ///
    ///      PlayerAbility에서 ShowUIMarker(slot, instant, isEnemySlot) 로 구분합니다.
    ///      isEnemySlot = true  → _enemyUIMarkerYOffset 사용
    ///      isEnemySlot = false → _sentryUIMarkerYOffset 사용
    ///
    /// [마커 구성 — 총 2개]
    ///   UI 마커 (_uiMarker)     : Canvas RectTransform — HUD 슬롯 위/아래를 가리킴
    ///   월드 마커 (_worldMarker) : BattleField GameObject — 캐릭터 머리 위를 가리킴
    ///
    /// [히어라키 위치]
    /// --- Managers ---
    ///   └── AbilityMarkerManager (이 스크립트)
    /// Canvas
    ///   └── UIMarker    ← _uiMarker (기본 비활성)
    /// BattleField (or 씬 루트)
    ///   └── WorldMarker ← _worldMarker (기본 비활성)
    /// </summary>
    public class AbilityMarkerManager : MonoBehaviour
    {
        // ─────────────────────────────────────────
        //  싱글턴
        // ─────────────────────────────────────────

        public static AbilityMarkerManager Instance { get; private set; }

        // ─────────────────────────────────────────
        //  Inspector — UI 마커
        // ─────────────────────────────────────────

        [Header("UI 마커 (Canvas - Screen Space Overlay)")]
        [Tooltip("Canvas 위에 배치된 마커 RectTransform.\n" +
                 "Q 능력 → 적 슬롯 / W·E 능력 → 센트리 슬롯 위로 anchoredPosition 이동.")]
        [SerializeField] private RectTransform _uiMarker;

        [Tooltip("UI 마커 슬롯 간 이동 소요 시간 (초)")]
        [SerializeField] private float _uiMoveDuration = 0.12f;

        [Tooltip("UI 마커 이동 Ease")]
        [SerializeField] private Ease _uiMoveEase = Ease.OutQuad;

        [Header("UI 마커 Y 오프셋 (위치 보정)")]
        [Tooltip("W / E 능력 (센트리 슬롯) 마커 Y 오프셋.\n" +
                 "HUD가 하단에 있으면 양수(슬롯 위쪽)로 조정하세요.\n" +
                 "양수 = 슬롯 중심 위 / 음수 = 슬롯 중심 아래")]
        [SerializeField] private float _sentryUIMarkerYOffset = 60f;

        [Tooltip("Q 능력 (적 슬롯) 마커 Y 오프셋.\n" +
                 "적 HUD가 상단에 있으므로 보통 _sentryUIMarkerYOffset의 부호를 반전한 값으로 설정합니다.\n" +
                 "양수 = 슬롯 중심 위 / 음수 = 슬롯 중심 아래")]
        [SerializeField] private float _enemyUIMarkerYOffset = -60f;

        // ─────────────────────────────────────────
        //  Inspector — 월드 마커
        // ─────────────────────────────────────────

        [Header("월드 마커 (BattleField - 3D Space)")]
        [Tooltip("배틀 필드 3D 공간의 마커 GameObject.\n" +
                 "캐릭터 머리 위에 뜨는 화살표로 사용합니다.")]
        [SerializeField] private GameObject _worldMarker;

        [Tooltip("월드 마커 캐릭터 기준 Y 오프셋 (위쪽으로 뜨는 높이)")]
        [SerializeField] private float _worldMarkerYOffset = 1.5f;

        [Tooltip("첫 표시 시 월드 마커 이동 소요 시간 (초). 이후는 LateUpdate에서 즉시 추적.")]
        [SerializeField] private float _worldSnapDuration = 0.1f;

        [Tooltip("월드 마커 이동 Ease")]
        [SerializeField] private Ease _worldMoveEase = Ease.OutQuad;

        // ─────────────────────────────────────────
        //  Inspector — 부유 연출
        // ─────────────────────────────────────────

        [Header("상하 부유 연출")]
        [Tooltip("마커가 위아래로 부유하는 진폭 (px / units)")]
        [SerializeField] private float _floatAmplitude = 8f;

        [Tooltip("부유 1회 왕복 시간 (초)")]
        [SerializeField] private float _floatDuration = 0.6f;

        // ─────────────────────────────────────────
        //  내부 상태 변수
        // ─────────────────────────────────────────

        /// <summary>현재 UI 마커 이동 Tween</summary>
        private Tween _uiMoveTween;

        /// <summary>UI 마커 부유 Tween</summary>
        private Tween _uiFloatTween;

        /// <summary>월드 마커 부유 Tween (LateUpdate 추적 중에는 사용 안 함)</summary>
        private Tween _worldFloatTween;

        /// <summary>
        /// 월드 마커가 실시간으로 추적할 대상 Transform.
        /// Space 확정 / 취소 시 null로 초기화합니다.
        /// null이면 LateUpdate 추적을 하지 않습니다.
        /// </summary>
        private Transform _worldTrackTarget;

        /// <summary>부유 진행 시간 누적 (LateUpdate 추적 모드에서 Sin 계산용)</summary>
        private float _worldFloatTime = 0f;

        // ─────────────────────────────────────────
        //  유니티 생명주기
        // ─────────────────────────────────────────

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else { Destroy(gameObject); return; }

            if (_uiMarker != null) _uiMarker.gameObject.SetActive(false);
            if (_worldMarker != null) _worldMarker.SetActive(false);
        }

        /// <summary>
        /// 월드 마커 실시간 추적.
        /// _worldTrackTarget이 설정된 동안 매 프레임 대상 위치 + Y 오프셋으로 이동합니다.
        /// Z = 0 고정을 함께 적용해 페이크 쿼터뷰 Z 오염을 방지합니다.
        ///
        /// [부유 연출]
        /// DOTween 대신 _worldFloatTime을 누적해 Sin 값으로 Y를 미세하게 움직입니다.
        /// LateUpdate 내에서 position을 직접 설정하므로 DOTween과 충돌하지 않습니다.
        /// </summary>
        private void LateUpdate()
        {
            if (_worldMarker == null || _worldTrackTarget == null) return;
            if (!_worldMarker.activeSelf) return;

            _worldFloatTime += Time.deltaTime;
            float floatY = Mathf.Sin(_worldFloatTime / _floatDuration * Mathf.PI)
                           * _floatAmplitude;

            _worldMarker.transform.position = new Vector3(
                _worldTrackTarget.position.x,
                _worldTrackTarget.position.y + _worldMarkerYOffset + floatY,
                0f);  // Z = 0 강제 고정
        }

        // ─────────────────────────────────────────
        //  외부 공개 프로퍼티
        // ─────────────────────────────────────────

        /// <summary>현재 UI 마커가 활성화되어 있는지 여부.</summary>
        public bool IsUIMarkerVisible => _uiMarker != null && _uiMarker.gameObject.activeSelf;

        // ─────────────────────────────────────────
        //  UI 마커
        // ─────────────────────────────────────────

        /// <summary>
        /// UI 마커를 대상 슬롯의 위치로 이동하고 활성화합니다.
        ///
        /// [Y 오프셋]
        ///   isEnemySlot = false → _sentryUIMarkerYOffset (센트리 HUD, 하단)
        ///   isEnemySlot = true  → _enemyUIMarkerYOffset  (적 HUD, 상단 / 부호 반전)
        /// </summary>
        /// <param name="targetSlot">이동할 슬롯의 RectTransform</param>
        /// <param name="instant">true = 즉시 이동 (첫 표시용)</param>
        /// <param name="isEnemySlot">true = Q 능력(적 슬롯) / false = W·E 능력(센트리 슬롯)</param>
        public void ShowUIMarker(RectTransform targetSlot, bool instant = false, bool isEnemySlot = false)
        {
            if (_uiMarker == null || targetSlot == null) return;

            _uiMarker.gameObject.SetActive(true);
            _uiFloatTween?.Kill();
            _uiMoveTween?.Kill();

            // 슬롯 위치 계산 + Y 오프셋 적용
            float yOffset = isEnemySlot ? _enemyUIMarkerYOffset : _sentryUIMarkerYOffset;

            float rot = isEnemySlot ? 0f : -180f;
            Quaternion ChangeRot = new Quaternion(_uiMarker.transform.localRotation.x, _uiMarker.transform.localRotation.y, rot, 0f);

            Vector2 targetPos = GetSlotAnchoredPos(targetSlot);
            targetPos.y += yOffset * 5f;

            if (instant)
            {
                _uiMarker.anchoredPosition = targetPos;
                _uiMarker.transform.localRotation = ChangeRot;
            }
            else
            {
                _uiMoveTween = _uiMarker
                    .DOAnchorPos(targetPos, _uiMoveDuration)
                    .SetEase(_uiMoveEase);
            }

            // 이동 완료 후 부유 연출 시작
            float delay = instant ? 0f : _uiMoveDuration;
            DOVirtual.DelayedCall(delay, () =>
            {
                if (_uiMarker == null || !_uiMarker.gameObject.activeSelf) return;
                Vector2 basePos = _uiMarker.anchoredPosition;
                _uiFloatTween = _uiMarker
                    .DOAnchorPosY(basePos.y + _floatAmplitude, _floatDuration)
                    .SetEase(Ease.InOutSine)
                    .SetLoops(-1, LoopType.Yoyo);
            });
        }

        /// <summary>UI 마커를 숨깁니다.</summary>
        public void HideUIMarker()
        {
            if (_uiMarker == null) return;
            _uiMoveTween?.Kill();
            _uiFloatTween?.Kill();
            _uiMarker.gameObject.SetActive(false);
        }

        /// <summary>
        /// targetSlot의 월드 좌표를 _uiMarker 부모 기준 anchoredPosition으로 변환합니다.
        /// </summary>
        private Vector2 GetSlotAnchoredPos(RectTransform targetSlot)
        {
            if (_uiMarker.parent is not RectTransform parentRect)
                return targetSlot.anchoredPosition;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                parentRect,
                RectTransformUtility.WorldToScreenPoint(null, targetSlot.position),
                null,
                out Vector2 localPos);

            return localPos;
        }

        // ─────────────────────────────────────────
        //  월드 마커
        // ─────────────────────────────────────────

        /// <summary>
        /// 월드 마커를 대상 캐릭터 위로 이동하고
        /// Space 확정 전까지 LateUpdate에서 실시간 추적합니다.
        ///
        /// [추적 방식]
        /// _worldTrackTarget에 대상을 저장하면 LateUpdate에서 매 프레임 위치를 갱신합니다.
        /// 첫 표시 시에만 _worldSnapDuration 동안 DOMove로 부드럽게 이동합니다.
        /// 이후 LateUpdate 추적으로 전환되므로 DOMove는 즉시 종료됩니다.
        ///
        /// [Z = 0 고정]
        /// LateUpdate에서 position을 설정할 때 Z를 항상 0으로 고정합니다.
        /// </summary>
        /// <param name="target">추적할 캐릭터 Transform</param>
        /// <param name="instant">true = 즉시 이동 (슬롯 변경 시 부드러운 전환 없이)</param>
        public void ShowWorldMarker(Transform target, bool instant = false)
        {
            if (_worldMarker == null || target == null) return;

            _worldMarker.SetActive(true);
            _worldFloatTween?.Kill();
            _worldFloatTime = 0f;

            // 추적 대상 설정 — LateUpdate에서 매 프레임 추적 시작
            _worldTrackTarget = target;

            // 첫 표시 또는 슬롯 변경 시: 목표 위치로 빠르게 스냅
            if (instant)
            {
                _worldMarker.transform.position = new Vector3(
                    target.position.x,
                    target.position.y + _worldMarkerYOffset,
                    0f);
            }
            else
            {
                // 짧은 DOMove로 슬롯 전환 시 부드럽게 이동
                // LateUpdate가 즉시 오버라이드하므로 목표 위치는 근사값으로 충분
                Vector3 snapTarget = new Vector3(
                    target.position.x,
                    target.position.y + _worldMarkerYOffset,
                    0f);

                _worldMarker.transform
                    .DOMove(snapTarget, _worldSnapDuration)
                    .SetEase(_worldMoveEase)
                    .OnUpdate(() => BattlePhysicsHelper.ClampZ(_worldMarker.transform));
            }
        }

        /// <summary>월드 마커를 숨기고 추적을 멈춥니다.</summary>
        public void HideWorldMarker()
        {
            if (_worldMarker == null) return;
            _worldTrackTarget = null;   // LateUpdate 추적 중단
            _worldFloatTime = 0f;
            _worldFloatTween?.Kill();
            _worldMarker.SetActive(false);
        }

        // ─────────────────────────────────────────
        //  전체 숨김
        // ─────────────────────────────────────────

        /// <summary>
        /// UI 마커와 월드 마커를 모두 숨깁니다.
        /// 선택 취소 / 확정 / 배틀 종료 시 호출합니다.
        /// </summary>
        public void HideAll()
        {
            HideUIMarker();
            HideWorldMarker();
        }
    }
}