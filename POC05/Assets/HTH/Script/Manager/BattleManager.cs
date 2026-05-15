using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;

namespace SENTRY
{
    /// <summary>
    /// 배틀 필드 전투를 조율하는 싱글턴 매니저.
    ///
    /// [물리 전환 흐름 정리]
    ///
    ///   FieldManager.SetupSentriesForDrop()
    ///     → EnterBattlePhysics() : Kinematic ON, gravityScale = 0
    ///     → SetupForBattle()     : 전투 플래그 설정 (_isBattlePhysics는 건드리지 않음)
    ///
    ///   BattleManager.BattleStartRoutine()
    ///     → SetupForBattle() 호출 없음 (Kinematic 상태를 건드리지 않음)
    ///     → EnterBattle() : AI 활성화만 수행
    ///
    ///   BattleManager.EndBattle()
    ///     → ExitBattlePhysics() : Dynamic 복원, gravityScale 복원
    ///
    /// [소환 흐름]
    ///   SpawnWithDropEffect() 1회가 전부. 추가 소환 없음.
    ///
    /// [히어라키 위치]
    /// --- Managers ---
    ///   └── BattleManager (이 스크립트)
    /// </summary>
    public class BattleManager : MonoBehaviour
    {
        // ─────────────────────────────────────────
        //  싱글턴
        // ─────────────────────────────────────────

        /// <summary>씬 어디서든 BattleManager.Instance로 접근합니다.</summary>
        public static BattleManager Instance { get; private set; }

        // ─────────────────────────────────────────
        //  Inspector 노출 필드
        // ─────────────────────────────────────────

        [Header("센트리 참조")]
        [Tooltip("타격 센트리")]
        [SerializeField] private StrikeSentry _strikeSentry;

        [Tooltip("사격 센트리")]
        [SerializeField] private ShootSentry _shootSentry;

        [Tooltip("벽 센트리")]
        [SerializeField] private WallSentry _wallSentry;

        [Header("플레이어 능력")]
        [Tooltip("PlayerAbility 컴포넌트. 전투 시작/종료 타이밍 전달에 사용합니다.")]
        [SerializeField] private PlayerAbility _playerAbility;

        [Header("스포너 참조")]
        [Tooltip("배틀 필드의 EnemySpawner 컴포넌트.\n" +
                 "EndBattle 시 SpawnStop() 호출용으로만 사용합니다.")]
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
        /// 센트리는 이미 FieldManager.SetupSentriesForDrop()에서
        /// EnterBattlePhysics() → SetupForBattle() 완료 상태입니다.
        /// 여기서 SetupForBattle()을 다시 호출하면 _isBattlePhysics가 리셋되므로
        /// 절대 호출하지 않습니다.
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

        /// <summary>
        /// 배틀 시작 루틴.
        ///
        /// ★ SetupForBattle() 호출 없음
        ///   이미 FieldManager에서 EnterBattlePhysics() → SetupForBattle() 완료됨.
        ///   여기서 다시 호출하면 SentryBase.SetupForBattle() 내부의
        ///   _isBattlePhysics = false 가 Kinematic을 해제해버립니다.
        ///   (SentryBase.SetupForBattle()에서도 해당 줄을 제거해야 합니다 — 패치 참고)
        ///
        /// [실행 순서]
        ///   1. 센트리 AI 활성화 (EnterBattle) — _isInBattle = true, StopFollowing()
        ///   2. 씬 내 모든 Enemy.ActivateBattleAI() — 적 AI 일괄 활성화
        /// </summary>
        private IEnumerator BattleStartRoutine()
        {
            _playerAbility?.OnBattleStart();

            // ① 센트리 AI 활성화
            // SetupForBattle()은 FieldManager에서 이미 완료됐으므로 호출하지 않습니다.
            // EnterBattle()만 호출해 전투 AI를 켭니다.
            if (_strikeSentry != null && !_strikeSentry.IsKnockedOut)
                _strikeSentry.EnterBattle();
            if (_shootSentry != null && !_shootSentry.IsKnockedOut)
                _shootSentry.EnterBattle();
            if (_wallSentry != null && !_wallSentry.IsKnockedOut)
                _wallSentry.EnterBattle();

            // ② 씬 내 모든 Enemy AI 활성화
            // SpawnWithDropEffect()로 소환된 적들은 _isBattleStarted = false 상태입니다.
            // ActivateBattleAI() 호출 이후부터 이동·공격이 시작됩니다.
            Enemy[] enemies = FindObjectsByType<Enemy>(FindObjectsSortMode.None);
            foreach (Enemy enemy in enemies)
            {
                if (!enemy.IsDead)
                    enemy.ActivateBattleAI();
            }

            Debug.Log($"[BattleManager] 센트리 AI 활성화 / 적 AI 활성화 — {enemies.Length}마리");

            yield return null;
        }

        // ─────────────────────────────────────────
        //  센트리 KO 체크
        // ─────────────────────────────────────────

        /// <summary>
        /// 센트리 KO 시 SentryBase.KnockOut()에서 호출합니다.
        /// 전원 KO 시 패배 처리합니다.
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

        /// <summary>
        /// 적 사망 시 Enemy.Die()에서 호출합니다.
        /// 킬 카운트 증가 → 경험치 분배 → 승리 판정 순서로 처리합니다.
        /// </summary>
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

        /// <summary>획득 경험치를 생존 센트리에게 균등 분배합니다.</summary>
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

        /// <summary>
        /// 승리 또는 패배 시 배틀을 종료합니다.
        /// ExitBattlePhysics()로 센트리 Rigidbody를 Dynamic으로 복원합니다.
        /// </summary>
        public void EndBattle(bool isVictory)
        {
            if (!_isInBattle) return;
            _isInBattle = false;

            _enemySpawner?.SpawnStop();
            _playerAbility?.OnBattleEnd();
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
        //  결산 데이터 생성
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

            Add(_strikeSentry);
            Add(_shootSentry);
            Add(_wallSentry);
            return result;
        }

        // ─────────────────────────────────────────
        //  결과 버튼 콜백
        // ─────────────────────────────────────────

        /// <summary>
        /// VictoryPanel [확인] / DefeatPanel [돌아가기] 버튼 클릭 시 호출됩니다.
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