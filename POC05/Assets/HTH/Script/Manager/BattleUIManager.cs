using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

namespace SENTRY
{
    /// <summary>
    /// 게임 내 UI 전체를 조율하는 싱글턴 매니저 (경량화 버전).
    /// (구 UIManager → BattleUIManager 이름 변경)
    ///
    /// [역할]
    /// 각 전담 매니저에 지시만 내리는 조율자입니다.
    ///   센트리 HUD / 능력 HUD / 콤보 HUD → PlayerBattleUIManager
    ///   적 캐릭터 HUD                     → EnemyBattleUIManager
    ///   패널 전환                         → BattleUIManager (이 클래스가 직접 처리)
    ///
    /// [버튼 이벤트 방식]
    /// Inspector OnClick 직접 연결 방식 대신 Button.onClick.AddListener()로
    /// 구독하고, OnDestroy()에서 RemoveListener()로 해제합니다.
    /// 패널이 활성화될 때 구독하고 비활성화/닫힐 때 해제하여
    /// 중복 호출과 메모리 누수를 방지합니다.
    ///
    /// [담당 패널]
    ///   EncounterPanel  — 전투/도망 선택창 (전투 버튼 / 도망 버튼)
    ///   VictoryPanel    — 배틀 결산 (확인 버튼)
    ///   DefeatPanel     — 패배 안내 (돌아가기 버튼)
    ///
    /// [히어라키 위치]
    /// --- Managers ---
    ///   └── BattleUIManager (이 스크립트)
    ///
    /// Canvas
    ///   ├── EncounterPanel     ← _encounterPanel (기본 비활성)
    ///   ├── VictoryPanel       ← _victoryPanel   (기본 비활성)
    ///   └── DefeatPanel        ← _defeatPanel    (기본 비활성)
    /// </summary>
    public class BattleUIManager : MonoBehaviour
    {
        // ─────────────────────────────────────────
        //  싱글턴
        // ─────────────────────────────────────────

        /// <summary>씬 어디서든 BattleUIManager.Instance로 접근합니다.</summary>
        public static BattleUIManager Instance { get; private set; }

        // ─────────────────────────────────────────
        //  Inspector — EncounterPanel (전투/도망 선택창)
        // ─────────────────────────────────────────

        [Header("EncounterPanel — 전투/도망 선택창")]
        [Tooltip("인카운터 선택창 루트 GameObject. 기본 비활성.")]
        [SerializeField] private GameObject _encounterPanel;

        [Tooltip("인카운터 이름 텍스트 (예: '숲 몬스터 출현!')")]
        [SerializeField] private TMP_Text _encounterNameText;

        [Tooltip("적 정보 텍스트 (예: '슬라임 × 3')")]
        [SerializeField] private TMP_Text _encounterEnemyInfoText;

        [Tooltip("[전투] 버튼. onClick을 코드에서 구독합니다.")]
        [SerializeField] private Button _fightButton;

        [Tooltip("[도망] 버튼. onClick을 코드에서 구독합니다.")]
        [SerializeField] private Button _fleeButton;

        // ─────────────────────────────────────────
        //  Inspector — VictoryPanel (배틀 결산창)
        // ─────────────────────────────────────────

        [Header("VictoryPanel — 배틀 결산창")]
        [Tooltip("결산창 루트 GameObject. 기본 비활성.")]
        [SerializeField] private GameObject _victoryPanel;

        [Tooltip("킬 카운트 결산 텍스트 (예: '처치: 5마리')")]
        [SerializeField] private TMP_Text _victoryKillText;

        [Tooltip("센트리 결산 텍스트 — 타격 센트리")]
        [SerializeField] private TMP_Text _victorySentryStrikeText;

        [Tooltip("센트리 결산 텍스트 — 사격 센트리")]
        [SerializeField] private TMP_Text _victorySentryShootText;

        [Tooltip("센트리 결산 텍스트 — 벽 센트리")]
        [SerializeField] private TMP_Text _victorySentryWallText;

        [Tooltip("VictoryPanel [확인] 버튼. onClick을 코드에서 구독합니다.")]
        [SerializeField] private Button _victoryConfirmButton;

        // ─────────────────────────────────────────
        //  Inspector — DefeatPanel (패배 안내창)
        // ─────────────────────────────────────────

        [Header("DefeatPanel — 패배 안내창")]
        [Tooltip("패배 패널 루트 GameObject. 기본 비활성.")]
        [SerializeField] private GameObject _defeatPanel;

        [Tooltip("DefeatPanel [돌아가기] 버튼. onClick을 코드에서 구독합니다.")]
        [SerializeField] private Button _defeatReturnButton;

        // ─────────────────────────────────────────
        //  내부 상태 변수
        // ─────────────────────────────────────────

