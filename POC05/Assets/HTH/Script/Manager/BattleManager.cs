using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;

namespace SENTRY
{
    /// <summary>
    /// 배틀 필드 전투를 조율하는 싱글턴 매니저.
    ///
    /// [새로운 진입 흐름에서의 역할]
    ///
    ///   FieldManager.BattleSequenceRoutine()이 낙하 연출과 선택창을 담당합니다.
    ///   [전투하기] 선택 → FieldManager.OnFightChosen() → 카운트다운
    ///   → BattleManager.StartBattle() 호출 (이 클래스)
    ///
    ///   BattleManager는 전투 진행(AI 활성화, 킬카운트, 승패 판정)만 담당합니다.
    ///   센트리 낙하 연출, 적 낙하 소환은 FieldManager / EnemySpawner가 담당합니다.
    ///
    /// [StartBattle() 역할]
    ///   센트리는 이미 낙하 완료 상태이므로 DOMove 등장 연출 없이
    ///   SetupForBattle() → EnterBattle(AI 활성) → SpawnStart(순환 소환)만 실행합니다.
    /// </summary>
    public class BattleManager : MonoBehaviour
    {
        public static BattleManager Instance { get; private set; }

        // ─────────────────────────────────────────
        //  Inspector 노출 필드
        // ─────────────────────────────────────────

        [Header("센트리 참조")]
        [SerializeField] private StrikeSentry _strikeSentry;
        [SerializeField] private ShootSentry _shootSentry;
        [SerializeField] private WallSentry _wallSentry;

        [Header("스포너 참조")]
        [SerializeField] private EnemySpawner _enemySpawner;

        // ─────────────────────────────────────────
        //  내부 상태 변수
        // ─────────────────────────────────────────

        private bool _isInBattle = false;
        private int _currentKillCount = 0;
        private int _killCountToWin = 0;
        private Transform _playerTransform;
        private BattleEncounterDataSO _currentEncounterData;
        private Dictionary<string, int> _expGainedThisBattle = new();

        // ─────────────────────────────────────────
        //  외부 공개 프로퍼티
        // ─────────────────────────────────────────

        public bool IsInBattle => _isInBattle;
        public int KillCount => _currentKillCount;
        public int KillTarget => _killCountToWin;

