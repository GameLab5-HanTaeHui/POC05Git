using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;
using System.Collections;

namespace SENTRY
{
    /// <summary>
    /// 게임 내 모든 HUD UI를 관리하는 싱글턴 매니저.
    ///
    /// [변경 사항]
    /// - PlayerHealth / 플레이어 HP 바 관련 코드 완전 제거
    /// - 센트리 HUD 패널과 플레이어 능력 패널에
    ///   배틀 진입/탐색 복귀 시 DOTween 슬라이드 인/아웃 애니메이션 추가
    ///   각 패널은 Bool(X/Y) 로 이동 방향을 독립 설정합니다.
    ///   → 카메라 외부에서 슬라이드 인 → 배틀 중 화면 내 유지 → 종료 시 슬라이드 아웃
    ///
    /// [히어라키 위치]
    /// UI Canvas (Screen Space - Overlay)
    ///   ├── SentryHUD          ← 슬라이드 패널 (센트리 3개 HP/스킬/레벨/EXP)
    ///   ├── AbilityHUD         ← 슬라이드 패널 (플레이어 능력 3개)
    ///   ├── ComboHUD           ← 항상 표시 (배틀 중)
    ///   └── BattleHUD          ← 킬 카운트 등 배틀 전용 HUD
    /// </summary>
    public class UIManager : MonoBehaviour
    {
        // ─────────────────────────────────────────
        //  싱글턴
        // ─────────────────────────────────────────

        /// <summary>씬 어디서든 UIManager.Instance로 접근합니다.</summary>
        public static UIManager Instance { get; private set; }

        // ─────────────────────────────────────────
        //  Inspector 노출 필드 — 센트리 참조
        // ─────────────────────────────────────────

        [Header("센트리 참조")]
        [Tooltip("타격 센트리 (HP/스킬/레벨 데이터 읽기용)")]
        [SerializeField] private StrikeSentry _strikeSentry;

        [Tooltip("사격 센트리")]
        [SerializeField] private ShootSentry _shootSentry;

        [Tooltip("벽 센트리")]
        [SerializeField] private WallSentry _wallSentry;

        [Header("플레이어 능력 참조")]
        [Tooltip("플레이어 특수 능력 컴포넌트")]
        [SerializeField] private PlayerAbility _playerAbility;

        // ─────────────────────────────────────────
        //  Inspector 노출 필드 — 슬라이드 패널
        // ─────────────────────────────────────────

        [Header("─ 슬라이드 패널 설정 ─")]

        [Tooltip("센트리 HUD 루트 RectTransform.\n" +
                 "이 오브젝트째로 화면 밖→안으로 슬라이드합니다.")]
        [SerializeField] private RectTransform _sentryHudPanel;

        [Tooltip("플레이어 능력 HUD 루트 RectTransform.")]
        [SerializeField] private RectTransform _abilityHudPanel;

        [Header("센트리 HUD 슬라이드 방향")]
        [Tooltip("true = X축으로 슬라이드 / false = Y축으로 슬라이드")]
        [SerializeField] private bool _sentrySlideOnX = true;

        [Tooltip("true = 양수 방향에서 진입 (오른쪽/위) / false = 음수 방향 (왼쪽/아래)")]
        [SerializeField] private bool _sentrySlideFromPositive = false;

        [Tooltip("슬라이드 이동 거리 (px). 화면 밖으로 완전히 나갈 만큼 충분히 크게 설정.")]
        [SerializeField] private float _sentrySlideDistance = 600f;

        [Header("능력 HUD 슬라이드 방향")]
        [Tooltip("true = X축으로 슬라이드 / false = Y축으로 슬라이드")]
        [SerializeField] private bool _abilitySlideOnX = false;

        [Tooltip("true = 양수 방향에서 진입 / false = 음수 방향에서 진입")]
        [SerializeField] private bool _abilitySlideFromPositive = false;

        [Tooltip("슬라이드 이동 거리 (px)")]
        [SerializeField] private float _abilitySlideDistance = 400f;

        [Header("슬라이드 애니메이션 설정")]
        [Tooltip("슬라이드 인/아웃 소요 시간 (초)")]
        [SerializeField] private float _slideDuration = 0.5f;

        [Tooltip("슬라이드 인 Ease (화면 안으로 들어올 때)")]
        [SerializeField] private Ease _slideInEase = Ease.OutBack;

