using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;

namespace SENTRY
{
    /// <summary>
    /// 배틀 필드 전투 전체를 조율하는 싱글턴 매니저.
    ///
    /// [변경 사항 - 페이크 쿼터뷰 대응]
    /// - PlaceSentriesAtBattleStart()에서 DOMove 이전에
    ///   각 센트리의 EnterBattlePhysics()를 먼저 호출합니다.
    ///   중력 OFF + Kinematic 전환 후에 DOMove를 실행해야
    ///   이동 도중 중력에 끌려 내려가는 현상을 방지할 수 있습니다.
    /// - EndBattle()에서 FieldManager.ReturnToField() 이전에
    ///   각 센트리의 ExitBattlePhysics()를 호출하여
    ///   탐색 필드 복귀 시 Dynamic + 원래 중력값으로 복원합니다.
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

        [Header("배틀 설정")]
        [Tooltip("배틀 시작 전 센트리 등장 연출 대기 시간 (초)")]
        [SerializeField] private float _battleStartDelay = 1.5f;

        [Header("센트리 참조")]
        [Tooltip("타격 센트리")]
        [SerializeField] private StrikeSentry _strikeSentry;

        [Tooltip("사격 센트리")]
        [SerializeField] private ShootSentry _shootSentry;

        [Tooltip("벽 센트리")]
        [SerializeField] private WallSentry _wallSentry;

        [Header("스포너 참조")]
        [Tooltip("배틀 필드의 EnemySpawner.\n" +
                 "BattleField 루트 하위에 배치하세요.")]
        [SerializeField] private EnemySpawner _enemySpawner;

        [Header("배틀 진입 포지션")]
        [Tooltip("배틀 시작 시 센트리 초기 배치 위치.\n" +
                 "[0]=Strike / [1]=Shoot / [2]=Wall\n" +
                 "쿼터뷰 기준 아군 진영에 배치하세요.\n" +
                 "이후 전투 중에는 AI가 자율적으로 이동합니다.")]
        [SerializeField] private Transform[] _sentryBattleStartPositions;

        [Tooltip("배틀 시작 시 플레이어가 이동할 후방 관전 위치")]
        [SerializeField] private Transform _playerBattlePosition;

        // ─────────────────────────────────────────
        //  내부 상태 변수
        // ─────────────────────────────────────────

        /// <summary>현재 배틀 진행 여부</summary>
        private bool _isInBattle = false;

        /// <summary>현재 처치 수</summary>
        private int _currentKillCount = 0;

        /// <summary>현재 인카운터의 클리어 목표 처치 수</summary>
        private int _killCountToWin = 0;

        /// <summary>플레이어 Transform</summary>
        private Transform _playerTransform;

        /// <summary>플레이어 체력 컴포넌트</summary>
        private PlayerHealth _playerHealth;

        /// <summary>현재 실행 중인 인카운터 데이터</summary>
        private BattleEncounterDataSO _currentEncounterData;

        // ─────────────────────────────────────────
        //  외부 공개 프로퍼티
        // ─────────────────────────────────────────

        /// <summary>현재 배틀 진행 여부</summary>
        public bool IsInBattle => _isInBattle;

        /// <summary>현재 처치 수</summary>
        public int KillCount => _currentKillCount;

        /// <summary>클리어 목표 처치 수</summary>
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
        /// 배틀을 시작합니다.
        /// BattleTrigger → FieldManager 이후 호출됩니다.
        /// </summary>
        /// <param name="player">플레이어 Transform</param>
        /// <param name="encounterData">이번 배틀의 인카운터 구성 SO</param>
        public void StartBattle(Transform player, BattleEncounterDataSO encounterData)
        {
            if (_isInBattle) return;
            if (GameManager.Instance != null && GameManager.Instance.IsGameOver) return;
            if (encounterData == null)
            {
                Debug.LogWarning("[BattleManager] encounterData가 null입니다. " +
                                 "배틀을 시작할 수 없습니다.");
                return;
            }

            _playerTransform = player;
            _playerHealth = player.GetComponentInChildren<PlayerHealth>();
            _isInBattle = true;
            _currentKillCount = 0;
            _currentEncounterData = encounterData;
            _killCountToWin = encounterData.GetKillCountToWin();

            ComboManager.Instance?.OnBattleStart();
            UIManager.Instance?.UpdateKillCount(0, _killCountToWin);

            Debug.Log($"<color=cyan>[BattleManager] 배틀 시작! " +
                      $"인카운터: {encounterData.encounterName} " +
                      $"/ 목표: {_killCountToWin}마리</color>");

            StartCoroutine(BattleStartRoutine());
        }

