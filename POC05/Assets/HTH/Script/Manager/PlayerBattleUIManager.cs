using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;
using System.Collections;

namespace SENTRY
{
    /// <summary>
    /// 플레이어 측 배틀 HUD를 전담하는 싱글턴 매니저.
    ///
    /// [담당 영역]
    /// - 센트리 3종 HP바 / 스킬 게이지 / 레벨 / EXP바 / KO 아이콘 갱신
    /// - 플레이어 능력(Ability) 쿨타임 3개 갱신
    /// - 콤보 게이지 / 콤보 쿨타임 갱신
    /// - 센트리 HUD 패널 & 능력 HUD 패널 슬라이드 인/아웃
    ///
    /// [콤보 HUD 구조]
    ///
    /// ▶ 2콤보 아이콘 — 센트리 슬롯마다 1개씩, 총 3개
    ///   각 센트리는 2콤보 조합에 1가지씩 참여합니다.
    ///     타격 센트리 슬롯 → _strike2ComboIcon
    ///     사격 센트리 슬롯 → _shoot2ComboIcon
    ///     벽 센트리 슬롯  → _wall2ComboIcon
    ///   Inspector 필드는 3개로 분리되어 있지만,
    ///   모두 SentryComboManager.Combo2CooldownRatio 1개를 읽어 동일하게 갱신됩니다.
    ///   → 게임상으로는 "내 센트리의 2콤보 준비 상태"를 각자 표시하지만
    ///     실제 쿨타임은 하나로 공유됩니다.
    ///
    /// ▶ 3콤보 아이콘 — 센트리 슬롯마다 1개씩, 총 3개
    ///   3콤보는 전원 연계 1종뿐이므로 모든 센트리가 동일한 쿨타임을 공유합니다.
    ///     타격 센트리 슬롯 → _strike3ComboIcon
    ///     사격 센트리 슬롯 → _shoot3ComboIcon
    ///     벽 센트리 슬롯  → _wall3ComboIcon
    ///   Inspector 필드는 3개로 분리되어 있지만,
    ///   모두 SentryComboManager.Combo3CooldownRatio 1개를 읽어 동일하게 갱신됩니다.
    ///
    /// [호출 관계]
    /// UIManager.SetBattleHudActive(true)  → SlideIn()
    /// UIManager.SetBattleHudActive(false) → SlideOut()
    /// UIManager.PlayLevelUpEffect()       → PlayLevelUpEffect()
    ///
    /// [히어라키 위치]
    /// --- Managers ---
    ///   └── PlayerBattleUIManager (이 스크립트)
    ///
    /// Canvas (Screen Space - Overlay)
    ///   ├── SentryHUDPanel   ← _sentryHudPanel
    ///   └── AbilityHUDPanel  ← _abilityHudPanel
    /// </summary>
    public class PlayerBattleUIManager : MonoBehaviour
    {
        // ─────────────────────────────────────────
        //  싱글턴
        // ─────────────────────────────────────────

        /// <summary>씬 어디서든 PlayerBattleUIManager.Instance로 접근합니다.</summary>
        public static PlayerBattleUIManager Instance { get; private set; }

        // ─────────────────────────────────────────
        //  Inspector — 센트리 참조
        // ─────────────────────────────────────────

        [Header("센트리 참조")]
        [Tooltip("타격 센트리 (데이터 읽기용)")]
        [SerializeField] private StrikeSentry _strikeSentry;

        [Tooltip("사격 센트리 (데이터 읽기용)")]
        [SerializeField] private ShootSentry _shootSentry;

        [Tooltip("벽 센트리 (데이터 읽기용)")]
        [SerializeField] private WallSentry _wallSentry;

        [Header("플레이어 능력 참조")]
        [Tooltip("플레이어 특수 능력 컴포넌트")]
        [SerializeField] private PlayerAbility _playerAbility;

        // ─────────────────────────────────────────
        //  Inspector — 슬라이드 패널
        // ─────────────────────────────────────────

