using UnityEngine;

namespace SENTRY
{
    /// <summary>
    /// 사격 센트리(ShootSentry)가 발사하는 호밍 탄환.
    ///
    /// [설계 의도]
    /// - Setup(target) 호출 시 지정한 적을 향해 MoveTowards로 추적합니다.
    /// - 기존 Bullet.cs 구조를 유지합니다.
    /// - damage 값은 ShootSentry.FireBullet()에서 외부 설정합니다.
    ///   (스킬 발동 시 일반 공격보다 높은 damage를 넘겨줄 수 있습니다.)
    /// - 적이 사라지면 탄환도 자동 파괴됩니다.
    ///
    /// [히어라키 위치]
    /// 동적 생성 오브젝트 (ShootSentry.FireBullet()에서 Instantiate)
    ///   └── Bullet (이 스크립트) + Collider2D(IsTrigger=true) + SpriteRenderer
    /// </summary>
    public class SentryBullet : MonoBehaviour
    {
        // ─────────────────────────────────────────
        //  Inspector 노출 필드
        // ─────────────────────────────────────────

        [Header("탄환 기본 설정")]
        [Tooltip("탄환이 적을 추적하는 이동 속도")]
        [SerializeField] private float _speed = 12f;

        [Tooltip("탄환 최대 생존 시간 (초). 이 시간이 지나면 자동 파괴됩니다.")]
        [SerializeField] private float _lifeTime = 2f;

        // ─────────────────────────────────────────
        //  외부에서 설정하는 필드
        // ─────────────────────────────────────────

        /// <summary>
        /// 탄환 데미지. ShootSentry.FireBullet()에서 발사 전에 설정합니다.
        /// 일반 공격과 스킬 공격의 데미지를 구분하기 위해 외부 설정 방식을 유지합니다.
        /// </summary>
        [HideInInspector] public int damage = 20;

        // ─────────────────────────────────────────
        //  내부 상태 변수
        // ─────────────────────────────────────────

        /// <summary>추적 대상 적 Transform. Setup()으로 설정됩니다.</summary>
        private Transform _target;

        /// <summary>Setup()이 호출되어 탄환이 발사 준비된 상태인지 여부</summary>
        private bool _isFired = false;

        // ─────────────────────────────────────────
        //  초기화
        // ─────────────────────────────────────────

        /// <summary>
        /// 탄환을 초기화하고 발사합니다. ShootSentry.FireBullet()에서 호출합니다.
        /// </summary>
        /// <param name="targetEnemy">추적할 적 Transform</param>
        public void Setup(Transform targetEnemy)
        {
            _target = targetEnemy;
            _isFired = true;

            // 수명이 다하면 자동 파괴 (적이 사라지지 않는 극단적 상황 대비)
            Destroy(gameObject, _lifeTime);
        }

        // ─────────────────────────────────────────
        //  유니티 생명주기
        // ─────────────────────────────────────────

        private void Update()
        {
            if (!_isFired) return;

            // 타겟이 사라졌으면 탄환도 파괴
            if (_target == null || !_target.gameObject.activeInHierarchy)
            {
                Destroy(gameObject);
                return;
            }

            // 적 위치를 향해 매 프레임 이동 (호밍)
            transform.position = Vector3.MoveTowards(
                transform.position,
                _target.position,
                _speed * Time.deltaTime
            );

            // 탄환 스프라이트를 이동 방향으로 회전
            Vector2 dir = (_target.position - transform.position).normalized;
            if (dir != Vector2.zero)
            {
                float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
                transform.rotation = Quaternion.Euler(0f, 0f, angle);
            }

            // 목표에 충분히 가까워지면 히트 처리
            if (Vector2.Distance(transform.position, _target.position) < 0.2f)
                HitTarget();
        }

        // ─────────────────────────────────────────
        //  히트 처리
        // ─────────────────────────────────────────

        /// <summary>
        /// 적에게 데미지를 적용하고 탄환을 파괴합니다.
        /// MoveTowards 방식과 OnTriggerEnter2D 방식 모두에서 호출됩니다.
        /// </summary>
        private void HitTarget()
        {
            if (_target != null)
            {
                Enemy enemy = _target.GetComponent<Enemy>();
                if (enemy != null)
                    enemy.TakeDamage(damage, HitType.Shoot, transform.position);
            }

            Destroy(gameObject);
        }

        /// <summary>
        /// 트리거 충돌 보조 처리. MoveTowards가 프레임을 건너뛸 때를 대비합니다.
        /// </summary>
        private void OnTriggerEnter2D(Collider2D collision)
        {
            if (collision.gameObject.layer == LayerMask.NameToLayer("Enemy"))
                HitTarget();
        }
    }
}