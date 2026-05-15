using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;
using System.Collections;

namespace SENTRY
{
    /// <summary>
    /// 게임 내 UI 전체를 조율하는 싱글턴 매니저.
    ///
    /// [변경 사항]
    /// - _countdownPanel 제거. 카운트다운은 _countdownText 하나로만 표시합니다.
    /// - PlayCountdown() 종료 후 _countdownText 내용을 비워 화면에 남지 않도록 수정.
    /// - ShowEncounterPanel() 콜백이 BattleTrigger → FieldManager로 연결됩니다.
    ///
    /// [히어라키 위치]
    /// --- Managers ---
    ///   └── BattleUIManager (이 스크립트)
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

        [Tooltip("적 정보 텍스트 (예: '슬라임 외 2마리')")]
        [SerializeField] private TMP_Text _encounterEnemyInfoText;

        [Tooltip("[전투] 버튼. onClick을 코드에서 구독합니다.")]
        [SerializeField] private Button _fightButton;

        [Tooltip("[도망] 버튼. onClick을 코드에서 구독합니다.")]
        [SerializeField] private Button _fleeButton;

        // ─────────────────────────────────────────
        //  Inspector — 카운트다운 텍스트
        // ─────────────────────────────────────────

        [Header("카운트다운 텍스트")]
        [Tooltip("전투 시작 전 카운트다운 숫자를 표시할 TMP_Text.\n" +
                 "Canvas에 단독으로 배치하세요. 패널 오브젝트는 필요 없습니다.\n" +
                 "연결하지 않으면 카운트다운 UI 없이 딜레이만 적용됩니다.")]
        [SerializeField] private TMP_Text _countdownText;

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
        //  유니티 생명주기
        // ─────────────────────────────────────────

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else { Destroy(gameObject); return; }
        }

        private void Start()
        {
            // 패널 초기 비활성화
            if (_encounterPanel != null) _encounterPanel.SetActive(false);
            if (_victoryPanel != null) _victoryPanel.SetActive(false);
            if (_defeatPanel != null) _defeatPanel.SetActive(false);

            // 카운트다운 텍스트 초기화
            if (_countdownText != null) _countdownText.text = string.Empty;

            // 버튼 이벤트 구독
            if (_fightButton != null)
            {
                _fightButton.onClick.RemoveAllListeners();
                _fightButton.onClick.AddListener(OnFightButtonClicked);
            }
            if (_fleeButton != null)
            {
                _fleeButton.onClick.RemoveAllListeners();
                _fleeButton.onClick.AddListener(OnFleeButtonClicked);
            }
            if (_victoryConfirmButton != null)
            {
                _victoryConfirmButton.onClick.RemoveAllListeners();
                _victoryConfirmButton.onClick.AddListener(OnReturnButtonClicked);
            }
            if (_defeatReturnButton != null)
            {
                _defeatReturnButton.onClick.RemoveAllListeners();
                _defeatReturnButton.onClick.AddListener(OnReturnButtonClicked);
            }
        }

        private void OnDestroy()
        {
            _fightButton?.onClick.RemoveAllListeners();
            _fleeButton?.onClick.RemoveAllListeners();
            _victoryConfirmButton?.onClick.RemoveAllListeners();
            _defeatReturnButton?.onClick.RemoveAllListeners();
        }

        // ─────────────────────────────────────────
        //  배틀 HUD 슬라이드 (PlayerBattleUIManager / EnemyBattleUIManager 위임)
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
        //  EncounterPanel — 전투/도망 선택창
        // ─────────────────────────────────────────

        /// <summary>
        /// 배틀 필드 낙하 연출 완료 후 FieldManager가 호출합니다.
        /// 버튼 클릭 결과는 FieldManager.OnFightChosen() / OnFleeChosen()으로 전달됩니다.
        /// </summary>
        public void ShowEncounterPanel(BattleEncounterDataSO data, BattleTrigger ignored)
        {
            if (_encounterPanel == null) return;

            if (_encounterNameText != null)
                _encounterNameText.text = data != null ? data.encounterName : "???";

            if (_encounterEnemyInfoText != null && data != null)
            {
                int total = data.GetTotalEnemyCount();
                string firstName = data.spawnEntries != null &&
                                   data.spawnEntries.Count > 0 &&
                                   data.spawnEntries[0].enemyPrefab != null
                    ? data.spawnEntries[0].enemyPrefab.name : "Unknown";

                _encounterEnemyInfoText.text = total > 1
                    ? $"{firstName} 외 {total - 1}마리"
                    : firstName;
            }

            _encounterPanel.SetActive(true);
            _encounterPanel.transform.localScale = Vector3.zero;
            _encounterPanel.transform
                .DOScale(Vector3.one, 0.3f)
                .SetEase(Ease.OutBack);
        }

        /// <summary>
        /// 인카운터 선택창을 닫습니다.
        /// BattleUIManager 버튼 클릭 시 내부에서 자동 호출됩니다.
        /// </summary>
        public void HideEncounterPanel()
        {
            if (_encounterPanel == null) return;
            _encounterPanel.transform
                .DOScale(Vector3.zero, 0.2f)
                .SetEase(Ease.InBack)
                .OnComplete(() => _encounterPanel.SetActive(false));
        }

        // ─────────────────────────────────────────
        //  EncounterPanel 버튼 콜백
        // ─────────────────────────────────────────

        private void OnFightButtonClicked()
        {
            HideEncounterPanel();
            FieldManager.Instance?.OnFightChosen();
        }

        private void OnFleeButtonClicked()
        {
            HideEncounterPanel();
            FieldManager.Instance?.OnFleeChosen();
        }

        // ─────────────────────────────────────────
        //  카운트다운
        // ─────────────────────────────────────────

        /// <summary>
        /// 전투 시작 전 카운트다운을 재생합니다.
        /// FieldManager.CountdownAndStartBattle()에서 yield return으로 대기합니다.
        ///
        /// [버그 수정]
        /// 카운트다운 완료 후 _countdownText.text를 string.Empty로 초기화합니다.
        /// 이전에는 "전투!" 텍스트가 화면에 계속 남아있는 버그가 있었습니다.
        ///
        /// [_countdownPanel 제거]
        /// 별도 패널 오브젝트 없이 TMP_Text 하나만 사용합니다.
        /// Canvas에 _countdownText를 단독으로 배치하면 됩니다.
        /// </summary>
        /// <param name="duration">카운트다운 총 시간 (초)</param>
        public IEnumerator PlayCountdown(float duration)
        {
            if (_countdownText == null)
            {
                // 텍스트 없으면 딜레이만 적용
                yield return new WaitForSeconds(duration);
                yield break;
            }

            int count = Mathf.RoundToInt(duration);

            for (int i = count; i >= 1; i--)
            {
                _countdownText.text = i.ToString();

                // 팝인 연출: 크게 → 정상 크기로
                _countdownText.transform.DOKill();
                _countdownText.transform.localScale = Vector3.one * 1.6f;
                _countdownText.transform
                    .DOScale(Vector3.one, 0.85f)
                    .SetEase(Ease.OutBack);

                yield return new WaitForSeconds(1f);
            }

            // "전투!" 표시 후 페이드 아웃
            _countdownText.text = "전투!";
            _countdownText.transform.DOKill();
            _countdownText.transform.localScale = Vector3.one * 1.4f;
            _countdownText.transform
                .DOScale(Vector3.one, 0.3f)
                .SetEase(Ease.OutBack);

            // [버그 수정] "전투!" 잠깐 보여준 뒤 텍스트를 비워 화면에서 제거
            yield return new WaitForSeconds(0.6f);

            _countdownText.transform.DOKill();
            _countdownText.text = string.Empty;
            _countdownText.transform.localScale = Vector3.one;
        }

        // ─────────────────────────────────────────
        //  VictoryPanel — 배틀 결산창
        // ─────────────────────────────────────────

        /// <summary>
        /// 승리 결산창을 열고 배틀 결과를 표시합니다.
        /// BattleManager.EndBattle(isVictory: true)에서 호출합니다.
        /// </summary>
        public void ShowVictoryPanel(BattleResultData data)
        {
            if (_victoryPanel == null) return;

            if (_victoryKillText != null)
                _victoryKillText.text = $"처치: {data.killCount}마리";

            if (data.sentryResults != null)
            {
                for (int i = 0; i < data.sentryResults.Count; i++)
                {
                    SentryResultData sr = data.sentryResults[i];
                    string line = sr.isKnockedOut
                        ? $"{sr.sentryName}  Lv.{sr.level}  KO"
                        : $"{sr.sentryName}  Lv.{sr.level}  +{sr.expGained} EXP";

                    if (i == 0 && _victorySentryStrikeText != null) _victorySentryStrikeText.text = line;
                    else if (i == 1 && _victorySentryShootText != null) _victorySentryShootText.text = line;
                    else if (i == 2 && _victorySentryWallText != null) _victorySentryWallText.text = line;
                }
            }

            _victoryPanel.SetActive(true);
            _victoryPanel.transform.localScale = Vector3.zero;
            _victoryPanel.transform.DOScale(Vector3.one, 0.4f).SetEase(Ease.OutBack);
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
            _defeatPanel.transform.DOScale(Vector3.one, 0.3f).SetEase(Ease.OutBack);
        }

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

        private void OnReturnButtonClicked()
        {
            BattleManager.Instance?.ReturnToFieldFromResult();
        }

        // ─────────────────────────────────────────
        //  레벨업 연출 (PlayerBattleUIManager에 위임)
        // ─────────────────────────────────────────

        /// <summary>
        /// 레벨업 연출을 PlayerBattleUIManager에 위임합니다.
        /// SentryBase.LevelUp()에서 호출합니다.
        /// </summary>
        public void PlayLevelUpEffect(string sentryName, int newLevel)
        {
            PlayerBattleUIManager.Instance?.PlayLevelUpEffect(sentryName, newLevel);
        }
    }
}