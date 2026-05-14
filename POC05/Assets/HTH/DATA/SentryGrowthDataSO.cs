using UnityEngine;

namespace SENTRY
{
    /// <summary>
    /// 센트리 레벨별 스탯 증가 데이터를 정의하는 ScriptableObject.
    ///
    /// [설계 의도]
    /// - 각 센트리 타입(타격/사격/벽)마다 하나씩 생성하여 Inspector에서
    ///   레벨별 스탯을 직관적으로 편집할 수 있습니다.
    /// - SentryBase.LevelUp()에서 이 데이터를 참조하여 스탯을 증가시킵니다.
    /// - ScriptableObject이므로 런타임에 값이 변경되지 않습니다.
    ///   (데이터는 불변, 실제 스탯은 센트리 인스턴스가 별도로 보관)
    ///
    /// [사용 방법]
    /// Project 창 우클릭 → Create → SENTRY → SentryGrowthData
    /// StrikeSentry_Growth, ShootSentry_Growth, WallSentry_Growth 3개 생성 권장
    /// </summary>
    [CreateAssetMenu(
        fileName = "SentryGrowthData",
        menuName = "SENTRY/SentryGrowthData",
        order = 0)]
    public class SentryGrowthDataSO : ScriptableObject
    {
        // ─────────────────────────────────────────
        //  레벨업 요구 경험치
        // ─────────────────────────────────────────

        [Header("경험치 설정")]
        [Tooltip("레벨 1 → 2 레벨업에 필요한 기준 경험치.\n" +
                 "실제 요구량 = baseExpToLevelUp × 현재레벨 (선형 증가)")]
        public int baseExpToLevelUp = 100;

        [Tooltip("최대 도달 가능 레벨")]
        public int maxLevel = 10;

        // ─────────────────────────────────────────
        //  레벨별 스탯 증가량 테이블
        // ─────────────────────────────────────────

        [Header("레벨별 스탯 증가 테이블")]
        [Tooltip("레벨업 1회당 HP 증가 비율 (1.1 = 10% 증가)")]
        public float hpMultiplierPerLevel = 1.1f;

        [Tooltip("레벨업 1회당 공격력 증가 비율")]
        public float damageMultiplierPerLevel = 1.1f;

        [Tooltip("레벨업 1회당 이동/추적 속도 증가량 (덧셈)")]
        public float speedBonusPerLevel = 0.1f;

        [Tooltip("레벨업 1회당 스킬 게이지 충전량 증가 (덧셈)")]
        public float skillGaugeBonusPerLevel = 2f;

        // ─────────────────────────────────────────
        //  공개 메서드
        // ─────────────────────────────────────────

        /// <summary>
        /// 현재 레벨에서 레벨업에 필요한 경험치를 반환합니다.
        /// </summary>
        /// <param name="currentLevel">현재 레벨 (1 이상)</param>
        public int GetRequiredExp(int currentLevel)
        {
            return baseExpToLevelUp * currentLevel;
        }
    }
}