using UnityEngine;
using System.Collections;

namespace SENTRY
{
    /// <summary>
    /// 배틀 필드에서 BattleEncounterDataSO의 정보를 기반으로 적을 소환하는 스포너.
    ///
    /// [변경 사항 - Revision3]
    /// 기존: 일정 시간 간격으로 무한 소환 (spawnInterval 방식)
    /// 변경: BattleEncounterDataSO에 정의된 적 목록을 순서대로 소환
    ///   - 각 EnemySpawnEntry의 spawnDelay / spawnInterval에 따라 타이밍 제어
    ///   - 모든 적을 소환하면 스폰이 종료됩니다.
    ///   - 처치 수가 killCountToWin에 도달하면 BattleManager가 배틀 종료 처리
    ///
    /// [소환 흐름]
    /// SpawnStart(encounterData, player)
    ///   → Entry[0]: spawnDelay 대기 → count마리를 spawnInterval 간격으로 소환
    ///   → Entry[1]: spawnDelay 대기 → count마리를 spawnInterval 간격으로 소환
    ///   → ... 모든 엔트리 완료 → 스폰 종료
    ///
    /// [히어라키 위치]
    /// BattleField
    ///   └── EnemySpawner (이 스크립트)
    ///         └── SpawnPoint_1, SpawnPoint_2 ... (자식 Transform)
    /// </summary>
    public class EnemySpawner : MonoBehaviour
    {
        // ─────────────────────────────────────────
        //  Inspector 노출 필드
        // ─────────────────────────────────────────

        [Header("스폰 포인트")]
        [Tooltip("적이 생성될 위치 목록.\n" +
                 "자식 오브젝트로 추가한 뒤 여기에 드래그하세요.\n" +
                 "엔트리별로 랜덤 포인트가 선택됩니다.")]
        [SerializeField] private Transform[] _spawnPoints;

        // ─────────────────────────────────────────
        //  내부 상태 변수
        // ─────────────────────────────────────────

        /// <summary>현재 실행 중인 인카운터 데이터</summary>
        private BattleEncounterDataSO _encounterData;

        /// <summary>플레이어 Transform (생성된 적에게 전달)</summary>
        private Transform _playerTransform;

        /// <summary>스폰 루프 실행 여부</summary>
        private bool _isSpawning = false;

        /// <summary>현재 생존 중인 적 수</summary>
        private int _aliveEnemyCount = 0;

        /// <summary>현재까지 소환된 총 적 수</summary>
        private int _totalSpawnedCount = 0;

        // ─────────────────────────────────────────
        //  외부 공개 프로퍼티
        // ─────────────────────────────────────────

        /// <summary>현재 생존 적 수 (BattleManager 참조용)</summary>
        public int AliveEnemyCount => _aliveEnemyCount;

        /// <summary>현재까지 소환된 총 적 수</summary>
        public int TotalSpawnedCount => _totalSpawnedCount;

        // ─────────────────────────────────────────
        //  스폰 제어
        // ─────────────────────────────────────────

        /// <summary>
        /// 인카운터 데이터를 기반으로 소환을 시작합니다.
        /// BattleManager.StartBattle()에서 호출합니다.
        /// </summary>
        /// <param name="encounterData">소환할 적 구성 SO</param>
        /// <param name="player">플레이어 Transform (적 AI 초기화용)</param>
        public void SpawnStart(BattleEncounterDataSO encounterData, Transform player)
        {
            if (encounterData == null)
            {
                Debug.LogWarning("[EnemySpawner] EncounterData가 없습니다. 스폰을 시작할 수 없습니다.");
                return;
            }

            _encounterData = encounterData;
            _playerTransform = player;
            _isSpawning = true;
            _aliveEnemyCount = 0;
            _totalSpawnedCount = 0;

            Debug.Log($"[EnemySpawner] 인카운터 시작: {encounterData.encounterName} " +
                      $"/ 총 적 수: {encounterData.GetTotalEnemyCount()}");

            StartCoroutine(SpawnRoutine());
        }

