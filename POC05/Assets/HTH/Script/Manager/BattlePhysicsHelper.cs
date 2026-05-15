using UnityEngine;

namespace SENTRY
{
    /// <summary>
    /// 배틀 필드(페이크 쿼터뷰)에서 DOMove 기반 이동의 두 가지 버그를 방지합니다.
    ///
    /// [버그 1 — TilemapCollider 뚫림]
    ///   DOMove는 Transform을 직접 조작하므로 Kinematic Rigidbody2D의 물리 충돌을 무시합니다.
    ///   → CircleCast로 이동 경로를 검사해 벽 직전 위치로 클램핑합니다.
    ///
    /// [버그 2 — Y축 승천 / 맵 뒤 뚫림]
    ///   페이크 쿼터뷰에서 Y축 = 깊이(원근감)입니다.
    ///   밀치기/넉백 방향 벡터에 Y 성분이 포함되면 오브젝트가 하늘로 승천하거나
    ///   맵 뒤쪽으로 뚫려 들어갑니다.
    ///   → 모든 이동 계산을 X축 방향만 사용하고, Y값은 출발지 기준으로 고정합니다.
    ///
    /// [GetSafeTarget 동작]
    ///   1. to의 Y값을 from의 Y값으로 고정 (Y축 이동 방지)
    ///   2. X축 방향으로만 CircleCast 발사
    ///   3. 충돌 없으면 Y 고정된 to 반환
    ///   4. 충돌 있으면 X축 벽 직전 위치 + from의 Y 반환
    ///
    /// [FlatDirection 동작]
    ///   두 Vector3 사이의 방향을 Y=0으로 평탄화한 normalized 벡터를 반환합니다.
    ///   밀치기/넉백 방향 계산 시 반드시 이 메서드를 사용하세요.
    /// </summary>
    public static class BattlePhysicsHelper
    {
        /// <summary>
        /// Y축을 고정한 채로 목표 위치를 CircleCast로 검증해 안전한 위치를 반환합니다.
        ///
        /// [핵심]
        ///   페이크 쿼터뷰에서 Y축은 깊이(원근감)입니다.
        ///   이동 중 Y가 바뀌면 오브젝트가 하늘/땅으로 이동합니다.
        ///   이 메서드는 to의 Y를 from의 Y로 강제 고정한 뒤 X 방향으로만 충돌을 검사합니다.
        /// </summary>
        /// <param name="from">이동 시작 위치 (오브젝트 현재 위치)</param>
        /// <param name="to">이동 목표 위치 (넉백/밀치기 원본 목표)</param>
        /// <param name="radius">오브젝트 콜라이더 반경 (CircleCast 크기)</param>
        /// <param name="wallLayer">벽 레이어마스크 (TilemapCollider 포함)</param>
        /// <returns>Y축 고정 + 벽 충돌 보정된 안전한 목표 위치</returns>
        public static Vector3 GetSafeTarget(
            Vector3 from,
            Vector3 to,
            float radius,
            LayerMask wallLayer)
        {
            // ── Step 1: Y축 고정 ──
            // to의 Y를 from의 Y로 강제 고정합니다.
            // 페이크 쿼터뷰에서 Y는 깊이이므로 밀치기/넉백에서 Y가 바뀌면 안 됩니다.
            Vector3 flatTo = new Vector3(to.x, from.y, to.z);

            // ── Step 2: X축 방향으로만 CircleCast ──
            Vector2 origin = new Vector2(from.x, from.y);
            Vector2 direction = new Vector2(flatTo.x - from.x, flatTo.y - from.y);
            float distance = direction.magnitude;

            if (distance <= 0.001f) return flatTo;

            RaycastHit2D hit = Physics2D.CircleCast(
                origin: origin,
                radius: radius,
                direction: direction.normalized,
                distance: distance,
                layerMask: wallLayer);

            // ── Step 3: 충돌 없으면 Y 고정된 목표 그대로 반환 ──
            if (hit.collider == null)
                return flatTo;

            // ── Step 4: 충돌 있으면 벽 직전 위치 + from의 Y 반환 ──
            float safeDistance = Mathf.Max(0f, hit.distance - radius - 0.05f);
            Vector2 safeXY = origin + direction.normalized * safeDistance;

            return new Vector3(safeXY.x, from.y, from.z);
        }

        /// <summary>
        /// 두 위치 사이의 방향 벡터를 Y=0으로 평탄화한 뒤 정규화해 반환합니다.
        ///
        /// 밀치기/넉백 방향 계산 시 반드시 이 메서드를 사용하세요.
        /// 일반 (to - from).normalized는 Y 성분이 포함되어 승천/뚫림이 발생합니다.
        ///
        /// 사용 예시:
        ///   Vector3 pushDir = BattlePhysicsHelper.FlatDirection(from, to);
        ///   Vector3 rawTarget = from + pushDir * pushDistance;
        ///   Vector3 safeTarget = BattlePhysicsHelper.GetSafeTarget(from, rawTarget, radius, wallLayer);
        /// </summary>
        public static Vector3 FlatDirection(Vector3 from, Vector3 to)
        {
            Vector3 dir = new Vector3(to.x - from.x, 0f, to.z - from.z);
            return dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.right;
        }
    }
}