using UnityEngine;
using System.Collections.Generic;

namespace SENTRY
{ 
    /// <summary>
    /// 2콤보(타격+사격)에서 발사되는 고속 관통 탄환.
    ///
    /// [설계 의도]
    /// - 기존 PiercingBullet.cs 구조를 유지합니다.
    /// - Setup() 호출 시점의 방향으로만 직진하며 여러 적을 관통합니다.
    /// - damage 값은 ComboManager에서 외부 설정합니다.
    ///   (일반 SentryBullet과 달리 콤보 데미지 배율이 적용됩니다.)
    /// - 한 번 맞은 적은 HashSet으로 기록하여 중복 데미지를 방지합니다.
    ///
    /// [히어라키 위치]
    /// 동적 생성 오브젝트 (ComboManager.Combo_StrikeShoot()에서 Instantiate)
    ///   └── PiercingBullet (이 스크립트) + Collider2D(IsTrigger=true) + SpriteRenderer
    /// </summary>
    public class SentryPiercingBullet : MonoBehaviour
    {
        // ─────────────────────────────────────────
        //  Inspector 노출 필드
        // ─────────────────────────────────────────

        [Header("탄환 설정")]
        [Tooltip("직진 이동 속도 (너무 빠르면 터널링 발생 가능)")]
        [SerializeField] private float _speed = 40f;

        [Tooltip("최대 생존 시간 (초)")]
        [SerializeField] private float _lifeTime = 2f;

        // ─────────────────────────────────────────
        //  외부에서 설정하는 필드
        // ─────────────────────────────────────────

        /// <summary>
        /// 탄환 데미지. ComboManager.Combo_StrikeShoot()에서 발사 전에 설정합니다.
        /// </summary>
        [HideInInspector] public int damage = 40;

        // ─────────────────────────────────────────
        //  내부 상태 변수
        // ─────────────────────────────────────────

        /// <summary>발사 시점에 고정된 이동 방향. 이후 변경되지 않습니다.</summary>
        private Vector2 _moveDirection;

        /// <summary>발사 준비 완료 여부</summary>
        private bool _isFired = false;

        /// <summary>이미 맞힌 적 목록. 중복 데미지 방지용.</summary>
        private HashSet<GameObject> _hitEnemies = new HashSet<GameObject>();

        // ─────────────────────────────────────────
        //  초기화
        // ─────────────────────────────────────────

        /// <summary>
        /// 탄환을 초기화하고 발사합니다.
        /// 발사 순간의 타겟 방향으로만 직진합니다. (유도 없음)
        /// </summary>
        /// <param name="targetEnemy">방향 계산에 사용할 적 Transform</param>
        public void Setup(Transform targetEnemy)
        {
            if (targetEnemy == null) { Destroy(gameObject); return; }

            _moveDirection = (targetEnemy.position - transform.position).normalized;

            float angle = Mathf.Atan2(_moveDirection.y, _moveDirection.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0f, 0f, angle);

            _isFired = true;
            Destroy(gameObject, _lifeTime);
        }

        // ─────────────────────────────────────────
        //  유니티 생명주기
        // ─────────────────────────────────────────

        private void Update()
        {
            if (!_isFired) return;

            // 고정 방향으로 직진 (유도 없음)
            transform.position += (Vector3)_moveDirection * _speed * Time.deltaTime;
        }

        // ─────────────────────────────────────────
        //  충돌 처리 (관통)
        // ─────────────────────────────────────────

        /// <summary>
        /// 벽/바닥에 닿으면 파괴, 적에게 닿으면 데미지 후 관통(파괴 없음).
        /// </summary>
        private void OnTriggerEnter2D(Collider2D collision)
        {
            // 벽 또는 바닥 → 파괴
            if (collision.gameObject.layer == LayerMask.NameToLayer("Wall") ||
                collision.gameObject.layer == LayerMask.NameToLayer("Ground"))
            {
                Destroy(gameObject);
                return;
            }

            // 적 → 관통 데미지 (중복 방지)
            if (collision.gameObject.layer == LayerMask.NameToLayer("Enemy"))
            {
                if (!_hitEnemies.Contains(collision.gameObject))
                {
                    Enemy enemy = collision.GetComponent<Enemy>();
                    if (enemy != null)
                        enemy.TakeDamage(damage, HitType.Shoot, transform.position);

                    _hitEnemies.Add(collision.gameObject);
                    Debug.Log($"<color=cyan>[PiercingBullet] {collision.gameObject.name} 관통!</color>");
                }
                // Destroy 없음 → 계속 직진하며 다음 적 관통
            }
        }
    }
}