        [Header("슬라이드 패널")]
        [Tooltip("센트리 HUD 루트 RectTransform")]
        [SerializeField] private RectTransform _sentryHudPanel;

        [Tooltip("플레이어 능력 HUD 루트 RectTransform")]
        [SerializeField] private RectTransform _abilityHudPanel;

        [Header("센트리 HUD 슬라이드 방향")]
        [Tooltip("true = X축 / false = Y축")]
        [SerializeField] private bool _sentrySlideOnX = true;

        [Tooltip("true = 양수 방향 진입 (오른쪽/위) / false = 음수 방향 (왼쪽/아래)")]
        [SerializeField] private bool _sentrySlideFromPositive = false;

        [Tooltip("슬라이드 이동 거리 (px)")]
        [SerializeField] private float _sentrySlideDistance = 600f;

        [Header("능력 HUD 슬라이드 방향")]
        [Tooltip("true = X축 / false = Y축")]
        [SerializeField] private bool _abilitySlideOnX = false;

        [Tooltip("true = 양수 방향 진입 / false = 음수 방향")]
        [SerializeField] private bool _abilitySlideFromPositive = false;

        [Tooltip("슬라이드 이동 거리 (px)")]
        [SerializeField] private float _abilitySlideDistance = 400f;

        [Header("슬라이드 애니메이션")]
        [Tooltip("슬라이드 인/아웃 소요 시간 (초)")]
        [SerializeField] private float _slideDuration = 0.5f;

        [Tooltip("슬라이드 인 Ease")]
        [SerializeField] private Ease _slideInEase = Ease.OutBack;

        [Tooltip("슬라이드 아웃 Ease")]
        [SerializeField] private Ease _slideOutEase = Ease.InBack;

        [Tooltip("두 패널 슬라이드 사이 시간 차 (초)")]
        [SerializeField] private float _slideStagger = 0.08f;

        // ─────────────────────────────────────────
        //  Inspector — 센트리 HP 바
        // ─────────────────────────────────────────

        [Header("센트리 HP 바 (Image - Filled)")]
        [SerializeField] private Image _strikeHpFill;
        [SerializeField] private Image _shootHpFill;
        [SerializeField] private Image _wallHpFill;

        [Header("센트리 HP 텍스트")]
        [SerializeField] private TMP_Text _strikeHpText;
        [SerializeField] private TMP_Text _shootHpText;
        [SerializeField] private TMP_Text _wallHpText;

        // ─────────────────────────────────────────
        //  Inspector — 센트리 스킬 / 레벨 / EXP
        // ─────────────────────────────────────────

        [Header("센트리 스킬 게이지 (Image - Filled)")]
        [SerializeField] private Image _strikeSkillFill;
        [SerializeField] private Image _shootSkillFill;
        [SerializeField] private Image _wallSkillFill;

        [Header("센트리 레벨 텍스트")]
        [SerializeField] private TMP_Text _strikeLevelText;
        [SerializeField] private TMP_Text _shootLevelText;
        [SerializeField] private TMP_Text _wallLevelText;

        [Header("센트리 EXP 바 (Image - Filled)")]
        [SerializeField] private Image _strikeExpFill;
        [SerializeField] private Image _shootExpFill;
        [SerializeField] private Image _wallExpFill;

        [Header("센트리 KO 아이콘")]
        [SerializeField] private GameObject _strikeKoIcon;
        [SerializeField] private GameObject _shootKoIcon;
        [SerializeField] private GameObject _wallKoIcon;

        // ─────────────────────────────────────────
        //  Inspector — 2콤보 아이콘 (센트리 슬롯별 1개씩 총 3개)
        // ─────────────────────────────────────────

        [Header("2콤보 쿨타임 — 센트리 슬롯별 1개씩 (총 3개)")]
        [Tooltip("타격 센트리 슬롯의 2콤보 쿨타임 바 (Image - Filled).\n" +
                 "Combo2CooldownRatio를 공유합니다.")]
        [SerializeField] private Image _strike2ComboFill;

