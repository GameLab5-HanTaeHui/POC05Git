using DG.Tweening;
using System.Collections;
using UnityEngine;

namespace SENTRY
{
    /// <summary>
    /// 배틀 필드에서 BattleEncounterDataSO의 정보를 기반으로 적을 소환하는 스포너.
    ///
    /// [설계 원칙]
    /// 소환은 SpawnWithDropEffect() 1회가 전부입니다.
    /// 배틀 진입 시 FieldManager.BattleSequenceRoutine()에서 호출되며
    /// 이후 추가 소환은 일절 없습니다.
    ///
    /// SpawnStart() / SpawnRoutine() / SpawnEnemy() 는 제거됐습니다.
    ///
    /// [소환 규칙]
    ///   - SpawnPoint를 인덱스 순서대로 1개씩 사용 (중복 방지)
    ///   - 최대 소환 수 = _spawnPoints.Length (초과 시 중단)
    ///   - 소환 직후 X Rotation = _enemyRotationX (-50) 적용 (쿼터뷰 카메라 보정)
    ///
    /// [히어라키 위치]
    /// BattleField
    ///   └── EnemySpawner (이 스크립트)
    ///         ├── SpawnPoint_1  ← _spawnPoints[0]
    ///         ├── SpawnPoint_2  ← _spawnPoints[1]
    ///         └── SpawnPoint_3  ← _spawnPoints[2]
    /// </summary>
    public class EnemySpawner : MonoBehaviour
    {
        // ─────────────────────────────────────────
        //  Inspector 노출 필드
        // ─────────────────────────────────────────

        [Header("스폰 포인트")]
        [Tooltip("적이 생성될 위치 목록.\n" +
                 "인덱스 순서대로 1개씩 사용하며 각 포인트에는 최대 1마리만 배치됩니다.")]
        [SerializeField] private Transform[] _spawnPoints;

        [Header("쿼터뷰 회전 보정")]
        [Tooltip("소환 직후 적 오브젝트에 적용할 X Rotation.\n" +
                 "카메라 X Rotation이 -50이면 이 값을 -50으로 설정하세요.")]
        [SerializeField] private float _enemyRotationX = -50f;

        // ─────────────────────────────────────────
        //  내부 상태 변수
        // ─────────────────────────────────────────

        /// <summary>현재 생존 중인 적 수</summary>
        private int _aliveEnemyCount = 0;

        /// <summary>현재까지 소환된 총 적 수</summary>
        private int _totalSpawnedCount = 0;

        // ─────────────────────────────────────────
        //  내부 캐시
        // ─────────────────────────────────────────

        /// <summary>
        /// SpawnWithDropEffect() 시점에 씬에서 직접 찾아 캐싱합니다.
        /// BattleField 하위 오브젝트가 비활성이어서 Instance가 null인 경우를 방지합니다.
        /// </summary>
        private EnemyComboManager _enemyComboManager;
        private EnemyBattleUIManager _enemyBattleUIManager;

        // ─────────────────────────────────────────
        //  외부 공개 프로퍼티
        // ─────────────────────────────────────────

        /// <summary>현재 생존 적 수</summary>
        public int AliveEnemyCount => _aliveEnemyCount;

        /// <summary>현재까지 소환된 총 적 수</summary>
        public int TotalSpawnedCount => _totalSpawnedCount;

        // ─────────────────────────────────────────
        //  스폰 중단 (배틀 종료 시)
        // ─────────────────────────────────────────

        /// <summary>
        /// 진행 중인 코루틴을 중단합니다.
        /// BattleManager.EndBattle()에서 호출합니다.
        /// </summary>
        public void SpawnStop()
        {
            StopAllCoroutines();
            Debug.Log("[EnemySpawner] 스폰 중단");
        }

        /// <summary>
        /// 적이 사망했을 때 생존 카운터를 감소시킵니다.
        /// BattleManager.OnEnemyDied()에서 호출합니다.
        /// </summary>
        public void NotifyEnemyDied()
        {
            _aliveEnemyCount = Mathf.Max(0, _aliveEnemyCount - 1);
        }

        // ─────────────────────────────────────────
        //  배틀 진입 연출 소환 (FieldManager 연동)
        // ─────────────────────────────────────────

