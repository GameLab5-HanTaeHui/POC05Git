using UnityEngine;

namespace SENTRY
{
    /// <summary>
    /// 배틀 필드(페이크 쿼터뷰) 이동 안전 유틸리티.
    ///
    /// [프로젝트 구조 요약]
    ///   - BattleField TileMap    : Rotation (0, 0, 0)
    ///   - 센트리 / 적 오브젝트  : Rotation X -50 (페이크 쿼터뷰 보정)
    ///   - 이동 기준축            : X (좌우), Y (높낮이 — 쿼터뷰 원근), Z (화면 깊이)
    ///
    /// [Z값 오염 원인]
    ///   오브젝트가 X -50으로 기울어진 상태에서 DOMove, DOShake, DOPunch 등
    ///   DOTween이 3D Vector3 공간으로 이동 계산을 하면 Z 성분이 오염됩니다.
    ///   Z가 0에서 벗어나면 캐릭터가 공중으로 뜨거나 맵 뒤로 사라지는 현상이 발생합니다.
    ///
    /// [해결 방식]
    ///   1. ClampZ()      — 모든 이동 후 Z를 강제 0으로 고정하는 헬퍼
    ///   2. GetSafeTarget() — Y는 from 기준 고정, Z는 항상 0으로 고정,
    ///                        X축 방향으로만 CircleCast 벽 충돌 검사
    ///   3. FlatDirection() — 방향 계산 시 Y, Z 성분을 0으로 평탄화
    ///                        X축 방향만 사용해 승천/뚫림 방지
    ///   4. SafeShakeOffset / SafePunchOffset — DOShake/DOPunch에서 Z를 0으로 마스킹
    ///
    /// [사용 패턴]
    ///   // 이동 목표 계산
    ///   Vector3 dir = BattlePhysicsHelper.FlatDirection(from, to);
    ///   Vector3 raw = from + dir * distance;
    ///   Vector3 safe = BattlePhysicsHelper.GetSafeTarget(from, raw, radius, wallLayer);
    ///   transform.DOMove(safe, duration).OnUpdate(() => BattlePhysicsHelper.ClampZ(transform));
    ///
    ///   // LateUpdate Z 가드 (SentryBase / Enemy에서 매 프레임 호출)
    ///   BattlePhysicsHelper.ClampZ(transform);
    /// </summary>
    public static class BattlePhysicsHelper
    {
        // ─────────────────────────────────────────
        //  Z 고정 헬퍼
        // ─────────────────────────────────────────

        /// <summary>
        /// 오브젝트의 Z 위치를 강제로 0으로 고정합니다.
        ///
        /// [사용처]
        ///   SentryBase.LateUpdate() — 매 프레임 Z 오염 방지
        ///   Enemy.LateUpdate()      — 매 프레임 Z 오염 방지
        ///   DOMove.OnUpdate()       — 이동 중 실시간 Z 보정
        /// </summary>
        public static void ClampZ(Transform t)
        {
            if (t == null) return;
            Vector3 pos = t.position;
            if (pos.z == 0f) return;
            pos.z = 0f;
            t.position = pos;
        }

        // ─────────────────────────────────────────
        //  방향 벡터 평탄화 (Y, Z = 0)
        // ─────────────────────────────────────────

        /// <summary>
        /// 두 위치 사이의 방향을 X축만 사용해 정규화한 벡터를 반환합니다.
        ///
        /// [왜 필요한가]
        ///   캐릭터가 X -50으로 기울어진 상태에서 일반 (to - from).normalized를 쓰면
        ///   Y, Z 성분이 포함되어 밀치기/넉백 시 캐릭터가 승천하거나 뚫립니다.
        ///   이 메서드는 X 성분만 취해 순수 좌우 방향만 반환합니다.
        ///
        /// [사용처]
        ///   WallSentry.TryPush() / UseSkill() / FallbackSkill()
        ///   Enemy.TakeDamage() 넉백 방향
        ///   SentryComboManager 콤보 연출 방향
        /// </summary>
        public static Vector3 FlatDirection(Vector3 from, Vector3 to)
        {
            // X 성분만 사용 (Y, Z 는 0으로 평탄화)
            float dx = to.x - from.x;
            if (Mathf.Abs(dx) < 0.0001f) return Vector3.right;
            return new Vector3(Mathf.Sign(dx), 0f, 0f);
        }

        // ─────────────────────────────────────────
        //  안전한 이동 목표 계산
        // ─────────────────────────────────────────