        [Tooltip("슬라이드 아웃 Ease (화면 밖으로 나갈 때)")]
        [SerializeField] private Ease _slideOutEase = Ease.InBack;

        [Tooltip("두 패널 슬라이드 사이의 시간 차 (초). 순차 연출용.")]
        [SerializeField] private float _slideStagger = 0.08f;

        // ─────────────────────────────────────────
        //  Inspector 노출 필드 — 센트리 HUD
        // ─────────────────────────────────────────

        [Header("─ 센트리 HP 바 (Image - Filled) ─")]
        [SerializeField] private Image _strikeHpFill;
        [SerializeField] private Image _shootHpFill;
        [SerializeField] private Image _wallHpFill;

        [Header("센트리 HP 텍스트")]
        [SerializeField] private TMP_Text _strikeHpText;
        [SerializeField] private TMP_Text _shootHpText;
        [SerializeField] private TMP_Text _wallHpText;

        [Header("센트리 스킬 게이지 (Image - Filled)")]
        [SerializeField] private Image _strikeSkillFill;
        [SerializeField] private Image _shootSkillFill;
        [SerializeField] private Image _wallSkillFill;

        [Header("센트리 레벨 텍스트")]
        [SerializeField] private TMP_Text _strikeLevelText;
        [SerializeField] private TMP_Text _shootLevelText;
        [SerializeField] private TMP_Text _wallLevelText;

        [Header("센트리 경험치 바 (Image - Filled)")]
        [SerializeField] private Image _strikeExpFill;
        [SerializeField] private Image _shootExpFill;
        [SerializeField] private Image _wallExpFill;

        [Header("센트리 KO 아이콘")]
        [SerializeField] private GameObject _strikeKoIcon;
        [SerializeField] private GameObject _shootKoIcon;
        [SerializeField] private GameObject _wallKoIcon;

        // ─────────────────────────────────────────
        //  Inspector 노출 필드 — 능력 HUD
        // ─────────────────────────────────────────

        [Header("─ 플레이어 능력 쿨타임 (Image - Filled) ─")]
        [Tooltip("능력1(목표 지정) 쿨타임 이미지 (fillAmount: 0=쿨중 / 1=사용가능)")]
        [SerializeField] private Image _ability1CooldownFill;

        [Tooltip("능력2(긴급 수리) 쿨타임 이미지")]
        [SerializeField] private Image _ability2CooldownFill;

        [Tooltip("능력3(과부화) 쿨타임 이미지")]
        [SerializeField] private Image _ability3CooldownFill;

        [Tooltip("능력1 남은 시간 텍스트 (사용 가능 시 비활성)")]
        [SerializeField] private TMP_Text _ability1CooldownText;

        [Tooltip("능력2 남은 시간 텍스트")]
        [SerializeField] private TMP_Text _ability2CooldownText;

        [Tooltip("능력3 남은 시간 텍스트")]
        [SerializeField] private TMP_Text _ability3CooldownText;

        // ─────────────────────────────────────────
        //  Inspector 노출 필드 — 콤보 / 배틀 HUD
        // ─────────────────────────────────────────

        [Header("─ 콤보 게이지 ─")]
        [SerializeField] private Image _comboGaugeFill;
        [SerializeField] private TMP_Text _comboGaugeText;
        [SerializeField] private Image _combo2CooldownFill;
        [SerializeField] private Image _combo3CooldownFill;
        [SerializeField] private GameObject _combo2ReadyIcon;
        [SerializeField] private GameObject _combo3ReadyIcon;

        [Header("─ 배틀 HUD ─")]
        [Tooltip("킬 카운트 텍스트 (예: Kill: 3 / 10)")]
        [SerializeField] private TMP_Text _killCountText;

        [Tooltip("배틀 HUD 루트 오브젝트. 배틀 중에만 활성화됩니다.")]
        [SerializeField] private GameObject _battleHudRoot;

        [Header("UI 갱신 주기")]
        [Tooltip("센트리 HUD 갱신 주기 (초)")]
        [SerializeField] private float _sentryHudRefreshRate = 0.1f;

        // ─────────────────────────────────────────
        //  내부 상태 변수
        // ─────────────────────────────────────────

        /// <summary>센트리 HUD 패널의 화면 내 기본 앵커 위치 (슬라이드 인 목표)</summary>
        private Vector2 _sentryHudOnPos;

        /// <summary>능력 HUD 패널의 화면 내 기본 앵커 위치</summary>
        private Vector2 _abilityHudOnPos;