        [Tooltip("사격 센트리 슬롯의 2콤보 쿨타임 바 (Image - Filled).\n" +
                 "Combo2CooldownRatio를 공유합니다.")]
        [SerializeField] private Image _shoot2ComboFill;

        [Tooltip("벽 센트리 슬롯의 2콤보 쿨타임 바 (Image - Filled).\n" +
                 "Combo2CooldownRatio를 공유합니다.")]
        [SerializeField] private Image _wall2ComboFill;

        [Tooltip("타격 센트리 슬롯의 2콤보 레디 아이콘.\n" +
                 "쿨타임 완료 시 활성화됩니다.")]
        [SerializeField] private GameObject _strike2ComboReadyIcon;

        [Tooltip("사격 센트리 슬롯의 2콤보 레디 아이콘.")]
        [SerializeField] private GameObject _shoot2ComboReadyIcon;

        [Tooltip("벽 센트리 슬롯의 2콤보 레디 아이콘.")]
        [SerializeField] private GameObject _wall2ComboReadyIcon;

        // ─────────────────────────────────────────
        //  Inspector — 3콤보 아이콘 (센트리 슬롯별 1개씩 총 3개)
        // ─────────────────────────────────────────

        [Header("3콤보 쿨타임 — 센트리 슬롯별 1개씩 (총 3개)")]
        [Tooltip("타격 센트리 슬롯의 3콤보 쿨타임 바 (Image - Filled).\n" +
                 "3콤보는 전원 공용이므로 3개 모두 Combo3CooldownRatio를 공유합니다.")]
        [SerializeField] private Image _strike3ComboFill;

        [Tooltip("사격 센트리 슬롯의 3콤보 쿨타임 바 (Image - Filled).")]
        [SerializeField] private Image _shoot3ComboFill;

        [Tooltip("벽 센트리 슬롯의 3콤보 쿨타임 바 (Image - Filled).")]
        [SerializeField] private Image _wall3ComboFill;

        [Tooltip("타격 센트리 슬롯의 3콤보 레디 아이콘.\n" +
                 "쿨타임 완료 + 3기 생존 시 활성화됩니다.")]
        [SerializeField] private GameObject _strike3ComboReadyIcon;

        [Tooltip("사격 센트리 슬롯의 3콤보 레디 아이콘.")]
        [SerializeField] private GameObject _shoot3ComboReadyIcon;

        [Tooltip("벽 센트리 슬롯의 3콤보 레디 아이콘.")]
        [SerializeField] private GameObject _wall3ComboReadyIcon;

        // ─────────────────────────────────────────
        //  Inspector — 공용 콤보 게이지
        // ─────────────────────────────────────────

        [Header("공용 콤보 게이지")]
        [Tooltip("공용 콤보 게이지 바 (Image - Filled).\n" +
                 "SentryComboManager.ComboGauge / MaxComboGauge 기준으로 갱신됩니다.")]
        [SerializeField] private Image _comboGaugeFill;

        [Tooltip("공용 콤보 게이지 수치 텍스트 (예: 60/100)")]
        [SerializeField] private TMP_Text _comboGaugeText;

        // ─────────────────────────────────────────
        //  Inspector — 능력 HUD
        // ─────────────────────────────────────────

        [Header("플레이어 능력 쿨타임 (Image - Filled)")]
        [Tooltip("능력1(목표 지정) 쿨타임 fillAmount (0=쿨중 / 1=사용가능)")]
        [SerializeField] private Image _ability1CooldownFill;

        [Tooltip("능력2(긴급 수리) 쿨타임")]
        [SerializeField] private Image _ability2CooldownFill;

        [Tooltip("능력3(과부화) 쿨타임")]
        [SerializeField] private Image _ability3CooldownFill;