        /// <summary>현재 선택 대기 중인 BattleTrigger</summary>
        private BattleTrigger _pendingTrigger;

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
            // 모든 패널 초기 비활성화
            if (_encounterPanel != null) _encounterPanel.SetActive(false);
            if (_victoryPanel != null) _victoryPanel.SetActive(false);
            if (_defeatPanel != null) _defeatPanel.SetActive(false);

            // ── 버튼 이벤트 구독 ──
            // 패널 비활성화 상태에서도 구독은 Awake/Start에서 1회만 등록합니다.
            // RemoveListener → AddListener 순서로 중복 등록을 방지합니다.
            if (_fightButton != null)
            {
                _fightButton.onClick.RemoveListener(OnFightButtonClicked);
                _fightButton.onClick.AddListener(OnFightButtonClicked);
            }

            if (_fleeButton != null)
            {
                _fleeButton.onClick.RemoveListener(OnFleeButtonClicked);
                _fleeButton.onClick.AddListener(OnFleeButtonClicked);
            }

            if (_victoryConfirmButton != null)
            {
                _victoryConfirmButton.onClick.RemoveListener(OnReturnButtonClicked);
                _victoryConfirmButton.onClick.AddListener(OnReturnButtonClicked);
            }

            if (_defeatReturnButton != null)
            {
                _defeatReturnButton.onClick.RemoveListener(OnReturnButtonClicked);
                _defeatReturnButton.onClick.AddListener(OnReturnButtonClicked);
            }
        }

        private void OnDestroy()
        {
            // ── 버튼 이벤트 구독 해제 ──
            // 씬 전환이나 오브젝트 파괴 시 메모리 누수 방지를 위해 해제합니다.
            if (_fightButton != null)
                _fightButton.onClick.RemoveListener(OnFightButtonClicked);

            if (_fleeButton != null)
                _fleeButton.onClick.RemoveListener(OnFleeButtonClicked);

            if (_victoryConfirmButton != null)
                _victoryConfirmButton.onClick.RemoveListener(OnReturnButtonClicked);

            if (_defeatReturnButton != null)
                _defeatReturnButton.onClick.RemoveListener(OnReturnButtonClicked);
        }

        // ─────────────────────────────────────────
        //  배틀 HUD 활성화 / 비활성화
        // ─────────────────────────────────────────

        /// <summary>
        /// 배틀 진입/종료 시 FieldManager가 호출합니다.
        /// 전담 매니저의 슬라이드 인/아웃을 지시합니다.
        /// </summary>
        /// <param name="active">true = 배틀 진입 / false = 배틀 종료</param>
        public void SetBattleHudActive(bool active)
        {
            if (active)
            {
                PlayerBattleUIManager.Instance?.SlideIn();
                EnemyBattleUIManager.Instance?.SlideIn();
            }
            else
            {
                PlayerBattleUIManager.Instance?.SlideOut();
                EnemyBattleUIManager.Instance?.SlideOut();
            }
        }

        // ─────────────────────────────────────────
        //  레벨업 연출 (PlayerBattleUIManager에 위임)
        // ─────────────────────────────────────────

        /// <summary>
        /// 레벨업 연출을 PlayerBattleUIManager에 위임합니다.
        /// SentryBase.LevelUp()에서 호출합니다.
        /// </summary>
        /// <param name="sentryName">레벨업한 센트리 이름</param>
        /// <param name="newLevel">새 레벨</param>
        public void PlayLevelUpEffect(string sentryName, int newLevel)
        {
            PlayerBattleUIManager.Instance?.PlayLevelUpEffect(sentryName, newLevel);
        }

        // ─────────────────────────────────────────
        //  EncounterPanel — 전투/도망 선택창
        // ─────────────────────────────────────────

        /// <summary>
        /// 인카운터 선택창을 열고 적 정보를 표시합니다.
        /// BattleTrigger.ShowEncounterPanel()에서 호출합니다.
        /// </summary>
        /// <param name="data">인카운터 데이터 SO (적 이름·수 표시용)</param>
        /// <param name="trigger">결과 콜백을 받을 BattleTrigger</param>
        public void ShowEncounterPanel(BattleEncounterDataSO data, BattleTrigger trigger)
        {
            if (_encounterPanel == null) return;

            // 인카운터 이름 표시
            if (_encounterNameText != null)
                _encounterNameText.text = data != null ? data.encounterName : "???";

            // 적 정보 요약 텍스트 (spawnEntries 첫 번째 적 이름 × 총 수)
            if (_encounterEnemyInfoText != null && data != null)
            {
                int total = data.GetTotalEnemyCount();
                string firstName = data.spawnEntries.Count > 0 &&
                                   data.spawnEntries[0].enemyPrefab != null
                    ? data.spawnEntries[0].enemyPrefab.name
                    : "Unknown";

                _encounterEnemyInfoText.text = total > 1
                    ? $"{firstName} 외 {total - 1}마리"
                    : $"{firstName}";
            }

            // BattleTrigger 참조 보관 → 버튼 클릭 시 콜백 전달
            _pendingTrigger = trigger;

            // 패널 팝인 연출
            _encounterPanel.SetActive(true);
            _encounterPanel.transform.localScale = Vector3.zero;
            _encounterPanel.transform
                .DOScale(Vector3.one, 0.3f)
                .SetEase(Ease.OutBack);
        }