        /// <summary>현재 슬라이드 인/아웃 Tween (중복 실행 방지용)</summary>
        private Tween _sentryTween;
        private Tween _abilityTween;

        // ─────────────────────────────────────────
        //  유니티 생명주기
        // ─────────────────────────────────────────

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else { Destroy(gameObject); return; }
        }

        private void Start()
        {
            // 배틀 HUD 초기 비활성화
            if (_battleHudRoot != null) _battleHudRoot.SetActive(false);

            // ── 슬라이드 패널 초기 위치 저장 + 화면 밖으로 이동 ──
            if (_sentryHudPanel != null)
            {
                _sentryHudOnPos = _sentryHudPanel.anchoredPosition;
                _sentryHudPanel.anchoredPosition = GetOffscreenPos(
                    _sentryHudOnPos, _sentrySlideOnX, _sentrySlideFromPositive, _sentrySlideDistance);
            }

            if (_abilityHudPanel != null)
            {
                _abilityHudOnPos = _abilityHudPanel.anchoredPosition;
                _abilityHudPanel.anchoredPosition = GetOffscreenPos(
                    _abilityHudOnPos, _abilitySlideOnX, _abilitySlideFromPositive, _abilitySlideDistance);
            }

            // 센트리 HUD 주기적 갱신 코루틴 시작
            StartCoroutine(SentryHudRefreshRoutine());
        }

        private void Update()
        {
            // 매 프레임 갱신이 필요한 UI
            RefreshAbilityHud();
            RefreshComboHud();
        }

        // ─────────────────────────────────────────
        //  슬라이드 인 / 아웃 (핵심)
        // ─────────────────────────────────────────

        /// <summary>
        /// 배틀 진입 시 UIManager에서 호출합니다.
        /// 센트리 HUD와 능력 HUD 패널을 화면 밖→안으로 슬라이드합니다.
        /// FieldManager.EnterBattleRoutine() 페이드 인 직전에 호출하면 자연스럽습니다.
        /// </summary>
        public void SlideHudIn()
        {
            SlidePanel(_sentryHudPanel, ref _sentryTween,
                _sentryHudOnPos, _slideDuration, _slideInEase, 0f);

            SlidePanel(_abilityHudPanel, ref _abilityTween,
                _abilityHudOnPos, _slideDuration, _slideInEase, _slideStagger);
        }

        /// <summary>
        /// 배틀 종료 시 UIManager에서 호출합니다.
        /// 센트리 HUD와 능력 HUD 패널을 화면 안→밖으로 슬라이드합니다.
        /// FieldManager.ReturnToFieldRoutine() 페이드 아웃 직후 호출합니다.
        /// </summary>
        public void SlideHudOut()
        {
            Vector2 sentryOff = GetOffscreenPos(
                _sentryHudOnPos, _sentrySlideOnX, _sentrySlideFromPositive, _sentrySlideDistance);
            Vector2 abilityOff = GetOffscreenPos(
                _abilityHudOnPos, _abilitySlideOnX, _abilitySlideFromPositive, _abilitySlideDistance);

            SlidePanel(_sentryHudPanel, ref _sentryTween,
                sentryOff, _slideDuration, _slideOutEase, 0f);

            SlidePanel(_abilityHudPanel, ref _abilityTween,
                abilityOff, _slideDuration, _slideOutEase, _slideStagger);
        }

        /// <summary>
        /// 개별 패널 슬라이드 실행 헬퍼.
        /// </summary>
        /// <param name="panel">이동할 RectTransform</param>
        /// <param name="tween">현재 Tween 참조 (중단용)</param>
        /// <param name="targetPos">목표 anchoredPosition</param>
        /// <param name="duration">소요 시간</param>
        /// <param name="ease">Ease 타입</param>
        /// <param name="delay">시작 딜레이 (초)</param>
        private void SlidePanel(RectTransform panel, ref Tween tween,
            Vector2 targetPos, float duration, Ease ease, float delay)
        {
            if (panel == null) return;

            tween?.Kill();
            tween = panel
                .DOAnchorPos(targetPos, duration)
                .SetEase(ease)
                .SetDelay(delay);
        }