        /// <summary>
        /// 배틀 시작 루틴.
        /// 물리 전환 → 스폰 위치 이동 → 딜레이 → AI 활성 → 적 소환 순서로 진행합니다.
        /// </summary>
        private IEnumerator BattleStartRoutine()
        {
            // ① 물리 전환 + 스폰 위치 이동 (DOMove 이전에 반드시 Kinematic 전환)
            PlaceSentriesAtBattleStart();

            // ② 등장 연출 대기
            yield return new WaitForSeconds(_battleStartDelay);

            // ③ 각 센트리 전투 AI 활성화
            if (_strikeSentry != null && !_strikeSentry.IsKnockedOut)
                _strikeSentry.EnterBattle();
            if (_shootSentry != null && !_shootSentry.IsKnockedOut)
                _shootSentry.EnterBattle();
            if (_wallSentry != null && !_wallSentry.IsKnockedOut)
                _wallSentry.EnterBattle();

            // ④ EnemySpawner에 인카운터 데이터 전달 → 적 소환 시작
            if (_enemySpawner != null)
                _enemySpawner.SpawnStart(_currentEncounterData, _playerTransform);
        }

        /// <summary>
        /// 배틀 시작 시 센트리를 쿼터뷰 아군 진영 초기 위치에 배치합니다.
        ///
        /// [페이크 쿼터뷰 핵심 순서]
        ///   1. EnterBattlePhysics() — 중력 OFF + Kinematic 전환
        ///   2. DOMove() — 스폰 위치로 이동 연출
        ///   이 순서를 반드시 지켜야 합니다.
        ///   순서가 바뀌면 DOMove 도중 중력에 끌려 바닥으로 떨어집니다.
        /// </summary>
        private void PlaceSentriesAtBattleStart()
        {
            if (_sentryBattleStartPositions == null ||
                _sentryBattleStartPositions.Length < 3)
            {
                Debug.LogWarning("[BattleManager] _sentryBattleStartPositions가 " +
                                 "3개 미만입니다. Inspector를 확인하세요.");
                return;
            }

            // ── Step 1: 물리 전환 (DOMove 이전 필수) ──
            if (_strikeSentry != null) _strikeSentry.EnterBattlePhysics();
            if (_shootSentry != null) _shootSentry.EnterBattlePhysics();
            if (_wallSentry != null) _wallSentry.EnterBattlePhysics();

            // ── Step 2: 스폰 위치로 DOMove 등장 연출 ──
            if (_strikeSentry != null && _sentryBattleStartPositions[0] != null)
                _strikeSentry.transform
                    .DOMove(_sentryBattleStartPositions[0].position, 0.8f)
                    .SetEase(Ease.OutBack);

            if (_shootSentry != null && _sentryBattleStartPositions[1] != null)
                _shootSentry.transform
                    .DOMove(_sentryBattleStartPositions[1].position, 0.8f)
                    .SetEase(Ease.OutBack)
                    .SetDelay(0.1f);

            if (_wallSentry != null && _sentryBattleStartPositions[2] != null)
                _wallSentry.transform
                    .DOMove(_sentryBattleStartPositions[2].position, 0.8f)
                    .SetEase(Ease.OutBack)
                    .SetDelay(0.2f);

            // ── Step 3: 플레이어 관전 위치 이동 ──
            if (_playerBattlePosition != null && _playerTransform != null)
                _playerTransform
                    .DOMove(_playerBattlePosition.position, 0.6f)
                    .SetEase(Ease.OutSine);
        }

