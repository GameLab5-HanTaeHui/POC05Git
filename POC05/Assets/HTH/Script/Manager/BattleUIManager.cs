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
    /// [변경 사항 — 진입 구조 재설계]
    /// - ShowEncounterPanel() 콜백이 BattleTrigger → FieldManager로 변경됩니다.
    ///   선택창은 이제 배틀 필드에서 표시되며
    ///   버튼 클릭 결과를 FieldManager.OnFightChosen() / OnFleeChosen()으로 전달합니다.
    /// - PlayCountdown() 추가: 전투 시작 전 3초 카운트다운 UI 코루틴
    /// </summary>
    public class BattleUIManager : MonoBehaviour
    {
        public static BattleUIManager Instance { get; private set; }

        // ─────────────────────────────────────────
        //  Inspector — EncounterPanel
        // ─────────────────────────────────────────

        [Header("EncounterPanel — 전투/도망 선택창")]
        [SerializeField] private GameObject _encounterPanel;
        [SerializeField] private TMP_Text _encounterNameText;
        [SerializeField] private TMP_Text _encounterEnemyInfoText;
        [SerializeField] private Button _fightButton;
        [SerializeField] private Button _fleeButton;

        // ─────────────────────────────────────────
        //  Inspector — CountdownPanel
        // ─────────────────────────────────────────

        [Header("CountdownPanel — 전투 시작 카운트다운")]
        [Tooltip("카운트다운 숫자를 표시할 TMP_Text.\n" +
                 "없으면 카운트다운 UI 없이 딜레이만 적용됩니다.")]
        [SerializeField] private TMP_Text _countdownText;

        [Tooltip("카운트다운 패널 루트 (없어도 동작합니다).")]
        [SerializeField] private GameObject _countdownPanel;

        // ─────────────────────────────────────────
        //  Inspector — VictoryPanel
        // ─────────────────────────────────────────

        [Header("VictoryPanel — 배틀 결산창")]
        [SerializeField] private GameObject _victoryPanel;
        [SerializeField] private TMP_Text _victoryKillText;
        [SerializeField] private TMP_Text _victorySentryStrikeText;
        [SerializeField] private TMP_Text _victorySentryShootText;
        [SerializeField] private TMP_Text _victorySentryWallText;
        [SerializeField] private Button _victoryConfirmButton;

        // ─────────────────────────────────────────
        //  Inspector — DefeatPanel
        // ─────────────────────────────────────────

        [Header("DefeatPanel — 패배 안내창")]
        [SerializeField] private GameObject _defeatPanel;
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
            if (_encounterPanel != null) _encounterPanel.SetActive(false);
            if (_victoryPanel != null) _victoryPanel.SetActive(false);
            if (_defeatPanel != null) _defeatPanel.SetActive(false);
            if (_countdownPanel != null) _countdownPanel.SetActive(false);

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
        //  배틀 HUD 슬라이드
        // ─────────────────────────────────────────

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
        /// (BattleTrigger를 통하지 않습니다.)
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

        public void HideEncounterPanel()
        {
            if (_encounterPanel == null) return;
            _encounterPanel.transform
                .DOScale(Vector3.zero, 0.2f)
                .SetEase(Ease.InBack)
                .OnComplete(() => _encounterPanel.SetActive(false));
        }

        private void OnFightButtonClicked()
        {
            HideEncounterPanel();
            // FieldManager가 카운트다운 → StartBattle() 순서로 진행
            FieldManager.Instance?.OnFightChosen();
        }

        private void OnFleeButtonClicked()
        {
            HideEncounterPanel();
            FieldManager.Instance?.OnFleeChosen();
        }

        // ─────────────────────────────────────────
        //  카운트다운 (전투 시작 전)
        // ─────────────────────────────────────────

        /// <summary>
        /// 전투 시작 전 카운트다운을 재생합니다.
        /// FieldManager.CountdownAndStartBattle()에서 yield return으로 대기합니다.
        /// </summary>
        /// <param name="duration">카운트다운 총 시간 (초). 정수로 반올림하여 표시합니다.</param>
        public IEnumerator PlayCountdown(float duration)
        {
            if (_countdownPanel != null) _countdownPanel.SetActive(true);

            int count = Mathf.RoundToInt(duration);
            for (int i = count; i >= 1; i--)
            {
                if (_countdownText != null)
                {
                    _countdownText.text = i.ToString();
                    _countdownText.transform.localScale = Vector3.one * 1.5f;
                    _countdownText.transform
                        .DOScale(Vector3.one, 0.8f)
                        .SetEase(Ease.OutBack);
                }
                yield return new WaitForSeconds(1f);
            }

            if (_countdownText != null) _countdownText.text = "전투!";
            yield return new WaitForSeconds(0.5f);

            if (_countdownPanel != null) _countdownPanel.SetActive(false);
        }

        // ─────────────────────────────────────────
        //  VictoryPanel
        // ─────────────────────────────────────────

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
        //  DefeatPanel
        // ─────────────────────────────────────────

        public void ShowDefeatPanel()
        {
            if (_defeatPanel == null) return;
            _defeatPanel.SetActive(true);
            _defeatPanel.transform.localScale = Vector3.zero;
            _defeatPanel.transform.DOScale(Vector3.one, 0.3f).SetEase(Ease.OutBack);
        }

        public void HideResultPanels()
        {
            if (_victoryPanel != null) _victoryPanel.SetActive(false);
            if (_defeatPanel != null) _defeatPanel.SetActive(false);
        }

        private void OnReturnButtonClicked()
        {
            BattleManager.Instance?.ReturnToFieldFromResult();
        }

        // ─────────────────────────────────────────
        //  레벨업 연출
        // ─────────────────────────────────────────

        public void PlayLevelUpEffect(string sentryName, int newLevel)
        {
            PlayerBattleUIManager.Instance?.PlayLevelUpEffect(sentryName, newLevel);
        }
    }
}