        /// <summary>
        /// Bool 설정을 기반으로 화면 밖 오프셋 위치를 계산합니다.
        /// </summary>
        /// <param name="onPos">화면 내 기준 위치 (anchoredPosition)</param>
        /// <param name="slideOnX">true = X축 이동 / false = Y축 이동</param>
        /// <param name="fromPositive">true = 양수 방향에서 진입 / false = 음수 방향</param>
        /// <param name="distance">이동 거리 (px)</param>
        private Vector2 GetOffscreenPos(Vector2 onPos, bool slideOnX, bool fromPositive, float distance)
        {
            float offset = fromPositive ? distance : -distance;

            return slideOnX
                ? new Vector2(onPos.x + offset, onPos.y)
                : new Vector2(onPos.x, onPos.y + offset);
        }

        // ─────────────────────────────────────────
        //  센트리 HUD (주기적 갱신)
        // ─────────────────────────────────────────

        /// <summary>
        /// _sentryHudRefreshRate 주기마다 센트리 HP / 스킬 게이지 / 레벨 / EXP를 갱신합니다.
        /// </summary>
        private IEnumerator SentryHudRefreshRoutine()
        {
            while (true)
            {
                RefreshSentryHud(_strikeSentry, _strikeHpFill, _strikeHpText,
                    _strikeSkillFill, _strikeLevelText, _strikeExpFill, _strikeKoIcon);

                RefreshSentryHud(_shootSentry, _shootHpFill, _shootHpText,
                    _shootSkillFill, _shootLevelText, _shootExpFill, _shootKoIcon);

                RefreshSentryHud(_wallSentry, _wallHpFill, _wallHpText,
                    _wallSkillFill, _wallLevelText, _wallExpFill, _wallKoIcon);

                yield return new WaitForSeconds(_sentryHudRefreshRate);
            }
        }

        /// <summary>개별 센트리의 모든 HUD 요소를 갱신합니다.</summary>
        private void RefreshSentryHud(
            SentryBase sentry,
            Image hpFill, TMP_Text hpText,
            Image skillFill,
            TMP_Text levelText,
            Image expFill,
            GameObject koIcon)
        {
            if (sentry == null) return;

            bool isKo = sentry.IsKnockedOut;

            // KO 아이콘
            if (koIcon != null) koIcon.SetActive(isKo);

            // HP 바
            if (hpFill != null)
                hpFill.fillAmount = sentry.MaxHp > 0
                    ? (float)sentry.CurrentHp / sentry.MaxHp : 0f;

            if (hpText != null)
                hpText.text = $"{sentry.CurrentHp}/{sentry.MaxHp}";

            // 스킬 게이지
            if (skillFill != null)
                skillFill.fillAmount = GetSkillRatio(sentry);

            // 레벨
            if (levelText != null)
                levelText.text = $"Lv.{sentry.CurrentLevel}";

            // 경험치 바
            if (expFill != null)
                expFill.fillAmount = sentry.RequiredExp > 0
                    ? (float)sentry.CurrentExp / sentry.RequiredExp : 1f;
        }

        /// <summary>센트리 타입에 맞게 스킬 게이지 비율을 반환합니다.</summary>
        private float GetSkillRatio(SentryBase sentry)
        {
            if (sentry is StrikeSentry s) return s.MaxSkillGauge > 0 ? s.SkillGauge / s.MaxSkillGauge : 0f;
            if (sentry is ShootSentry h) return h.MaxSkillGauge > 0 ? h.SkillGauge / h.MaxSkillGauge : 0f;
            if (sentry is WallSentry w) return w.MaxSkillGauge > 0 ? w.SkillGauge / w.MaxSkillGauge : 0f;
            return 0f;
        }

        // ─────────────────────────────────────────
        //  능력 HUD (매 프레임)
        // ─────────────────────────────────────────

        /// <summary>
        /// 플레이어 특수 능력 쿨타임을 매 프레임 갱신합니다.
        /// PlayerHealth 제거로 HP 갱신은 삭제되었습니다.
        /// </summary>
        private void RefreshAbilityHud()
        {
            if (_playerAbility == null) return;

            SetCooldownUI(_ability1CooldownFill, _ability1CooldownText,
                _playerAbility.Ability1CooldownRatio);

            SetCooldownUI(_ability2CooldownFill, _ability2CooldownText,
                _playerAbility.Ability2CooldownRatio);

            SetCooldownUI(_ability3CooldownFill, _ability3CooldownText,
                _playerAbility.Ability3CooldownRatio);
        }