        /// <summary>
        /// 인카운터 선택창을 닫습니다.
        /// BattleTrigger.OnPlayerChoseFight() / OnPlayerChoseFlee()에서 호출합니다.
        /// </summary>
        public void HideEncounterPanel()
        {
            if (_encounterPanel == null) return;

            _encounterPanel.transform
                .DOScale(Vector3.zero, 0.2f)
                .SetEase(Ease.InBack)
                .OnComplete(() => _encounterPanel.SetActive(false));

            _pendingTrigger = null;
        }

        // ─────────────────────────────────────────
        //  EncounterPanel 버튼 콜백
        // ─────────────────────────────────────────

        /// <summary>
        /// [전투] 버튼 클릭 콜백.
        /// Start()에서 _fightButton.onClick.AddListener()로 구독됩니다.
        /// </summary>
        private void OnFightButtonClicked()
        {
            _pendingTrigger?.OnPlayerChoseFight();
        }

        /// <summary>
        /// [도망] 버튼 클릭 콜백.
        /// Start()에서 _fleeButton.onClick.AddListener()로 구독됩니다.
        /// </summary>
        private void OnFleeButtonClicked()
        {
            _pendingTrigger?.OnPlayerChoseFlee();
        }

        // ─────────────────────────────────────────
        //  VictoryPanel — 배틀 결산창
        // ─────────────────────────────────────────

        /// <summary>
        /// 승리 결산창을 열고 배틀 결과를 표시합니다.
        /// BattleManager.EndBattle(isVictory: true)에서 호출합니다.
        /// </summary>
        /// <param name="data">배틀 결산 데이터 (킬 카운트 · 센트리별 EXP · 레벨)</param>
        public void ShowVictoryPanel(BattleResultData data)
        {
            if (_victoryPanel == null) return;

            // 킬 카운트 결산 표시
            if (_victoryKillText != null)
                _victoryKillText.text = $"처치: {data.killCount}마리";

            // 센트리별 결산 텍스트 (이름 · 레벨 · 획득 EXP)
            if (data.sentryResults != null)
            {
                for (int i = 0; i < data.sentryResults.Count; i++)
                {
                    SentryResultData sr = data.sentryResults[i];
                    string line = sr.isKnockedOut
                        ? $"{sr.sentryName}  Lv.{sr.level}  KO"
                        : $"{sr.sentryName}  Lv.{sr.level}  +{sr.expGained} EXP";

                    if (i == 0 && _victorySentryStrikeText != null)
                        _victorySentryStrikeText.text = line;
                    else if (i == 1 && _victorySentryShootText != null)
                        _victorySentryShootText.text = line;
                    else if (i == 2 && _victorySentryWallText != null)
                        _victorySentryWallText.text = line;
                }
            }

            // 패널 팝인 연출
            _victoryPanel.SetActive(true);
            _victoryPanel.transform.localScale = Vector3.zero;
            _victoryPanel.transform
                .DOScale(Vector3.one, 0.4f)
                .SetEase(Ease.OutBack);
        }

        // ─────────────────────────────────────────
        //  DefeatPanel — 패배 안내창
        // ─────────────────────────────────────────

        /// <summary>
        /// 패배 안내창을 열습니다.
        /// BattleManager.EndBattle(isVictory: false)에서 호출합니다.
        /// </summary>
        public void ShowDefeatPanel()
        {
            if (_defeatPanel == null) return;

            _defeatPanel.SetActive(true);
            _defeatPanel.transform.localScale = Vector3.zero;
            _defeatPanel.transform
                .DOScale(Vector3.one, 0.4f)
                .SetEase(Ease.OutBack);
        }

        // ─────────────────────────────────────────
        //  결과 패널 닫기
        // ─────────────────────────────────────────

        /// <summary>
        /// VictoryPanel / DefeatPanel을 모두 비활성화합니다.
        /// BattleManager.ReturnToFieldFromResult()에서 호출합니다.
        /// </summary>
        public void HideResultPanels()
        {
            if (_victoryPanel != null) _victoryPanel.SetActive(false);
            if (_defeatPanel != null) _defeatPanel.SetActive(false);
        }

        // ─────────────────────────────────────────
        //  결과 패널 버튼 콜백
        // ─────────────────────────────────────────

        /// <summary>
        /// VictoryPanel [확인] / DefeatPanel [돌아가기] 버튼 클릭 콜백.
        /// Start()에서 각 버튼의 onClick.AddListener()로 구독됩니다.
        /// </summary>
        private void OnReturnButtonClicked()
        {
            BattleManager.Instance?.ReturnToFieldFromResult();
        }
    }
}