        /// <summary>
        /// FieldManager.BattleSequenceRoutine()에서 yield return으로 호출됩니다.
        /// 이 메서드가 이번 배틀의 유일한 소환 수단입니다.
        /// 이후 추가 소환은 없습니다.
        ///
        /// [소환 규칙]
        ///   SpawnPoint를 인덱스 순서대로 1개씩 사용합니다.
        ///   소환 수가 _spawnPoints.Length를 초과하면 즉시 중단합니다.
        ///   각 적은 하늘 위(_enemyRotationX 적용된 채로)에서 낙하합니다.
        ///   모든 적의 착지가 완료된 후 코루틴을 반환합니다.
        ///
        /// [X Rotation 보정]
        ///   소환 직후 _enemyRotationX를 적용해 쿼터뷰 카메라 각도에 맞게 서 있는 것처럼 보입니다.
        /// </summary>
        public IEnumerator SpawnWithDropEffect(BattleEncounterDataSO encounterData)
        {
            if (encounterData == null || _spawnPoints == null || _spawnPoints.Length == 0)
                yield break;

            // 매니저 캐싱 (BattleField SetActive(true) 직후이므로 반드시 찾을 수 있음)
            _enemyComboManager = EnemyComboManager.Instance
                                 ?? FindFirstObjectByType<EnemyComboManager>();
            _enemyBattleUIManager = EnemyBattleUIManager.Instance
                                    ?? FindFirstObjectByType<EnemyBattleUIManager>();

            if (_enemyComboManager == null)
                Debug.LogWarning("[EnemySpawner] ★ EnemyComboManager를 찾을 수 없습니다.");
            if (_enemyBattleUIManager == null)
                Debug.LogWarning("[EnemySpawner] ★ EnemyBattleUIManager를 찾을 수 없습니다.");

            _enemyComboManager?.Initialize(encounterData.comboCount);

            _aliveEnemyCount = 0;
            _totalSpawnedCount = 0;

            const float dropHeight = 20f;
            const float dropDuration = 0.5f;
            const float stagger = 0.25f;

            float lastDropEnd = 0f;
            int spawnPointIdx = 0;
            int maxSpawn = _spawnPoints.Length;

            foreach (var entry in encounterData.spawnEntries)
            {
                if (entry.enemyPrefab == null) continue;

                for (int i = 0; i < entry.count; i++)
                {
                    // 최대 소환 수 = SpawnPoint 수 초과 시 중단
                    if (spawnPointIdx >= maxSpawn)
                    {
                        Debug.LogWarning($"[EnemySpawner] SpawnPoint({maxSpawn}개) 초과 — 소환 중단");
                        goto SpawnDone;
                    }

                    Transform spawnPoint = _spawnPoints[spawnPointIdx++];
                    Vector3 landPos = spawnPoint.position;
                    Vector3 dropStart = landPos + Vector3.up * dropHeight;

                    // 적 생성 — 하늘 위에 배치
                    GameObject newEnemy = Instantiate(
                        entry.enemyPrefab, dropStart, Quaternion.identity);

                    // X Rotation 보정 — 쿼터뷰 카메라 각도에 맞게 서 있는 것처럼 보이게 함
                    Vector3 euler = newEnemy.transform.eulerAngles;
                    newEnemy.transform.eulerAngles = new Vector3(
                        _enemyRotationX, euler.y, euler.z);

                    // 페이크 쿼터뷰 물리 전환 (중력 OFF + Kinematic)
                    Rigidbody2D rb = newEnemy.GetComponent<Rigidbody2D>();
                    if (rb != null)
                    {
                        rb.gravityScale = 0f;
                        rb.bodyType = RigidbodyType2D.Kinematic;
                        rb.linearVelocity = Vector2.zero;
                    }

                    // 낙하 연출 (stagger 간격으로 순서대로 낙하)
                    float delay = _totalSpawnedCount * stagger;

                    newEnemy.transform
                        .DOMove(landPos, dropDuration)
                        .SetEase(Ease.InQuad)
                        .SetDelay(delay)
                        .OnComplete(() =>
                            newEnemy.transform.DOPunchScale(
                                new Vector3(0.3f, -0.3f, 0f), 0.25f, 5, 0.5f));

                    lastDropEnd = delay + dropDuration + 0.25f;

                    // Enemy 초기화 + 매니저 등록
                    Enemy enemyScript = newEnemy.GetComponent<Enemy>();
                    if (enemyScript != null)
                    {
                        enemyScript.Init();

                        if (_enemyComboManager != null)
                            _enemyComboManager.RegisterEnemy(enemyScript);
                        else
                            _enemyBattleUIManager?.RegisterEnemy(enemyScript);
                    }

                    _aliveEnemyCount++;
                    _totalSpawnedCount++;
                }
            }

        SpawnDone:
            // 마지막 적 착지 + 충격 연출 완료까지 대기
            yield return new WaitForSeconds(lastDropEnd);

            Debug.Log($"[EnemySpawner] 낙하 소환 완료 — {_totalSpawnedCount}마리");
        }
    }
}