        // ─────────────────────────────────────────
        //  적 사망 처리
        // ─────────────────────────────────────────

        /// <summary>
        /// 적이 사망할 때 Enemy.Die()에서 호출됩니다.
        /// 경험치 분배 + 콤보 게이지 충전 + 킬 카운트 체크를 수행합니다.
        /// </summary>
        /// <param name="expAmount">사망한 적의 경험치 보상</param>
        public void OnEnemyDied(int expAmount)
        {
            if (!_isInBattle) return;

            _currentKillCount++;
            _enemySpawner?.NotifyEnemyDied();

            // 경험치를 생존 센트리에게 분배
            DistributeExp(expAmount);

            // 콤보 게이지 충전
            ComboManager.Instance?.OnEnemyKilled();

            // HUD 갱신
            UIManager.Instance?.UpdateKillCount(_currentKillCount, _killCountToWin);

            Debug.Log($"[BattleManager] 킬 카운트: {_currentKillCount} / {_killCountToWin}");

            if (_currentKillCount >= _killCountToWin)
                EndBattle(isVictory: true);
        }

        /// <summary>
        /// 경험치를 생존 중인 센트리에게 균등 분배합니다.
        /// </summary>
        private void DistributeExp(int totalExp)
        {
            var alive = new System.Collections.Generic.List<SentryBase>();
            if (_strikeSentry != null && !_strikeSentry.IsKnockedOut) alive.Add(_strikeSentry);
            if (_shootSentry != null && !_shootSentry.IsKnockedOut) alive.Add(_shootSentry);
            if (_wallSentry != null && !_wallSentry.IsKnockedOut) alive.Add(_wallSentry);

            if (alive.Count == 0) return;

            int share = Mathf.Max(1, totalExp / alive.Count);
            foreach (var sentry in alive)
                sentry.AddExp(share);
        }

        // ─────────────────────────────────────────
        //  배틀 종료
        // ─────────────────────────────────────────

        /// <summary>
        /// 배틀을 종료합니다.
        /// 승리 시 FieldManager.ReturnToField()를 호출하고,
        /// 패배 시 GameManager.GameOver()를 호출합니다.
        /// </summary>
        /// <param name="isVictory">true = 클리어, false = 전멸 패배</param>
        public void EndBattle(bool isVictory)
        {
            if (!_isInBattle) return;
            _isInBattle = false;

            _enemySpawner?.SpawnStop();
            ComboManager.Instance?.OnBattleEnd();

            // ── 센트리 AI 정지 ──
            if (_strikeSentry != null) _strikeSentry.ExitBattle();
            if (_shootSentry != null) _shootSentry.ExitBattle();
            if (_wallSentry != null) _wallSentry.ExitBattle();

            // ── 물리 복원 (탐색 필드 복귀 전 반드시 실행) ──
            // Dynamic + 원래 gravityScale로 복원하여
            // 탐색 필드에서 정상적인 2D 사이드뷰 물리가 동작하게 합니다.
            if (_strikeSentry != null) _strikeSentry.ExitBattlePhysics();
            if (_shootSentry != null) _shootSentry.ExitBattlePhysics();
            if (_wallSentry != null) _wallSentry.ExitBattlePhysics();

            if (isVictory)
            {
                Debug.Log("<color=lime>[BattleManager] 배틀 클리어!</color>");
                UIManager.Instance?.SetBattleHudActive(false);

                FieldManager.Instance?.ReturnToField(
                    _strikeSentry?.transform,
                    _shootSentry?.transform,
                    _wallSentry?.transform);
            }
            else
            {
                Debug.Log("<color=red>[BattleManager] 전멸 — 게임오버</color>");
                GameManager.Instance?.GameOver(false);
            }
        }

        /// <summary>
        /// 플레이어 사망 시 PlayerHealth에서 호출합니다.
        /// </summary>
        public void OnPlayerDied()
        {
            if (!_isInBattle) return;
            EndBattle(isVictory: false);
        }
    }
}