        /// <summary>
        /// 스폰을 강제 중단합니다.
        /// BattleManager.EndBattle()에서 호출합니다.
        /// </summary>
        public void SpawnStop()
        {
            _isSpawning = false;
            StopAllCoroutines();
            Debug.Log("[EnemySpawner] 스폰 중단");
        }

        /// <summary>
        /// 적이 사망했을 때 BattleManager.OnEnemyDied()를 통해 호출됩니다.
        /// 생존 카운터를 감소시킵니다.
        /// </summary>
        public void NotifyEnemyDied()
        {
            _aliveEnemyCount = Mathf.Max(0, _aliveEnemyCount - 1);
        }

        // ─────────────────────────────────────────
        //  소환 루프
        // ─────────────────────────────────────────

        /// <summary>
        /// BattleEncounterDataSO의 spawnEntries를 순서대로 처리합니다.
        /// 각 엔트리의 spawnDelay / spawnInterval에 따라 타이밍을 제어합니다.
        /// </summary>
        private IEnumerator SpawnRoutine()
        {
            if (_encounterData == null) yield break;

            foreach (EnemySpawnEntry entry in _encounterData.spawnEntries)
            {
                // 배틀 종료 또는 게임오버 시 중단
                if (!_isSpawning) yield break;
                if (GameManager.Instance != null && GameManager.Instance.IsGameOver)
                    yield break;

                // 프리팹 미설정 엔트리는 건너뜀
                if (entry.enemyPrefab == null)
                {
                    Debug.LogWarning($"[EnemySpawner] {_encounterData.encounterName} — " +
                                     "enemyPrefab이 null인 엔트리가 있습니다. 건너뜁니다.");
                    continue;
                }

                // 이전 엔트리 이후 대기 (엔트리 간 딜레이)
                if (entry.spawnDelay > 0f)
                    yield return new WaitForSeconds(entry.spawnDelay);

                // 이 엔트리의 count만큼 spawnInterval 간격으로 소환
                for (int i = 0; i < entry.count; i++)
                {
                    if (!_isSpawning) yield break;

                    SpawnEnemy(entry.enemyPrefab);

                    // 마지막 적이 아니면 interval 대기
                    if (i < entry.count - 1 && entry.spawnInterval > 0f)
                        yield return new WaitForSeconds(entry.spawnInterval);
                }
            }

            Debug.Log($"[EnemySpawner] 모든 적 소환 완료 " +
                      $"(총 {_totalSpawnedCount}마리)");
        }

        // ─────────────────────────────────────────
        //  개별 적 생성
        // ─────────────────────────────────────────

        /// <summary>
        /// 랜덤 스폰 포인트에 적을 생성하고 Init을 호출합니다.
        /// </summary>
        /// <param name="prefab">생성할 적 프리팹</param>
        private void SpawnEnemy(GameObject prefab)
        {
            if (_spawnPoints == null || _spawnPoints.Length == 0)
            {
                Debug.LogWarning("[EnemySpawner] SpawnPoint가 없습니다.");
                return;
            }

            // 랜덤 스폰 포인트 선택
            Transform spawnPoint =
                _spawnPoints[Random.Range(0, _spawnPoints.Length)];

            GameObject newEnemy = Instantiate(
                prefab,
                spawnPoint.position,
                Quaternion.identity);

            Enemy enemyScript = newEnemy.GetComponent<Enemy>();
            if (enemyScript != null)
                enemyScript.Init(_playerTransform);

            _aliveEnemyCount++;
            _totalSpawnedCount++;

            Debug.Log($"[EnemySpawner] {prefab.name} 소환 " +
                      $"(생존: {_aliveEnemyCount} / 총 소환: {_totalSpawnedCount})");
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (_spawnPoints == null) return;
            Gizmos.color = Color.red;
            foreach (var point in _spawnPoints)
            {
                if (point != null)
                    Gizmos.DrawSphere(point.position, 0.3f);
            }
        }
#endif
    }
}