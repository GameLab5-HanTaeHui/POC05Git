using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;

namespace SENTRY
{
    /// <summary>
    /// 배틀 필드 전투 전체를 조율하는 싱글턴 매니저.
    ///
    /// [변경 사항 - Revision3]
    /// - StartBattle(player, encounterData) 파라미터에
    ///   BattleEncounterDataSO를 추가로 받습니다.
    /// - _killCountToWin을 Inspector 고정값 대신
    ///   encounterData.GetKillCountToWin()으로 동적 설정합니다.
    /// - EnemySpawner.SpawnStart()에 encounterData를 전달합니다.
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
                 "쿼터뷰 기준 아군 진영 좌측에 배치하세요.\n" +
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

        /// <summary>현재 인카운터의 클리어 목표 처치 수 (encounterData에서 동적 설정)</summary>
        private int _killCountToWin = 0;

        private Transform _playerTransform;
        private PlayerHealth _playerHealth;

        /// <summary>현재 실행 중인 인카운터 데이터</summary>
        private BattleEncounterDataSO _currentEncounterData;

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
                Debug.LogWarning("[BattleManager] encounterData가 null입니다. 배틀을 시작할 수 없습니다.");
                return;
            }

            _playerTransform = player;
            _playerHealth = player.GetComponentInChildren<PlayerHealth>();
            _isInBattle = true;
            _currentKillCount = 0;
            _currentEncounterData = encounterData;

            // 클리어 목표 처치 수를 인카운터 데이터에서 동적으로 설정
            _killCountToWin = encounterData.GetKillCountToWin();

            if (ComboManager.Instance != null) ComboManager.Instance.OnBattleStart();

            if (UIManager.Instance != null)
                UIManager.Instance.UpdateKillCount(0, _killCountToWin);

            Debug.Log($"<color=cyan>[BattleManager] 배틀 시작! " +
                      $"인카운터: {encounterData.encounterName} " +
                      $"/ 목표: {_killCountToWin}마리</color>");

            StartCoroutine(BattleStartRoutine());
        }

        private IEnumerator BattleStartRoutine()
        {
            // 센트리를 배틀 초기 포지션으로 이동 (등장 연출)
            PlaceSentriesAtBattleStart();

            yield return new WaitForSeconds(_battleStartDelay);

            // 각 센트리 전투 AI 활성화
            if (_strikeSentry != null && !_strikeSentry.IsKnockedOut)
                _strikeSentry.EnterBattle();
            if (_shootSentry != null && !_shootSentry.IsKnockedOut)
                _shootSentry.EnterBattle();
            if (_wallSentry != null && !_wallSentry.IsKnockedOut)
                _wallSentry.EnterBattle();

            // EnemySpawner에 인카운터 데이터를 전달하여 소환 시작
            if (_enemySpawner != null)
                _enemySpawner.SpawnStart(_currentEncounterData, _playerTransform);
        }

        /// <summary>
        /// 배틀 시작 시 센트리를 쿼터뷰 아군 진영 초기 위치에 배치합니다.
        /// 이후 전투 중 센트리 위치는 AI가 자율적으로 관리합니다.
        /// </summary>
        private void PlaceSentriesAtBattleStart()
        {
            if (_sentryBattleStartPositions == null ||
                _sentryBattleStartPositions.Length < 3) return;

            if (_strikeSentry != null && _sentryBattleStartPositions[0] != null)
                _strikeSentry.transform
                    .DOMove(_sentryBattleStartPositions[0].position, 0.8f)
                    .SetEase(Ease.OutBack);

            if (_shootSentry != null && _sentryBattleStartPositions[1] != null)
                _shootSentry.transform
                    .DOMove(_sentryBattleStartPositions[1].position, 0.8f)
                    .SetEase(Ease.OutBack).SetDelay(0.1f);

            if (_wallSentry != null && _sentryBattleStartPositions[2] != null)
                _wallSentry.transform
                    .DOMove(_sentryBattleStartPositions[2].position, 0.8f)
                    .SetEase(Ease.OutBack).SetDelay(0.2f);

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
        /// <param name="expAmount">분배할 총 경험치</param>
        public void OnEnemyDied(int expAmount)
        {
            if (!_isInBattle) return;

            if (_enemySpawner != null) _enemySpawner.NotifyEnemyDied();

            _currentKillCount++;
            Debug.Log($"[BattleManager] 킬: {_currentKillCount}/{_killCountToWin}");

            DistributeExp(expAmount);

            if (ComboManager.Instance != null) ComboManager.Instance.OnEnemyKilled();

            if (UIManager.Instance != null)
                UIManager.Instance.UpdateKillCount(_currentKillCount, _killCountToWin);

            if (_currentKillCount >= _killCountToWin)
                StartCoroutine(EndBattle(true));
        }

        private void DistributeExp(int totalExp)
        {
            List<SentryBase> aliveList = new List<SentryBase>();
            if (_strikeSentry != null && !_strikeSentry.IsKnockedOut)
                aliveList.Add(_strikeSentry);
            if (_shootSentry != null && !_shootSentry.IsKnockedOut)
                aliveList.Add(_shootSentry);
            if (_wallSentry != null && !_wallSentry.IsKnockedOut)
                aliveList.Add(_wallSentry);

            if (aliveList.Count == 0) return;

            int expPerSentry = totalExp / aliveList.Count;
            foreach (SentryBase s in aliveList)
                s.GainExp(expPerSentry);
        }

        // ─────────────────────────────────────────
        //  배틀 종료
        // ─────────────────────────────────────────

        private IEnumerator EndBattle(bool isVictory)
        {
            if (!_isInBattle) yield break;
            _isInBattle = false;

            if (_enemySpawner != null) _enemySpawner.SpawnStop();

            if (_strikeSentry != null) _strikeSentry.ExitBattle();
            if (_shootSentry != null) _shootSentry.ExitBattle();
            if (_wallSentry != null) _wallSentry.ExitBattle();

            if (ComboManager.Instance != null) ComboManager.Instance.OnBattleEnd();

            if (isVictory)
            {
                Debug.Log("<color=lime>[BattleManager] 배틀 승리!</color>");
                yield return new WaitForSeconds(1.5f);

                // 저장된 탐색 필드 위치로 복귀
                if (FieldManager.Instance != null)
                    FieldManager.Instance.ReturnToField(
                        _strikeSentry != null ? _strikeSentry.transform : null,
                        _shootSentry != null ? _shootSentry.transform : null,
                        _wallSentry != null ? _wallSentry.transform : null);
            }
            else
            {
                Debug.Log("<color=red>[BattleManager] 배틀 패배</color>");
                if (GameManager.Instance != null)
                    GameManager.Instance.GameOver(false);
            }
        }

        /// <summary>외부(플레이어 사망 등)에서 강제 패배 처리합니다.</summary>
        public void ForceLoseBattle()
        {
            if (_isInBattle) StartCoroutine(EndBattle(false));
        }
    }
}