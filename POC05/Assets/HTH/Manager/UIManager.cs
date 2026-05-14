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
    /// [설계 의도]
    /// - 기존 UIManager.cs(조합 UI 중심)를 완전히 폐기하고 새 컨셉에 맞게 재작성합니다.
    /// - 관리하는 UI 요소:
    ///     1. 센트리 HP 바 (3개)
    ///     2. 센트리 스킬 게이지 (3개)
    ///     3. 콤보 게이지 + 2콤보/3콤보 쿨타임 아이콘
    ///     4. 센트리 레벨 / 경험치 바 (3개)
    ///     5. 플레이어 HP 바
    ///     6. 플레이어 특수 능력 쿨타임 (3개)
    ///     7. 배틀 킬 카운트 텍스트
    ///     8. KO 상태 아이콘 표시
    /// - 매 프레임 Update가 아닌 필요 시점에만 갱신하는 방식을 지향합니다.
    ///   (무거운 UI는 코루틴으로 일정 주기마다 갱신)
    ///
    /// [히어라키 위치]
    /// UI Canvas
    ///   ├── SentryHUD
    ///   │     ├── StrikePanel, ShootPanel, WallPanel
    ///   ├── PlayerHUD
    ///   ├── ComboHUD
    ///   └── BattleHUD
    /// --- Managers ---
    ///   └── UIManager (이 스크립트)
    /// </summary>
    public class UIManager : MonoBehaviour
    {
        // ─────────────────────────────────────────
        //  싱글턴
        // ─────────────────────────────────────────

        /// <summary>씬 어디서든 UIManager.Instance로 접근합니다.</summary>
        public static UIManager Instance { get; private set; }

        // ─────────────────────────────────────────
        //  Inspector 노출 필드
        // ─────────────────────────────────────────

        [Header("센트리 참조")]
        [Tooltip("타격 센트리 (HP/스킬/레벨 데이터 읽기용)")]
        [SerializeField] private StrikeSentry _strikeSentry;

        [Tooltip("사격 센트리")]
        [SerializeField] private ShootSentry _shootSentry;

        [Tooltip("벽 센트리")]
        [SerializeField] private WallSentry _wallSentry;

        [Header("플레이어 참조")]
        [Tooltip("플레이어 체력 컴포넌트")]
        [SerializeField] private PlayerHealth _playerHealth;

        [Tooltip("플레이어 특수 능력 컴포넌트")]
        [SerializeField] private PlayerAbility _playerAbility;

        // ── 센트리 HUD ──────────────────────────

        [Header("센트리 HP 바 (Image - Filled)")]
        [Tooltip("타격 센트리 HP 이미지 (Image Type = Filled)")]
        [SerializeField] private Image _strikeHpFill;

        [Tooltip("사격 센트리 HP 이미지")]
        [SerializeField] private Image _shootHpFill;

        [Tooltip("벽 센트리 HP 이미지")]
        [SerializeField] private Image _wallHpFill;

        [Header("센트리 HP 텍스트")]
        [Tooltip("타격 센트리 HP 수치 텍스트 (예: 120/150)")]
        [SerializeField] private TMP_Text _strikeHpText;

        [Tooltip("사격 센트리 HP 수치 텍스트")]
        [SerializeField] private TMP_Text _shootHpText;

        [Tooltip("벽 센트리 HP 수치 텍스트")]
        [SerializeField] private TMP_Text _wallHpText;

        [Header("센트리 스킬 게이지 (Image - Filled)")]
        [Tooltip("타격 센트리 스킬 게이지 이미지")]
        [SerializeField] private Image _strikeSkillFill;

        [Tooltip("사격 센트리 스킬 게이지 이미지")]
        [SerializeField] private Image _shootSkillFill;

        [Tooltip("벽 센트리 스킬 게이지 이미지")]
        [SerializeField] private Image _wallSkillFill;

        [Header("센트리 레벨 텍스트")]
        [Tooltip("타격 센트리 레벨 텍스트 (예: Lv.3)")]
        [SerializeField] private TMP_Text _strikeLevelText;

        [Tooltip("사격 센트리 레벨 텍스트")]
        [SerializeField] private TMP_Text _shootLevelText;

        [Tooltip("벽 센트리 레벨 텍스트")]
        [SerializeField] private TMP_Text _wallLevelText;

        [Header("센트리 경험치 바 (Image - Filled)")]
        [Tooltip("타격 센트리 경험치 이미지")]
        [SerializeField] private Image _strikeExpFill;

        [Tooltip("사격 센트리 경험치 이미지")]
        [SerializeField] private Image _shootExpFill;

        [Tooltip("벽 센트리 경험치 이미지")]
        [SerializeField] private Image _wallExpFill;

        [Header("센트리 KO 아이콘")]
        [Tooltip("타격 센트리 KO 아이콘 (KO 시 활성화)")]
        [SerializeField] private GameObject _strikeKoIcon;

        [Tooltip("사격 센트리 KO 아이콘")]
        [SerializeField] private GameObject _shootKoIcon;

        [Tooltip("벽 센트리 KO 아이콘")]
        [SerializeField] private GameObject _wallKoIcon;

        // ── 플레이어 HUD ─────────────────────────

        [Header("플레이어 HP")]
        [Tooltip("플레이어 HP 슬라이더")]
        [SerializeField] private Slider _playerHpSlider;

        [Tooltip("플레이어 HP 텍스트 (예: 80/100)")]
        [SerializeField] private TMP_Text _playerHpText;

        [Header("플레이어 능력 쿨타임 (Image - Filled)")]
        [Tooltip("능력1(목표 지정) 쿨타임 이미지 (fillAmount: 0=쿨타임 중 / 1=사용 가능)")]
        [SerializeField] private Image _ability1CooldownFill;

        [Tooltip("능력2(긴급 수리) 쿨타임 이미지")]
        [SerializeField] private Image _ability2CooldownFill;

        [Tooltip("능력3(과부화) 쿨타임 이미지")]
        [SerializeField] private Image _ability3CooldownFill;

        [Tooltip("능력1 쿨타임 텍스트 (남은 초 표시, 사용 가능 시 숨김)")]
        [SerializeField] private TMP_Text _ability1CooldownText;

        [Tooltip("능력2 쿨타임 텍스트")]
        [SerializeField] private TMP_Text _ability2CooldownText;

        [Tooltip("능력3 쿨타임 텍스트")]
        [SerializeField] private TMP_Text _ability3CooldownText;

        // ── 콤보 HUD ─────────────────────────────

        [Header("콤보 게이지")]
        [Tooltip("콤보 게이지 이미지 (fillAmount 방식)")]
        [SerializeField] private Image _comboGaugeFill;

        [Tooltip("콤보 게이지 수치 텍스트 (예: 75/100)")]
        [SerializeField] private TMP_Text _comboGaugeText;

        [Tooltip("2콤보 쿨타임 아이콘 이미지 (fillAmount)")]
        [SerializeField] private Image _combo2CooldownFill;

        [Tooltip("3콤보 쿨타임 아이콘 이미지 (fillAmount)")]
        [SerializeField] private Image _combo3CooldownFill;

        [Tooltip("2콤보 사용 가능 표시 오브젝트 (쿨타임 0일 때 활성화)")]
        [SerializeField] private GameObject _combo2ReadyIcon;

        [Tooltip("3콤보 사용 가능 표시 오브젝트")]
        [SerializeField] private GameObject _combo3ReadyIcon;

        // ── 배틀 HUD ─────────────────────────────

        [Header("배틀 킬 카운트")]
        [Tooltip("킬 카운트 텍스트 (예: Kill: 3 / 10)")]
        [SerializeField] private TMP_Text _killCountText;

        [Tooltip("배틀 HUD 루트 오브젝트. 배틀 중에만 활성화됩니다.")]
        [SerializeField] private GameObject _battleHudRoot;

        // ── 갱신 주기 ────────────────────────────

        [Header("UI 갱신 주기")]
        [Tooltip("센트리 HUD 갱신 주기 (초). 낮을수록 정확하지만 부하가 높습니다.")]
        [SerializeField] private float _sentryHudRefreshRate = 0.1f;

        // ─────────────────────────────────────────
        //  유니티 생명주기
        // ─────────────────────────────────────────

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);
        }

        private void Start()
        {
            // 배틀 HUD 초기 비활성화
            if (_battleHudRoot != null) _battleHudRoot.SetActive(false);

            // 센트리 HUD 주기적 갱신 코루틴 시작
            StartCoroutine(SentryHudRefreshRoutine());
        }

        private void Update()
        {
            // 매 프레임 갱신이 필요한 UI (쿨타임 수치 등)
            RefreshPlayerHud();
            RefreshComboHud();
        }

        // ─────────────────────────────────────────
        //  센트리 HUD (주기적 갱신)
        // ─────────────────────────────────────────

        /// <summary>
        /// _sentryHudRefreshRate 주기마다 센트리 HP / 스킬 게이지 / 레벨 / EXP를 갱신합니다.
        /// 매 프레임 갱신 대신 코루틴으로 처리하여 성능을 최적화합니다.
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

        /// <summary>
        /// 개별 센트리의 모든 HUD 요소를 갱신합니다.
        /// </summary>
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

            // KO 아이콘 표시
            if (koIcon != null) koIcon.SetActive(isKo);

            // HP 바
            if (hpFill != null)
                hpFill.fillAmount = sentry.MaxHp > 0
                    ? (float)sentry.CurrentHp / sentry.MaxHp
                    : 0f;

            if (hpText != null)
                hpText.text = $"{sentry.CurrentHp}/{sentry.MaxHp}";

            // 스킬 게이지 (StrikeSentry / ShootSentry / WallSentry 각각 캐스팅)
            float skillRatio = GetSkillRatio(sentry);
            if (skillFill != null) skillFill.fillAmount = skillRatio;

            // 레벨
            if (levelText != null)
                levelText.text = $"Lv.{sentry.CurrentLevel}";

            // 경험치 바
            if (expFill != null)
            {
                float expRatio = sentry.RequiredExp > 0
                    ? (float)sentry.CurrentExp / sentry.RequiredExp
                    : 1f;
                expFill.fillAmount = expRatio;
            }
        }

        /// <summary>
        /// 센트리 타입에 맞게 스킬 게이지 비율을 반환합니다.
        /// 각 자식 클래스의 SkillGauge / MaxSkillGauge 프로퍼티를 사용합니다.
        /// </summary>
        private float GetSkillRatio(SentryBase sentry)
        {
            if (sentry is StrikeSentry strike)
                return strike.MaxSkillGauge > 0
                    ? strike.SkillGauge / strike.MaxSkillGauge : 0f;

            if (sentry is ShootSentry shoot)
                return shoot.MaxSkillGauge > 0
                    ? shoot.SkillGauge / shoot.MaxSkillGauge : 0f;

            if (sentry is WallSentry wall)
                return wall.MaxSkillGauge > 0
                    ? wall.SkillGauge / wall.MaxSkillGauge : 0f;

            return 0f;
        }

        // ─────────────────────────────────────────
        //  플레이어 HUD (매 프레임)
        // ─────────────────────────────────────────

        /// <summary>
        /// 플레이어 HP와 특수 능력 쿨타임을 갱신합니다.
        /// 매 프레임 호출됩니다.
        /// </summary>
        private void RefreshPlayerHud()
        {
            // 플레이어 HP
            if (_playerHealth != null)
            {
                if (_playerHpSlider != null)
                    _playerHpSlider.value = _playerHealth.MaxHp > 0
                        ? (float)_playerHealth.CurrentHp / _playerHealth.MaxHp
                        : 0f;

                if (_playerHpText != null)
                    _playerHpText.text = $"{_playerHealth.CurrentHp}/{_playerHealth.MaxHp}";
            }

            // 특수 능력 쿨타임
            if (_playerAbility != null)
            {
                SetCooldownUI(_ability1CooldownFill, _ability1CooldownText,
                    _playerAbility.Ability1CooldownRatio);

                SetCooldownUI(_ability2CooldownFill, _ability2CooldownText,
                    _playerAbility.Ability2CooldownRatio);

                SetCooldownUI(_ability3CooldownFill, _ability3CooldownText,
                    _playerAbility.Ability3CooldownRatio);
            }
        }

        /// <summary>
        /// 쿨타임 이미지와 텍스트를 ratio(0~1)로 갱신합니다.
        /// ratio가 1이면 사용 가능 상태로 표시합니다.
        /// </summary>
        private void SetCooldownUI(Image fillImage, TMP_Text cooldownText, float ratio)
        {
            if (fillImage != null) fillImage.fillAmount = ratio;

            if (cooldownText != null)
            {
                if (ratio >= 1f)
                {
                    // 사용 가능
                    cooldownText.gameObject.SetActive(false);
                }
                else
                {
                    // 쿨타임 중: 남은 시간 표시
                    cooldownText.gameObject.SetActive(true);
                    // ratio가 0에 가까울수록 쿨타임이 많이 남음
                    // 실제 남은 초는 PlayerAbility에서 공개 프로퍼티 추가 권장
                    cooldownText.text = ratio < 1f ? "..." : "";
                }
            }
        }

        // ─────────────────────────────────────────
        //  콤보 HUD (매 프레임)
        // ─────────────────────────────────────────

        /// <summary>
        /// 콤보 게이지와 2/3콤보 쿨타임 아이콘을 갱신합니다.
        /// </summary>
        private void RefreshComboHud()
        {
            if (ComboManager.Instance == null) return;

            // 콤보 게이지
            float comboRatio = ComboManager.Instance.MaxComboGauge > 0
                ? ComboManager.Instance.ComboGauge / ComboManager.Instance.MaxComboGauge
                : 0f;

            if (_comboGaugeFill != null)
                _comboGaugeFill.fillAmount = comboRatio;

            if (_comboGaugeText != null)
                _comboGaugeText.text =
                    $"{(int)ComboManager.Instance.ComboGauge}/{(int)ComboManager.Instance.MaxComboGauge}";

            // 2콤보 쿨타임
            float combo2Ratio = ComboManager.Instance.Combo2CooldownRatio;
            if (_combo2CooldownFill != null) _combo2CooldownFill.fillAmount = combo2Ratio;
            if (_combo2ReadyIcon != null) _combo2ReadyIcon.SetActive(combo2Ratio >= 1f);

            // 3콤보 쿨타임
            float combo3Ratio = ComboManager.Instance.Combo3CooldownRatio;
            if (_combo3CooldownFill != null) _combo3CooldownFill.fillAmount = combo3Ratio;
            if (_combo3ReadyIcon != null) _combo3ReadyIcon.SetActive(combo3Ratio >= 1f);
        }

        // ─────────────────────────────────────────
        //  배틀 HUD (외부 호출)
        // ─────────────────────────────────────────

        /// <summary>
        /// 배틀 시작/종료 시 BattleManager가 호출합니다.
        /// 배틀 HUD를 활성화/비활성화합니다.
        /// </summary>
        public void SetBattleHudActive(bool active)
        {
            if (_battleHudRoot != null) _battleHudRoot.SetActive(active);
        }

        /// <summary>
        /// 킬 카운트 텍스트를 갱신합니다. BattleManager.OnEnemyDied()에서 호출합니다.
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
        /// 레벨업 시 센트리 HUD 패널에 레벨업 연출을 재생합니다.
        /// SentryBase.LevelUp()에서 호출합니다.
        /// </summary>
        /// <param name="sentryName">레벨업한 센트리 이름 (로그 및 연출용)</param>
        /// <param name="newLevel">새 레벨</param>
        public void PlayLevelUpEffect(string sentryName, int newLevel)
        {
            Debug.Log($"<color=yellow>[UI] {sentryName} 레벨업! → Lv.{newLevel}</color>");

            // 레벨 텍스트 펀치 스케일 연출
            TMP_Text levelText = GetLevelText(sentryName);
            if (levelText != null)
                levelText.transform.DOPunchScale(Vector3.one * 0.4f, 0.4f, 5, 0.5f);
        }

        /// <summary>센트리 이름으로 해당 레벨 텍스트 컴포넌트를 반환합니다.</summary>
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