using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using DG.Tweening;

namespace SENTRY
{
    /// <summary>
    /// 배틀 UI 버튼에 IPointer 인터페이스 기반 호버/클릭 연출을 추가하는 컴포넌트.
    ///
    /// [사용 방법]
    /// BattleUIManager의 각 버튼 오브젝트에 이 스크립트를 추가하세요.
    /// Button 컴포넌트와 함께 동작하며, onClick 이벤트는 BattleUIManager에서
    /// AddListener로 구독합니다. 이 컴포넌트는 시각 연출만 담당합니다.
    ///
    /// [연출 종류]
    ///   OnPointerEnter — 마우스 올렸을 때: 스케일 확대 + 색상 강조
    ///   OnPointerExit  — 마우스 벗어났을 때: 스케일 / 색상 원상 복구
    ///   OnPointerClick — 클릭 시: 펀치 스케일 (눌림 효과)
    ///
    /// [히어라키 위치]
    /// EncounterPanel
    ///   ├── FightButton  ← Button + BattleUIButton 부착
    ///   └── FleeButton   ← Button + BattleUIButton 부착
    /// VictoryPanel
    ///   └── ConfirmButton ← Button + BattleUIButton 부착
    /// DefeatPanel
    ///   └── ReturnButton  ← Button + BattleUIButton 부착
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class BattleUIButton : MonoBehaviour,
        IPointerEnterHandler,
        IPointerExitHandler,
        IPointerClickHandler
    {
        // ─────────────────────────────────────────
        //  Inspector — 호버 연출 설정
        // ─────────────────────────────────────────

        [Header("호버 연출 (OnPointerEnter / Exit)")]
        [Tooltip("마우스 올렸을 때 확대될 스케일 배율.\n" +
                 "1.1이면 10% 확대됩니다.")]
        [SerializeField] private float _hoverScale = 1.1f;

        [Tooltip("스케일 변화 소요 시간 (초)")]
        [SerializeField] private float _hoverScaleDuration = 0.15f;

        [Tooltip("마우스 올렸을 때 버튼 이미지 색상.\n" +
                 "알파는 원본 알파를 유지합니다.")]
        [SerializeField] private Color _hoverColor = new Color(1f, 0.9f, 0.4f, 1f);

        [Tooltip("색상 변화 소요 시간 (초)")]
        [SerializeField] private float _hoverColorDuration = 0.1f;

        // ─────────────────────────────────────────
        //  Inspector — 클릭 연출 설정
        // ─────────────────────────────────────────

        [Header("클릭 연출 (OnPointerClick)")]
        [Tooltip("클릭 시 펀치 스케일 강도.\n" +
                 "0.15이면 ±15% 범위로 튀깁니다.")]
        [SerializeField] private float _clickPunchStrength = 0.15f;

        [Tooltip("펀치 스케일 소요 시간 (초)")]
        [SerializeField] private float _clickPunchDuration = 0.2f;

        [Tooltip("펀치 진동 횟수")]
        [SerializeField] private int _clickPunchVibrato = 5;

        // ─────────────────────────────────────────
        //  내부 상태 변수
        // ─────────────────────────────────────────

        /// <summary>버튼 Image 컴포넌트 캐시 (색상 변경용)</summary>
        private Image _image;

        /// <summary>버튼의 기본 색상 (원상 복구용)</summary>
        private Color _defaultColor;

        /// <summary>버튼의 기본 스케일 (원상 복구용)</summary>
        private Vector3 _defaultScale;

        /// <summary>현재 스케일 Tween (중복 실행 방지)</summary>
        private Tween _scaleTween;

        /// <summary>현재 색상 Tween (중복 실행 방지)</summary>
        private Tween _colorTween;

        // ─────────────────────────────────────────
        //  유니티 생명주기
        // ─────────────────────────────────────────

        private void Awake()
        {
            _image = GetComponent<Image>();
            _defaultScale = transform.localScale;

            if (_image != null)
                _defaultColor = _image.color;
        }

        private void OnDisable()
        {
            // 패널 비활성화 시 진행 중인 Tween을 중단하고 기본 상태로 복구합니다.
            _scaleTween?.Kill();
            _colorTween?.Kill();
            transform.localScale = _defaultScale;

            if (_image != null)
                _image.color = _defaultColor;
        }

        // ─────────────────────────────────────────
        //  IPointerEnterHandler — 호버 진입
        // ─────────────────────────────────────────

        /// <summary>
        /// 마우스 커서가 버튼 위로 올라왔을 때 호출됩니다.
        /// 스케일 확대 + 색상 강조 연출을 재생합니다.
        /// </summary>
        public void OnPointerEnter(PointerEventData eventData)
        {
            // 스케일 확대
            _scaleTween?.Kill();
            _scaleTween = transform
                .DOScale(_defaultScale * _hoverScale, _hoverScaleDuration)
                .SetEase(Ease.OutBack);

            // 색상 강조
            if (_image != null)
            {
                _colorTween?.Kill();
                _colorTween = _image
                    .DOColor(_hoverColor, _hoverColorDuration)
                    .SetEase(Ease.OutQuad);
            }
        }

        // ─────────────────────────────────────────
        //  IPointerExitHandler — 호버 이탈
        // ─────────────────────────────────────────

        /// <summary>
        /// 마우스 커서가 버튼 밖으로 벗어났을 때 호출됩니다.
        /// 스케일과 색상을 기본값으로 복구합니다.
        /// </summary>
        public void OnPointerExit(PointerEventData eventData)
        {
            // 스케일 원상 복구
            _scaleTween?.Kill();
            _scaleTween = transform
                .DOScale(_defaultScale, _hoverScaleDuration)
                .SetEase(Ease.OutQuad);

            // 색상 원상 복구
            if (_image != null)
            {
                _colorTween?.Kill();
                _colorTween = _image
                    .DOColor(_defaultColor, _hoverColorDuration)
                    .SetEase(Ease.OutQuad);
            }
        }

        // ─────────────────────────────────────────
        //  IPointerClickHandler — 클릭 연출
        // ─────────────────────────────────────────

        /// <summary>
        /// 버튼을 클릭했을 때 호출됩니다.
        /// 펀치 스케일로 눌림 효과를 재생합니다.
        /// onClick 이벤트(BattleUIManager에서 구독)는 별도로 처리됩니다.
        /// </summary>
        public void OnPointerClick(PointerEventData eventData)
        {
            // 진행 중인 스케일 Tween을 중단하고 펀치 효과 재생
            _scaleTween?.Kill();
            _scaleTween = transform
                .DOPunchScale(
                    Vector3.one * _clickPunchStrength,
                    _clickPunchDuration,
                    _clickPunchVibrato,
                    elasticity: 0.5f)
                .OnComplete(() =>
                {
                    // 펀치 종료 후 호버 상태(커서가 아직 위에 있을 수 있음)를
                    // 확인해 스케일을 올바르게 복구합니다.
                    transform.localScale = _defaultScale;
                });
        }
    }
}