        [Tooltip("능력1 남은 시간 텍스트")]
        [SerializeField] private TMP_Text _ability1CooldownText;

        [Tooltip("능력2 남은 시간 텍스트")]
        [SerializeField] private TMP_Text _ability2CooldownText;

        [Tooltip("능력3 남은 시간 텍스트")]
        [SerializeField] private TMP_Text _ability3CooldownText;

        // ─────────────────────────────────────────
        //  Inspector — 갱신 주기
        // ─────────────────────────────────────────

        [Header("갱신 주기")]
        [Tooltip("센트리 HUD 갱신 주기 (초)")]
        [SerializeField] private float _sentryHudRefreshRate = 0.1f;

        // ─────────────────────────────────────────
        //  내부 상태 변수
        // ─────────────────────────────────────────

        /// <summary>센트리 HUD 패널의 화면 내 기본 앵커 위치 (슬라이드 인 목표)</summary>
        private Vector2 _sentryHudOnPos;

        /// <summary>능력 HUD 패널의 화면 내 기본 앵커 위치</summary>
        private Vector2 _abilityHudOnPos;

        /// <summary>센트리 패널 현재 슬라이드 Tween (중복 실행 방지)</summary>
        private Tween _sentryTween;

        /// <summary>능력 패널 현재 슬라이드 Tween (중복 실행 방지)</summary>
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
            // 화면 내 기본 위치 저장 후 화면 밖으로 이동
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

            // 센트리 HUD 주기 갱신 코루틴 시작
            StartCoroutine(SentryHudRefreshRoutine());
        }

        private void Update()
        {
            // 매 프레임 갱신이 필요한 UI
            RefreshAbilityHud();
            RefreshComboHud();
        }

        // ─────────────────────────────────────────
        //  슬라이드 인 / 아웃
        // ─────────────────────────────────────────

        /// <summary>
        /// 배틀 진입 시 센트리 HUD와 능력 HUD를 화면 밖→안으로 슬라이드합니다.
        /// UIManager.SetBattleHudActive(true)에서 호출합니다.
        /// </summary>
        public void SlideIn()
        {
            SlidePanel(_sentryHudPanel, ref _sentryTween,
                _sentryHudOnPos, _slideDuration, _slideInEase, 0f);

            SlidePanel(_abilityHudPanel, ref _abilityTween,
                _abilityHudOnPos, _slideDuration, _slideInEase, _slideStagger);
        }

        /// <summary>
        /// 배틀 종료 시 센트리 HUD와 능력 HUD를 화면 안→밖으로 슬라이드합니다.
        /// UIManager.SetBattleHudActive(false)에서 호출합니다.
        /// </summary>
        public void SlideOut()
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

        /// <summary>개별 패널 슬라이드 실행 헬퍼.</summary>
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

        /// <summary>Bool 설정 기반으로 화면 밖 오프셋 위치를 계산합니다.</summary>
        private Vector2 GetOffscreenPos(
            Vector2 onPos, bool slideOnX, bool fromPositive, float distance)
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
                RefreshSentryHud(_strikeSentry,
                    _strikeHpFill, _strikeHpText,
                    _strikeSkillFill,
                    _strikeLevelText, _strikeExpFill,
                    _strikeKoIcon);

                RefreshSentryHud(_shootSentry,
                    _shootHpFill, _shootHpText,
                    _shootSkillFill,
                    _shootLevelText, _shootExpFill,
                    _shootKoIcon);

                RefreshSentryHud(_wallSentry,
                    _wallHpFill, _wallHpText,
                    _wallSkillFill,
                    _wallLevelText, _wallExpFill,
                    _wallKoIcon);