        // ─────────────────────────────────────────
        //  유니티 생명주기
        // ─────────────────────────────────────────

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);
        }

        // ─────────────────────────────────────────
        //  배틀 시작
        // ─────────────────────────────────────────

        /// <summary>
        /// [전투하기] 선택 → FieldManager.CountdownAndStartBattle() → 이 메서드 호출.
        ///
        /// 센트리와 적은 이미 낙하 완료 상태이므로
        /// DOMove 등장 연출 없이 AI 활성화 → 지속 소환 시작 순서로 진행합니다.
        /// </summary>
        public void StartBattle(Transform player, BattleEncounterDataSO encounterData)
        {
            if (_isInBattle) return;
            if (encounterData == null)
            {
                Debug.LogWarning("[BattleManager] encounterData가 null입니다.");
                return;
            }

            _playerTransform = player;
            _isInBattle = true;
            _currentKillCount = 0;
            _currentEncounterData = encounterData;
            _killCountToWin = encounterData.GetKillCountToWin();
            _expGainedThisBattle.Clear();

            SentryComboManager.Instance?.OnBattleStart();

            Debug.Log($"<color=cyan>[BattleManager] 배틀 시작! " +
                      $"인카운터: {encounterData.encounterName} " +
                      $"/ 목표: {_killCountToWin}</color>");

            StartCoroutine(BattleStartRoutine());
        }

        private IEnumerator BattleStartRoutine()
        {
            // ① 배틀 상태 초기화 (레벨·EXP·HP 유지)
            // 센트리는 FieldManager.SetupSentriesForDrop()에서 이미 SetupForBattle() 완료
            // 여기서는 전투 플래그 갱신만 수행
            if (_strikeSentry != null) _strikeSentry.SetupForBattle(_playerTransform);
            if (_shootSentry != null) _shootSentry.SetupForBattle(_playerTransform);
            if (_wallSentry != null) _wallSentry.SetupForBattle(_playerTransform);

            // ② 센트리 AI 활성화 (이미 낙하 완료 위치에 있음)
            if (_strikeSentry != null && !_strikeSentry.IsKnockedOut) _strikeSentry.EnterBattle();
            if (_shootSentry != null && !_shootSentry.IsKnockedOut) _shootSentry.EnterBattle();
            if (_wallSentry != null && !_wallSentry.IsKnockedOut) _wallSentry.EnterBattle();

            // ③ 지속 소환 시작 (낙하 연출로 소환된 초기 적과 별개로 추가 소환)
            // SpawnWithDropEffect로 소환된 적은 이미 등록됐으므로
            // SpawnStart는 필요시 추가 웨이브 소환에 사용합니다.
            // 인카운터 설계에 따라 비활성화 가능합니다.
            _enemySpawner?.SpawnStart(_currentEncounterData, _playerTransform);

            yield return null;
        }

        // ─────────────────────────────────────────
        //  센트리 KO 체크
        // ─────────────────────────────────────────

        /// <summary>센트리 KO 시 SentryBase.KnockOut()에서 호출합니다.</summary>
        public void OnSentryKnockedOut()
        {
            if (!_isInBattle) return;

            bool strikeKO = _strikeSentry == null || _strikeSentry.IsKnockedOut;
            bool shootKO = _shootSentry == null || _shootSentry.IsKnockedOut;
            bool wallKO = _wallSentry == null || _wallSentry.IsKnockedOut;

            if (strikeKO && shootKO && wallKO)
            {
                Debug.Log("<color=red>[BattleManager] 전원 KO — 패배</color>");
                EndBattle(isVictory: false);
            }
        }

        // ─────────────────────────────────────────
        //  적 사망 처리
        // ─────────────────────────────────────────

        public void OnEnemyDied(int expAmount)
        {
            if (!_isInBattle) return;

            _currentKillCount++;
            _enemySpawner?.NotifyEnemyDied();
            DistributeExp(expAmount);
            SentryComboManager.Instance?.OnEnemyKilled();

            Debug.Log($"[BattleManager] 처치 {_currentKillCount}/{_killCountToWin}");

            if (_currentKillCount >= _killCountToWin)
                EndBattle(isVictory: true);
        }

        private void DistributeExp(int totalExp)
        {
            var alive = new List<SentryBase>();
            if (_strikeSentry != null && !_strikeSentry.IsKnockedOut) alive.Add(_strikeSentry);
            if (_shootSentry != null && !_shootSentry.IsKnockedOut) alive.Add(_shootSentry);
            if (_wallSentry != null && !_wallSentry.IsKnockedOut) alive.Add(_wallSentry);
            if (alive.Count == 0) return;

            int share = Mathf.Max(1, totalExp / alive.Count);
            foreach (var s in alive)
            {
                s.AddExp(share);
                if (!_expGainedThisBattle.ContainsKey(s.SentryName))
                    _expGainedThisBattle[s.SentryName] = 0;
                _expGainedThisBattle[s.SentryName] += share;
            }
        }

        // ─────────────────────────────────────────
        //  배틀 종료
        // ─────────────────────────────────────────

        public void EndBattle(bool isVictory)
        {
            if (!_isInBattle) return;
            _isInBattle = false;

            _enemySpawner?.SpawnStop();
            SentryComboManager.Instance?.OnBattleEnd();

            if (_strikeSentry != null) { _strikeSentry.ExitBattle(); _strikeSentry.ExitBattlePhysics(); }
            if (_shootSentry != null) { _shootSentry.ExitBattle(); _shootSentry.ExitBattlePhysics(); }
            if (_wallSentry != null) { _wallSentry.ExitBattle(); _wallSentry.ExitBattlePhysics(); }

            if (isVictory)
            {
                Debug.Log("<color=lime>[BattleManager] 클리어!</color>");
                BattleUIManager.Instance?.ShowVictoryPanel(BuildBattleResult());
            }
            else
            {
                Debug.Log("<color=red>[BattleManager] 패배</color>");
                BattleUIManager.Instance?.ShowDefeatPanel();
            }
        }

        // ─────────────────────────────────────────
        //  결산 데이터
        // ─────────────────────────────────────────

        private BattleResultData BuildBattleResult()
        {
            var result = new BattleResultData
            {
                killCount = _currentKillCount,
                sentryResults = new List<SentryResultData>()
            };

            void Add(SentryBase s)
            {
                if (s == null) return;
                _expGainedThisBattle.TryGetValue(s.SentryName, out int gained);
                result.sentryResults.Add(new SentryResultData
                {
                    sentryName = s.SentryName,
                    level = s.CurrentLevel,
                    expGained = gained,
                    currentExp = s.CurrentExp,
                    requiredExp = s.RequiredExp,
                    currentHp = s.CurrentHp,
                    maxHp = s.MaxHp,
                    isKnockedOut = s.IsKnockedOut
                });
            }

            Add(_strikeSentry); Add(_shootSentry); Add(_wallSentry);
            return result;
        }

        // ─────────────────────────────────────────
        //  결산창/패배 패널 버튼 콜백 (BattleUIManager에서 호출)
        // ─────────────────────────────────────────

        public void ReturnToFieldFromResult()
        {
            BattleUIManager.Instance?.HideResultPanels();
            FieldManager.Instance?.ReturnToField(
                _strikeSentry?.transform,
                _shootSentry?.transform,
                _wallSentry?.transform);
        }
    }

    // ─────────────────────────────────────────
    //  결산 데이터 구조체
    // ─────────────────────────────────────────

    [System.Serializable]
    public class BattleResultData
    {
        public int killCount;
        public List<SentryResultData> sentryResults;
    }

    [System.Serializable]
    public class SentryResultData
    {
        public string sentryName;
        public int level;
        public int expGained;
        public int currentExp;
        public int requiredExp;
        public int currentHp;
        public int maxHp;
        public bool isKnockedOut;
    }
}