        /// <summary>
        /// 이동 목표 위치를 검증하고 안전한 위치를 반환합니다.
        ///
        /// [처리 순서]
        ///   Step 1. Z를 항상 from.z(= 0)으로 고정
        ///   Step 2. Y를 from.y로 고정 (수직 이동 방지)
        ///   Step 3. X축 방향으로만 CircleCast 발사 → 벽 충돌 검사
        ///   Step 4. 충돌 없으면 Y/Z 고정된 to 반환
        ///           충돌 있으면 벽 직전 위치 반환 (Y/Z 고정 유지)
        ///
        /// [Y 고정 이유]
        ///   페이크 쿼터뷰에서 Y = 원근감(깊이)입니다.
        ///   이동 중 Y가 바뀌면 캐릭터가 다른 깊이로 이동해 시각적으로 부자연스럽습니다.
        ///   밀치기/넉백은 좌우(X)로만 발생해야 합니다.
        ///
        /// [Z 고정 이유]
        ///   Z는 화면 깊이입니다. 0에서 벗어나면 캐릭터가 공중으로 뜨거나 사라집니다.
        ///   모든 이동에서 Z = 0 강제 고정이 필수입니다.
        /// </summary>
        /// <param name="from">이동 시작 위치 (현재 위치)</param>
        /// <param name="to">이동 목표 위치 (원본)</param>
        /// <param name="radius">콜라이더 반경 (CircleCast 크기)</param>
        /// <param name="wallLayer">벽 레이어마스크 (TilemapCollider 포함)</param>
        /// <returns>Z=0, Y=from.y 고정 + 벽 충돌 보정된 안전한 목표 위치</returns>
        public static Vector3 GetSafeTarget(
            Vector3 from,
            Vector3 to,
            float radius,
            LayerMask wallLayer)
        {
            // Step 1 & 2: Z=0 고정, Y=from.y 고정
            Vector3 flatTo = new Vector3(to.x, from.y, 0f);

            // Step 3: X 방향으로만 CircleCast
            float dx = flatTo.x - from.x;
            float distance = Mathf.Abs(dx);
            Vector2 origin = new Vector2(from.x, from.y);
            Vector2 direction = new Vector2(Mathf.Sign(dx), 0f);

            if (distance <= 0.001f)
                return new Vector3(from.x, from.y, 0f);

            RaycastHit2D hit = Physics2D.CircleCast(
                origin: origin,
                radius: radius,
                direction: direction,
                distance: distance,
                layerMask: wallLayer);

            // Step 4: 충돌 없으면 Y/Z 고정된 목표 그대로 반환
            if (hit.collider == null)
                return flatTo;

            // 충돌 있으면 벽 직전 위치 (반경 + 여유 0.05f 유지)
            float safeX = from.x + direction.x * Mathf.Max(0f, hit.distance - radius - 0.05f);
            return new Vector3(safeX, from.y, 0f);
        }

        // ─────────────────────────────────────────
        //  DOTween 진동 오프셋 Z 마스킹
        // ─────────────────────────────────────────

        /// <summary>
        /// DOShakePosition / DOPunchPosition의 strenght 벡터에서 Z를 0으로 마스킹합니다.
        ///
        /// DOShakePosition(duration, strength)에서 strength가 Vector3이면
        /// Z축을 0으로 줘도 되지만, float 오버로드는 3축 모두 흔듭니다.
        /// 이 메서드는 Vector3 오버로드용 XY만 흔드는 strength 벡터를 반환합니다.
        ///
        /// [사용 예시]
        ///   // 이전 — Z 오염 발생
        ///   transform.DOShakePosition(0.2f, 0.15f, 10, 90f);
        ///
        ///   // 이후 — Z 차단
        ///   transform.DOShakePosition(0.2f, BattlePhysicsHelper.ShakeStrength(0.15f), 10, 90f);
        /// </summary>
        /// <param name="strength">XY 흔들림 강도</param>
        /// <returns>Z=0 마스킹된 Vector3 strength</returns>
        public static Vector3 ShakeStrength(float strength)
            => new Vector3(strength, strength, 0f);

        /// <summary>
        /// DOPunchPosition 벡터에서 Z를 0으로 마스킹합니다.
        /// 방향 벡터를 받아 Z를 제거하고 반환합니다.
        /// </summary>
        public static Vector3 PunchDir(Vector3 dir)
            => new Vector3(dir.x, 0f, 0f);
    }
}