                yield return new WaitForSeconds(_sentryHudRefreshRate);
            }
        }

        /// <summary>개별 센트리의 모든 HUD 요소를 갱신합니다.</summary>
        private void RefreshSentryHud(
            SentryBase sentry,
            Image hpFill, TMP_Text hpText,
            Image skillFill,
            TMP_Text levelText, Image expFill,
            GameObject koIcon)
        {
            if (sentry == null) return;

            bool isKo = sentry.IsKnockedOut;

            if (koIcon != null) koIcon.SetActive(isKo);

            if (hpFill != null)
                hpFill.fillAmount = sentry.MaxHp > 0
                    ? (float)sentry.CurrentHp / sentry.MaxHp : 0f;

            if (hpText != null)
                hpText.text = $"{sentry.CurrentHp}/{sentry.MaxHp}";

            if (skillFill != null)
                skillFill.fillAmount = GetSkillRatio(sentry);

            if (levelText != null)
                levelText.text = $"Lv.{sentry.CurrentLevel}";

            if (expFill != null)
                expFill.fillAmount = sentry.RequiredExp > 0
                    ? (float)sentry.CurrentExp / sentry.RequiredExp : 1f;
        }

        /// <summary>센트리 타입에 맞게 스킬 게이지 비율을 반환합니다.</summary>
        private float GetSkillRatio(SentryBase sentry)
        {
            if (sentry is StrikeSentry s)
                return s.MaxSkillGauge > 0 ? s.SkillGauge / s.MaxSkillGauge : 0f;
            if (sentry is ShootSentry h)
                return h.MaxSkillGauge > 0 ? h.SkillGauge / h.MaxSkillGauge : 0f;
            if (sentry is WallSentry w)
                return w.MaxSkillGauge > 0 ? w.SkillGauge / w.MaxSkillGauge : 0f;
            return 0f;
        }

        // ─────────────────────────────────────────
        //  레벨업 연출 (외부 호출)
        // ─────────────────────────────────────────

        /// <summary>
        /// 레벨업 시 해당 센트리의 레벨 텍스트에 펀치 스케일 연출을 재생합니다.
        /// UIManager.PlayLevelUpEffect()를 통해 전달받아 실행됩니다.
        /// </summary>
        public void PlayLevelUpEffect(string sentryName, int newLevel)
        {
            TMP_Text levelText = GetLevelText(sentryName);
            if (levelText != null)
                levelText.transform.DOPunchScale(Vector3.one * 0.4f, 0.4f, 5, 0.5f);

            Debug.Log($"<color=yellow>[PlayerBattleUI] {sentryName} 레벨업! → Lv.{newLevel}</color>");
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

        // ─────────────────────────────────────────
        //  능력 HUD (매 프레임 갱신)
        // ─────────────────────────────────────────

        /// <summary>
        /// 플레이어 특수 능력 쿨타임을 매 프레임 갱신합니다.
        /// Update()에서 호출됩니다.
        /// </summary>
        private void RefreshAbilityHud()
        {
            if (_playerAbility == null) return;

            RefreshAbilitySlot(
                _ability1CooldownFill, _ability1CooldownText,
                _playerAbility.Ability1CooldownRatio);

            RefreshAbilitySlot(
                _ability2CooldownFill, _ability2CooldownText,
                _playerAbility.Ability2CooldownRatio);

            RefreshAbilitySlot(
                _ability3CooldownFill, _ability3CooldownText,
                _playerAbility.Ability3CooldownRatio);
        }

        /// <summary>
        /// 개별 능력 슬롯 UI를 갱신합니다.
        /// ratio가 1이면 사용 가능 상태로 텍스트를 숨깁니다.
        /// </summary>
        /// <param name="fill">쿨타임 바 Image</param>
        /// <param name="text">상태 텍스트</param>
        /// <param name="ratio">쿨타임 비율 (0=쿨중 / 1=사용가능)</param>
        private void RefreshAbilitySlot(Image fill, TMP_Text text, float ratio)
        {
            if (fill != null)
                fill.fillAmount = ratio;

            if (text != null)
            {
                bool ready = ratio >= 1f;
                text.gameObject.SetActive(!ready);
                if (!ready) text.text = $"{Mathf.RoundToInt(ratio * 100)}%";
            }
        }

        // ─────────────────────────────────────────
        //  콤보 HUD (매 프레임 갱신)
        // ─────────────────────────────────────────

        /// <summary>
        /// 공용 콤보 게이지, 2콤보 쿨타임(3개 동기), 3콤보 쿨타임(3개 동기)을
        /// 매 프레임 갱신합니다.
        /// Update()에서 호출됩니다.
        ///
        /// [2콤보 동작]
        ///   _strike2ComboFill / _shoot2ComboFill / _wall2ComboFill 3개 모두
        ///   SentryComboManager.Combo2CooldownRatio 동일한 값으로 갱신됩니다.
        ///   각 센트리 슬롯에서 "내 2콤보가 준비됐는가"를 보여주지만
        ///   실제 쿨타임은 하나를 공유합니다.
        ///
        /// [3콤보 동작]
        ///   _strike3ComboFill / _shoot3ComboFill / _wall3ComboFill 3개 모두
        ///   SentryComboManager.Combo3CooldownRatio 동일한 값으로 갱신됩니다.
        ///   3콤보는 전원 연계 1종이므로 모든 센트리가 같은 쿨타임을 공유합니다.
        /// </summary>
        private void RefreshComboHud()
        {
            if (SentryComboManager.Instance == null) return;

            // ── 공용 콤보 게이지 ──
            float comboRatio = SentryComboManager.Instance.MaxComboGauge > 0
                ? SentryComboManager.Instance.ComboGauge / SentryComboManager.Instance.MaxComboGauge
                : 0f;

            if (_comboGaugeFill != null)
                _comboGaugeFill.fillAmount = comboRatio;

            if (_comboGaugeText != null)
                _comboGaugeText.text =
                    $"{(int)SentryComboManager.Instance.ComboGauge}" +
                    $"/{(int)SentryComboManager.Instance.MaxComboGauge}";

            // ── 2콤보 쿨타임 — 3개 슬롯 동일 값으로 갱신 ──
            // Combo2CooldownRatio: 생존 조합 중 가장 준비된 조합의 비율
            float combo2Ratio = SentryComboManager.Instance.Combo2CooldownRatio;
            bool combo2Ready = combo2Ratio >= 1f;

            if (_strike2ComboFill != null) _strike2ComboFill.fillAmount = combo2Ratio;
            if (_shoot2ComboFill != null) _shoot2ComboFill.fillAmount = combo2Ratio;
            if (_wall2ComboFill != null) _wall2ComboFill.fillAmount = combo2Ratio;

            if (_strike2ComboReadyIcon != null) _strike2ComboReadyIcon.SetActive(combo2Ready);
            if (_shoot2ComboReadyIcon != null) _shoot2ComboReadyIcon.SetActive(combo2Ready);
            if (_wall2ComboReadyIcon != null) _wall2ComboReadyIcon.SetActive(combo2Ready);

            // ── 3콤보 쿨타임 — 3개 슬롯 동일 값으로 갱신 ──
            // Combo3CooldownRatio: 전원 공용 1개 쿨타임
            float combo3Ratio = SentryComboManager.Instance.Combo3CooldownRatio;
            bool combo3Ready = combo3Ratio >= 1f;

            if (_strike3ComboFill != null) _strike3ComboFill.fillAmount = combo3Ratio;
            if (_shoot3ComboFill != null) _shoot3ComboFill.fillAmount = combo3Ratio;
            if (_wall3ComboFill != null) _wall3ComboFill.fillAmount = combo3Ratio;

            if (_strike3ComboReadyIcon != null) _strike3ComboReadyIcon.SetActive(combo3Ready);
            if (_shoot3ComboReadyIcon != null) _shoot3ComboReadyIcon.SetActive(combo3Ready);
            if (_wall3ComboReadyIcon != null) _wall3ComboReadyIcon.SetActive(combo3Ready);
        }
    }
}