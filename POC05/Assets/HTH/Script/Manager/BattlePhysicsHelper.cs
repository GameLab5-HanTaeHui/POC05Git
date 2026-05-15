using UnityEngine;

namespace SENTRY
{
    /// <summary>
    /// 배틀 필드에서 DOMove 기반 넉백/밀치기가 TilemapCollider를 뚫는 버그를 방지하는 유틸리티.
    ///
    /// [문제 원인]
    /// DOMove는 Transform을 직접 조작하므로 물리 엔진(Collider)을 완전히 무시합니다.
    /// Kinematic Rigidbody2D 상태에서도 TilemapCollider2D를 그냥 통과합니다.
    ///
    /// [해결 방식]
    /// DOMove 호출 전에 CircleCast로 이동 경로 상의 충돌을 검사합니다.
    /// 충돌이 감지되면 벽에 닿기 직전 위치로 목표를 클램핑합니다.
    /// DOMove는 보정된 안전한 위치로만 이동합니다.
    ///
    /// [사용법]
    /// Vector3 safe = BattlePhysicsHelper.GetSafeTarget(
    ///     from:      transform.position,
    ///     to:        knockTarget,
    ///     radius:    0.4f,
    ///     wallLayer: _wallLayer);
    ///
    /// transform.DOMove(safe, duration).SetEase(Ease.OutQuart);
    ///
    /// [Inspector 설정]
    /// wallLayer에 BattleField TilemapCollider 오브젝트가 속한 레이어를 설정하세요.
    /// 보통 "Ground" 또는 "Wall" 레이어입니다.
    /// </summary>
    public static class BattlePhysicsHelper
    {
        /// <summary>
        /// 넉백/밀치기 목표 위치를 CircleCast로 검증해 벽에 막히면 안전한 위치로 보정합니다.
        ///
        /// [동작 원리]
        ///   1. 현재 위치(from)에서 목표(to) 방향으로 CircleCast 발사
        ///   2. 이동 거리 내에 충돌이 없으면 원래 목표 반환
        ///   3. 충돌이 있으면 벽 직전 안전한 위치 반환
        /// </summary>
        /// <param name="from">이동 시작 위치 (현재 위치)</param>
        /// <param name="to">이동 목표 위치 (넉백/밀치기 목표)</param>
        /// <param name="radius">캐릭터 콜라이더 반경 (CircleCast 크기)</param>
        /// <param name="wallLayer">충돌 검사할 레이어 (TilemapCollider 포함)</param>
        /// <returns>벽에 막히지 않는 안전한 목표 위치</returns>
        public static Vector3 GetSafeTarget(
            Vector3 from,
            Vector3 to,
            float radius,
            LayerMask wallLayer)
        {
            Vector2 origin = from;
            Vector2 dir = ((Vector2)to - (Vector2)from).normalized;
            float distance = Vector2.Distance(from, to);

            if (distance <= 0f) return from;

            RaycastHit2D hit = Physics2D.CircleCast(
                origin: origin,
                radius: radius,
                direction: dir,
                distance: distance,
                layerMask: wallLayer);

            if (hit.collider == null)
                return to; // 충돌 없음 — 원래 목표 그대로

            // 충돌 지점 직전으로 클램핑 (반경 + 여유 0.05 유지)
            float safeDistance = Mathf.Max(0f, hit.distance - radius - 0.05f);
            return from + (Vector3)(dir * safeDistance);
        }
    }
}