        /// <summary>
        /// 쿨타임 이미지와 텍스트를 ratio(0~1)로 갱신합니다.
        /// ratio == 1 이면 사용 가능 상태로 텍스트를 숨깁니다.
        /// </summary>
        private void SetCooldownUI(Image fillImage, TMP_Text cooldownText, float ratio)
        {
            if (fillImage != null)
                fillImage.fillAmount = ratio;

            if (cooldownText == null) return;

            if (ratio >= 1f)
            {
                cooldownText.gameObject.SetActive(false);
            }
            else
            {
                cooldownText.gameObject.SetActive(true);
                // ratio가 0에 가까울수록 더 많이 남음
                // 실제 남은 초가 필요하면 PlayerAbility에 RemainTime 프로퍼티 추가 권장
                cooldownText.text = $"{(1f - ratio):P0}";
            }
        }

        // ─────────────────────────────────────────
        //  콤보 HUD (매 프레임)
        // ─────────────────────────────────────────

        /// <summary>콤보 게이지와 2/3콤보 쿨타임 아이콘을 갱신합니다.</summary>
        private void RefreshComboHud()
        {
            if (ComboManager.Instance == null) return;

            float comboRatio = ComboManager.Instance.MaxComboGauge > 0
                ? ComboManager.Instance.ComboGauge / ComboManager.Instance.MaxComboGauge
                : 0f;

            if (_comboGaugeFill != null) _comboGaugeFill.fillAmount = comboRatio;

            if (_comboGaugeText != null)
                _comboGaugeText.text =
                    $"{(int)ComboManager.Instance.ComboGauge}/{(int)ComboManager.Instance.MaxComboGauge}";

            float c2 = ComboManager.Instance.Combo2CooldownRatio;
            if (_combo2CooldownFill != null) _combo2CooldownFill.fillAmount = c2;
            if (_combo2ReadyIcon != null) _combo2ReadyIcon.SetActive(c2 >= 1f);

            float c3 = ComboManager.Instance.Combo3CooldownRatio;
            if (_combo3CooldownFill != null) _combo3CooldownFill.fillAmount = c3;
            if (_combo3ReadyIcon != null) _combo3ReadyIcon.SetActive(c3 >= 1f);
        }

        // ─────────────────────────────────────────
        //  배틀 HUD (외부 호출)
        // ─────────────────────────────────────────

        /// <summary>
        /// 배틀 시작/종료 시 BattleManager가 호출합니다.
        /// HUD 슬라이드 인/아웃도 함께 처리합니다.
        /// </summary>
        public void SetBattleHudActive(bool active)
        {
            if (_battleHudRoot != null) _battleHudRoot.SetActive(active);

            if (active) SlideHudIn();
            else SlideHudOut();
        }

        /// <summary>
        /// 킬 카운트 텍스트를 갱신합니다.
        /// BattleManager.OnEnemyDied()에서 호출합니다.
        /// </summary>
        public void UpdateKillCount(int current, int target)
        {
            if (_killCountText != null)
                _killCountText.text = $"Kill: {current} / {target}";
        }

        // ─────────────────────────────────────────
        //  레벨업 연출 (외부 호출)
        // ─────────────────────────────────────────

        /// <summary>
        /// 레벨업 시 해당 센트리의 레벨 텍스트에 펀치 스케일 연출을 재생합니다.
        /// SentryBase.LevelUp()에서 호출합니다.
        /// </summary>
        /// <param name="sentryName">레벨업한 센트리 이름</param>
        /// <param name="newLevel">새 레벨</param>
        public void PlayLevelUpEffect(string sentryName, int newLevel)
        {
            Debug.Log($"<color=yellow>[UI] {sentryName} 레벨업! → Lv.{newLevel}</color>");

            TMP_Text levelText = GetLevelText(sentryName);
            if (levelText != null)
                levelText.transform.DOPunchScale(Vector3.one * 0.4f, 0.4f, 5, 0.5f);
        }

        /// <summary>센트리 이름으로 레벨 텍스트 컴포넌트를 반환합니다.</summary>
        private TMP_Text GetLevelText(string sentryName)
        {
            if (_strikeSentry != null && _strikeSentry.SentryName == sentryName)
                return _strikeLevelText;
            if (_shootSentry != null && _shootSentry.SentryName == sentryName)
                return _shootLevelText;
            if (_wallSentry != null && _wallSentry.SentryName == sentryName)
                return _wallLevelText;
            return null;
        }
    }
}