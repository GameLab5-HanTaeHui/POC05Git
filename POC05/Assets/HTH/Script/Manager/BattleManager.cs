using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;

namespace SENTRY
{
    /// <summary>
    /// 배틀 필드 전투 전체를 조율하는 싱글턴 매니저.
    ///
    /// [변경 사항]
    /// - 패배 조건: 모든 센트리 KO → UIManager.ShowDefeatPanel()
    /// - 승리 조건: 킬 카운트 달성 → UIManager.ShowVictoryPanel(결산 데이터)
    /// - ReturnToField()는 결산창 / 패배 패널의 버튼 클릭 후 실행됩니다.
    /// - GameManager 의존성 제거 (배틀 단위 패배 처리)
    /// </summary>
    public class BattleManager : MonoBehaviour
    {
        public static BattleManager Instance { get; private set; }

        [Header("배틀 설정")]
        [SerializeField] private float _battleStartDelay = 1.5f;

        [Header("센트리 참조")]
        [SerializeField] private StrikeSentry _strikeSentry;
        [SerializeField] private ShootSentry _shootSentry;
        [SerializeField] private WallSentry _wallSentry;

        [Header("스포너 참조")]
        [SerializeField] private EnemySpawner _enemySpawner;

        [Header("배틀 진입 포지션")]
        [Tooltip("[0]=Strike / [1]=Shoot / [2]=Wall")]
        [SerializeField] private Transform[] _sentryBattleStartPositions;

        [Tooltip("배틀 중 플레이어 관전 위치")]
        [SerializeField] private Transform _playerBattlePosition;

        private bool _isInBattle = false;
        private int _currentKillCount = 0;
        private int _killCountToWin = 0;
        private Transform _playerTransform;
        private BattleEncounterDataSO _currentEncounterData;
        private Dictionary<string, int> _expGainedThisBattle = new();

        public bool IsInBattle => _isInBattle;
        public int KillCount => _currentKillCount;
        public int KillTarget => _killCountToWin;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);
        }

        // ─────────────────────────────────────────
        //  배틀 시작
        // ─────────────────────────────────────────

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
                      $"인카운터: {encounterData.encounterName} / 목표: {_killCountToWin}</color>");

            StartCoroutine(BattleStartRoutine());
        }

        private IEnumerator BattleStartRoutine()
        {
            // ① 배틀 상태 초기화 (레벨·EXP·HP 유지)
            if (_strikeSentry != null) _strikeSentry.SetupForBattle(_playerTransform);
            if (_shootSentry != null) _shootSentry.SetupForBattle(_playerTransform);
            if (_wallSentry != null) _wallSentry.SetupForBattle(_playerTransform);

            // ② 물리 전환 + 스폰 위치 이동
            PlaceSentriesAtBattleStart();

            // ③ 등장 연출 대기
            yield return new WaitForSeconds(_battleStartDelay);

            // ④ 생존 센트리 AI 활성화
            if (_strikeSentry != null && !_strikeSentry.IsKnockedOut) _strikeSentry.EnterBattle();
            if (_shootSentry != null && !_shootSentry.IsKnockedOut) _shootSentry.EnterBattle();
            if (_wallSentry != null && !_wallSentry.IsKnockedOut) _wallSentry.EnterBattle();

            // ⑤ 적 소환 시작
            _enemySpawner?.SpawnStart(_currentEncounterData, _playerTransform);
        }

        // ─────────────────────────────────────────
        //  센트리 배치
        // ─────────────────────────────────────────

        private void PlaceSentriesAtBattleStart()
        {
            if (_sentryBattleStartPositions == null || _sentryBattleStartPositions.Length < 3)
            {
                Debug.LogWarning("[BattleManager] _sentryBattleStartPositions 3개 미만");
                return;
            }

            if (_strikeSentry != null) _strikeSentry.EnterBattlePhysics();
            if (_shootSentry != null) _shootSentry.EnterBattlePhysics();
            if (_wallSentry != null) _wallSentry.EnterBattlePhysics();

            if (_strikeSentry != null && _sentryBattleStartPositions[0] != null)
                _strikeSentry.transform.DOMove(_sentryBattleStartPositions[0].position, 0.8f)
                    .SetEase(Ease.OutBack);
            if (_shootSentry != null && _sentryBattleStartPositions[1] != null)
                _shootSentry.transform.DOMove(_sentryBattleStartPositions[1].position, 0.8f)
                    .SetEase(Ease.OutBack).SetDelay(0.1f);
            if (_wallSentry != null && _sentryBattleStartPositions[2] != null)
                _wallSentry.transform.DOMove(_sentryBattleStartPositions[2].position, 0.8f)
                    .SetEase(Ease.OutBack).SetDelay(0.2f);

            if (_playerBattlePosition != null && _playerTransform != null)
                _playerTransform.DOMove(_playerBattlePosition.position, 0.6f).SetEase(Ease.OutSine);
        }

        // ─────────────────────────────────────────
        //  센트리 KO 체크
        // ─────────────────────────────────────────

        /// <summary>
        /// 센트리 KO 시 각 센트리(SentryBase.KnockOut)에서 호출합니다.
        /// 전원 KO이면 패배 처리합니다.
        /// </summary>
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
                Debug.Log("<color=lime>[BattleManager] 배틀 클리어!</color>");
                BattleUIManager.Instance?.ShowVictoryPanel(BuildBattleResult());
            }
            else
            {
                Debug.Log("<color=red>[BattleManager] 패배</color>");
                BattleUIManager.Instance?.ShowDefeatPanel();
            }
        }

        // ─────────────────────────────────────────
        //  결산 데이터 빌드
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
        //  결산창 / 패배 패널 버튼 콜백
        // ─────────────────────────────────────────

        /// <summary>
        /// 결산창 [확인] 또는 패배 패널 [돌아가기] 버튼 클릭 시
        /// UIManager를 통해 이 메서드가 호출됩니다.
        /// </summary>
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