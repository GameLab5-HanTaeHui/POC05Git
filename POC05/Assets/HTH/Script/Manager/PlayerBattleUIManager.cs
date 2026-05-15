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
    /// [버그 수정]
    /// - Start()에 Inspector 연결 자동 검증 추가
    ///   null인 슬롯을 Console 경고로 출력하여 연결 누락을 빠르게 발견할 수 있습니다.
    /// - WallSentry SkillGauge / MaxSkillGauge 프로퍼티 public 여부 확인
    ///   (GetSkillRatio 내부 WallSentry 분기에서 접근)
    /// </summary>
    public class PlayerBattleUIManager : MonoBehaviour
    {
        public static PlayerBattleUIManager Instance { get; private set; }

        // ─────────────────────────────────────────
        //  Inspector — 센트리 참조
        // ─────────────────────────────────────────

        [Header("센트리 참조 ★ 반드시 연결 필요")]
        [SerializeField] private StrikeSentry _strikeSentry;
        [SerializeField] private ShootSentry _shootSentry;
        [SerializeField] private WallSentry _wallSentry;

        [Header("플레이어 능력 참조")]
        [SerializeField] private PlayerAbility _playerAbility;

        // ─────────────────────────────────────────
        //  Inspector — 슬라이드 패널
        // ─────────────────────────────────────────

        [Header("슬라이드 패널")]
        [SerializeField] private RectTransform _sentryHudPanel;
        [SerializeField] private RectTransform _abilityHudPanel;

        [Header("센트리 HUD 슬라이드")]
        [SerializeField] private bool _sentrySlideOnX = true;
        [SerializeField] private bool _sentrySlideFromPositive = false;
        [SerializeField] private float _sentrySlideDistance = 600f;

        [Header("능력 HUD 슬라이드")]
        [SerializeField] private bool _abilitySlideOnX = false;
        [SerializeField] private bool _abilitySlideFromPositive = false;
        [SerializeField] private float _abilitySlideDistance = 400f;

        [Header("슬라이드 애니메이션")]
        [SerializeField] private float _slideDuration = 0.5f;
        [SerializeField] private Ease _slideInEase = Ease.OutBack;
        [SerializeField] private Ease _slideOutEase = Ease.InBack;
        [SerializeField] private float _slideStagger = 0.08f;

        // ─────────────────────────────────────────
        //  Inspector — 센트리 HUD (HP, 스킬, 레벨, EXP, KO)
        // ─────────────────────────────────────────

        [Header("타격 센트리 HUD ★ 반드시 연결 필요")]
        [SerializeField] private Image _strikeHpFill;
        [SerializeField] private TMP_Text _strikeHpText;
        [SerializeField] private Image _strikeSkillFill;
        [SerializeField] private TMP_Text _strikeLevelText;
        [SerializeField] private Image _strikeExpFill;
        [SerializeField] private GameObject _strikeKoIcon;

        [Header("사격 센트리 HUD ★ 반드시 연결 필요")]
        [SerializeField] private Image _shootHpFill;
        [SerializeField] private TMP_Text _shootHpText;
        [SerializeField] private Image _shootSkillFill;
        [SerializeField] private TMP_Text _shootLevelText;
        [SerializeField] private Image _shootExpFill;
        [SerializeField] private GameObject _shootKoIcon;

        [Header("벽 센트리 HUD ★ 반드시 연결 필요")]
        [SerializeField] private Image _wallHpFill;
        [SerializeField] private TMP_Text _wallHpText;
        [SerializeField] private Image _wallSkillFill;
        [SerializeField] private TMP_Text _wallLevelText;
        [SerializeField] private Image _wallExpFill;
        [SerializeField] private GameObject _wallKoIcon;

        // ─────────────────────────────────────────
        //  Inspector — 능력 HUD
        // ─────────────────────────────────────────

        [Header("플레이어 능력 쿨타임")]
        [SerializeField] private Image _ability1CooldownFill;
        [SerializeField] private Image _ability2CooldownFill;
        [SerializeField] private Image _ability3CooldownFill;
        [SerializeField] private TMP_Text _ability1CooldownText;
        [SerializeField] private TMP_Text _ability2CooldownText;
        [SerializeField] private TMP_Text _ability3CooldownText;

        // ─────────────────────────────────────────
        //  Inspector — 콤보 HUD
        // ─────────────────────────────────────────

        [Header("공용 콤보 게이지")]
        [SerializeField] private Image _comboGaugeFill;
        [SerializeField] private TMP_Text _comboGaugeText;

        [Header("2콤보 쿨타임 아이콘 (센트리 슬롯별 1개)")]
        [SerializeField] private Image _strike2ComboFill;
        [SerializeField] private Image _shoot2ComboFill;
        [SerializeField] private Image _wall2ComboFill;

        [Header("3콤보 쿨타임 아이콘 (센트리 슬롯별 1개)")]
        [SerializeField] private Image _strike3ComboFill;
        [SerializeField] private Image _shoot3ComboFill;
        [SerializeField] private Image _wall3ComboFill;

        [Header("갱신 주기")]
        [SerializeField] private float _sentryHudRefreshRate = 0.1f;

        // ─────────────────────────────────────────
        //  내부 상태
        // ─────────────────────────────────────────

        private Vector2 _sentryHudOnPos;
        private Vector2 _abilityHudOnPos;
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
            // ── 슬라이드 초기 위치 저장 → 화면 밖으로 이동 ──
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

            // ── Inspector 연결 검증 ──
            // 실시간 갱신 / 스킬 Fill / KO 아이콘이 동작하지 않는다면
            // 아래 Console 경고에서 null인 슬롯을 확인하세요.
            ValidateInspectorConnections();

            StartCoroutine(SentryHudRefreshRoutine());
        }

        private void Update()
        {
            RefreshAbilityHud();
            RefreshComboHud();
        }

        // ─────────────────────────────────────────
        //  Inspector 검증 (버그 2, 3, 4 진단용)
        // ─────────────────────────────────────────

        /// <summary>
        /// Start()에서 1회 실행되어 null인 Inspector 슬롯을 Console 경고로 출력합니다.
        /// 실시간 갱신·스킬 Fill·KO 아이콘이 동작하지 않을 때 이 로그를 먼저 확인하세요.
        /// </summary>
        private void ValidateInspectorConnections()
        {
            // 센트리 참조
            if (_strikeSentry == null) Debug.LogWarning("[PlayerBattleUI] ★ _strikeSentry 미연결");
            if (_shootSentry == null) Debug.LogWarning("[PlayerBattleUI] ★ _shootSentry 미연결");
            if (_wallSentry == null) Debug.LogWarning("[PlayerBattleUI] ★ _wallSentry 미연결");

            // HP 바
            if (_strikeHpFill == null) Debug.LogWarning("[PlayerBattleUI] ★ _strikeHpFill 미연결");
            if (_shootHpFill == null) Debug.LogWarning("[PlayerBattleUI] ★ _shootHpFill 미연결");
            if (_wallHpFill == null) Debug.LogWarning("[PlayerBattleUI] ★ _wallHpFill 미연결");

            // 스킬 게이지 Fill (버그 3)
            if (_strikeSkillFill == null) Debug.LogWarning("[PlayerBattleUI] ★ _strikeSkillFill 미연결 — 스킬 게이지 표시 안됨");
            if (_shootSkillFill == null) Debug.LogWarning("[PlayerBattleUI] ★ _shootSkillFill 미연결 — 스킬 게이지 표시 안됨");
            if (_wallSkillFill == null) Debug.LogWarning("[PlayerBattleUI] ★ _wallSkillFill 미연결 — 스킬 게이지 표시 안됨");

            // KO 아이콘 (버그 4)
            if (_strikeKoIcon == null) Debug.LogWarning("[PlayerBattleUI] ★ _strikeKoIcon 미연결 — KO 아이콘 표시 안됨");
            if (_shootKoIcon == null) Debug.LogWarning("[PlayerBattleUI] ★ _shootKoIcon 미연결 — KO 아이콘 표시 안됨");
            if (_wallKoIcon == null) Debug.LogWarning("[PlayerBattleUI] ★ _wallKoIcon 미연결 — KO 아이콘 표시 안됨");

            Debug.Log("[PlayerBattleUI] Inspector 검증 완료");
        }

        // ─────────────────────────────────────────
        //  슬라이드 인 / 아웃
        // ─────────────────────────────────────────

        public void SlideIn()
        {
            SlidePanel(_sentryHudPanel, ref _sentryTween,
                _sentryHudOnPos, _slideDuration, _slideInEase, 0f);
            SlidePanel(_abilityHudPanel, ref _abilityTween,
                _abilityHudOnPos, _slideDuration, _slideInEase, _slideStagger);
        }

        public void SlideOut()
        {
            Vector2 sOff = GetOffscreenPos(_sentryHudOnPos,
                _sentrySlideOnX, _sentrySlideFromPositive, _sentrySlideDistance);
            Vector2 aOff = GetOffscreenPos(_abilityHudOnPos,
                _abilitySlideOnX, _abilitySlideFromPositive, _abilitySlideDistance);

            SlidePanel(_sentryHudPanel, ref _sentryTween,
                sOff, _slideDuration, _slideOutEase, 0f);
            SlidePanel(_abilityHudPanel, ref _abilityTween,
                aOff, _slideDuration, _slideOutEase, _slideStagger);
        }

        private void SlidePanel(RectTransform panel, ref Tween tween,
            Vector2 target, float duration, Ease ease, float delay)
        {
            if (panel == null) return;
            tween?.Kill();
            tween = panel.DOAnchorPos(target, duration).SetEase(ease).SetDelay(delay);
        }

        private Vector2 GetOffscreenPos(
            Vector2 onPos, bool slideOnX, bool fromPositive, float distance)
        {
            float offset = fromPositive ? distance : -distance;
            return slideOnX
                ? new Vector2(onPos.x + offset, onPos.y)
                : new Vector2(onPos.x, onPos.y + offset);
        }

        // ─────────────────────────────────────────
        //  센트리 HUD 갱신 (버그 2, 3, 4 핵심)
        // ─────────────────────────────────────────

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

        private void RefreshSentryHud(
            SentryBase sentry,
            Image hpFill, TMP_Text hpText,
            Image skillFill,
            TMP_Text levelText, Image expFill,
            GameObject koIcon)
        {
            if (sentry == null) return;

            bool isKo = sentry.IsKnockedOut;

            // 버그 4 — KO 아이콘
            if (koIcon != null) koIcon.SetActive(isKo);

            // 버그 2 — HP 실시간 갱신
            if (hpFill != null)
                hpFill.fillAmount = sentry.MaxHp > 0
                    ? (float)sentry.CurrentHp / sentry.MaxHp : 0f;
            if (hpText != null)
                hpText.text = $"{sentry.CurrentHp}/{sentry.MaxHp}";

            // 버그 3 — 스킬 게이지 Fill
            if (skillFill != null)
                skillFill.fillAmount = GetSkillRatio(sentry);

            if (levelText != null)
                levelText.text = $"Lv.{sentry.CurrentLevel}";
            if (expFill != null)
                expFill.fillAmount = sentry.RequiredExp > 0
                    ? (float)sentry.CurrentExp / sentry.RequiredExp : 1f;
        }

        /// <summary>
        /// 센트리 타입별 스킬 게이지 비율을 반환합니다.
        /// WallSentry도 SkillGauge / MaxSkillGauge 프로퍼티가 public이어야 합니다.
        /// </summary>
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
        //  능력 HUD 갱신
        // ─────────────────────────────────────────

        private void RefreshAbilityHud()
        {
            if (_playerAbility == null) return;

            RefreshAbilitySlot(_ability1CooldownFill, _ability1CooldownText,
                _playerAbility.Ability1CooldownRatio);
            RefreshAbilitySlot(_ability2CooldownFill, _ability2CooldownText,
                _playerAbility.Ability2CooldownRatio);
            RefreshAbilitySlot(_ability3CooldownFill, _ability3CooldownText,
                _playerAbility.Ability3CooldownRatio);
        }

        private void RefreshAbilitySlot(Image fill, TMP_Text text, float ratio)
        {
            if (fill != null) fill.fillAmount = ratio;
            if (text != null)
            {
                bool ready = ratio >= 1f;
                text.gameObject.SetActive(!ready);
                if (!ready) text.text = $"{Mathf.RoundToInt(ratio * 100)}%";
            }
        }

        // ─────────────────────────────────────────
        //  콤보 HUD 갱신
        // ─────────────────────────────────────────

        private void RefreshComboHud()
        {
            if (SentryComboManager.Instance == null) return;

            float comboRatio = SentryComboManager.Instance.MaxComboGauge > 0
                ? SentryComboManager.Instance.ComboGauge /
                  SentryComboManager.Instance.MaxComboGauge : 0f;

            if (_comboGaugeFill != null) _comboGaugeFill.fillAmount = comboRatio;
            if (_comboGaugeText != null)
                _comboGaugeText.text =
                    $"{(int)SentryComboManager.Instance.ComboGauge}/" +
                    $"{(int)SentryComboManager.Instance.MaxComboGauge}";

            float c2 = SentryComboManager.Instance.Combo2CooldownRatio;
            if (_strike2ComboFill != null) _strike2ComboFill.fillAmount = c2;
            if (_shoot2ComboFill != null) _shoot2ComboFill.fillAmount = c2;
            if (_wall2ComboFill != null) _wall2ComboFill.fillAmount = c2;

            float c3 = SentryComboManager.Instance.Combo3CooldownRatio;
            if (_strike3ComboFill != null) _strike3ComboFill.fillAmount = c3;
            if (_shoot3ComboFill != null) _shoot3ComboFill.fillAmount = c3;
            if (_wall3ComboFill != null) _wall3ComboFill.fillAmount = c3;
        }

        // ─────────────────────────────────────────
        //  레벨업 연출
        // ─────────────────────────────────────────

        public void PlayLevelUpEffect(string sentryName, int newLevel)
        {
            TMP_Text t = null;
            if (_strikeSentry != null && _strikeSentry.SentryName == sentryName) t = _strikeLevelText;
            else if (_shootSentry != null && _shootSentry.SentryName == sentryName) t = _shootLevelText;
            else if (_wallSentry != null && _wallSentry.SentryName == sentryName) t = _wallLevelText;

            t?.transform.DOPunchScale(Vector3.one * 0.4f, 0.4f, 5, 0.5f);
            Debug.Log($"<color=yellow>[PlayerBattleUI] {sentryName} 레벨업! Lv.{newLevel}</color>");
        }
    }
}