using UnityEngine;
using System.Collections.Generic;

namespace SENTRY
{
    /// <summary>
    /// 배틀 인카운터(전투 구성)를 정의하는 ScriptableObject.
    ///
    /// [변경 사항]
    /// - comboCount 필드 추가
    ///   EnemyComboGroup이 이 값을 읽어 적 콤보 순번 AI를 초기화합니다.
    ///     1 = 단독 공격 (기존 방식)
    ///     2 = 2명이 번갈아 공격
    ///     3 = 3명이 순서대로 공격
    ///
    /// [사용 방법]
    /// Project 창 우클릭 → Create → SENTRY → BattleEncounterData
    ///
    /// [히어라키 흐름]
    /// BattleTrigger._encounterData (이 SO 참조)
    ///   → BattleManager.StartBattle(encounterData)
    ///   → EnemySpawner.SpawnStart(encounterData)
    ///   → EnemyComboGroup.Initialize(comboCount)
    ///   → 적 순차 소환 + 콤보 순번 AI 활성
    /// </summary>
    [CreateAssetMenu(
        fileName = "BattleEncounterData",
        menuName = "SENTRY/BattleEncounterData",
        order = 1)]
    public class BattleEncounterDataSO : ScriptableObject
    {
        // ─────────────────────────────────────────
        //  인카운터 기본 정보
        // ─────────────────────────────────────────

        [Header("인카운터 기본 정보")]
        [Tooltip("인카운터 이름. 디버그 및 에디터 식별용.\n" +
                 "예) Forest_Easy / Cave_Normal / Boss_Stage")]
        public string encounterName = "NewEncounter";

        [Tooltip("배틀 클리어에 필요한 처치 수.\n" +
                 "0이면 spawnEntries의 총 적 수를 자동으로 사용합니다.")]
        public int killCountToWin = 0;

        // ─────────────────────────────────────────
        //  적 콤보 순번 설정
        // ─────────────────────────────────────────

        [Header("적 콤보 순번 설정")]
        [Tooltip("적 캐릭터 콤보 순번 수.\n" +
                 "1 = 단독 공격 (모두 자유 공격)\n" +
                 "2 = 2명이 번갈아가며 공격\n" +
                 "3 = 3명이 순서대로 공격\n\n" +
                 "EnemyComboGroup이 이 값을 읽어 AI를 초기화합니다.")]
        [Range(1, 3)]
        public int comboCount = 1;

        // ─────────────────────────────────────────
        //  적 소환 목록
        // ─────────────────────────────────────────

        [Header("적 소환 목록")]
        [Tooltip("소환할 적들의 정보 목록.\n" +
                 "순서대로 소환되며, spawnDelay로 타이밍을 조절합니다.")]
        public List<EnemySpawnEntry> spawnEntries = new List<EnemySpawnEntry>();

        // ─────────────────────────────────────────
        //  공개 메서드
        // ─────────────────────────────────────────

        /// <summary>
        /// 이 인카운터의 총 적 수를 반환합니다.
        /// killCountToWin이 0이면 이 값이 클리어 조건이 됩니다.
        /// </summary>
        public int GetTotalEnemyCount()
        {
            int total = 0;
            foreach (var entry in spawnEntries)
                total += entry.count;
            return total;
        }

        /// <summary>
        /// 클리어 목표 처치 수를 반환합니다.
        /// killCountToWin이 0이면 총 적 수를 반환합니다.
        /// </summary>
        public int GetKillCountToWin()
        {
            return killCountToWin > 0 ? killCountToWin : GetTotalEnemyCount();
        }
    }

    // ─────────────────────────────────────────────
    //  적 소환 엔트리
    // ─────────────────────────────────────────────

    /// <summary>
    /// 소환할 적 1종의 정보를 담는 구조체.
    /// BattleEncounterDataSO.spawnEntries 배열의 원소입니다.
    /// </summary>
    [System.Serializable]
    public class EnemySpawnEntry
    {
        [Tooltip("소환할 적 프리팹. Enemy.cs가 붙어 있어야 합니다.")]
        public GameObject enemyPrefab;

        [Tooltip("이 프리팹을 몇 마리 소환할지 지정합니다.")]
        [Range(1, 20)]
        public int count = 1;

        [Tooltip("이전 엔트리 소환 완료 후 이 적을 소환하기까지 대기 시간 (초).\n" +
                 "0이면 이전 엔트리와 동시에 소환합니다.")]
        [Range(0f, 10f)]
        public float spawnDelay = 0f;

        [Tooltip("이 엔트리 내 각 적 사이의 소환 간격 (초).\n" +
                 "예) count=3, interval=0.5 → 0.5초 간격으로 3마리 소환.")]
        [Range(0f, 5f)]
        public float spawnInterval = 0.5f;
    }
}