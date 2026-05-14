using UnityEngine;
using System.Collections;
using DG.Tweening;

namespace SENTRY
{
    /// <summary>
    /// 탐색 필드 내 쉼터 구역 컴포넌트.
    ///
    /// [설계 의도]
    /// - 쉼터 구역은 탐색 필드(ExplorationField) 곳곳에 배치됩니다.
    /// - 플레이어가 이 구역에 진입하면:
    ///     1. 플레이어 HP를 일정량 서서히 회복합니다.
    ///     2. KO 상태인 센트리를 일정 HP로 부활시킵니다.
    /// - 부활은 플레이어가 구역 안에 머무는 동안만 가능합니다.
    ///   (구역을 벗어나면 회복이 중단됩니다.)
    /// - Collider2D의 IsTrigger = true 로 설정해야 합니다.
    ///
    /// [히어라키 위치]
    /// ExplorationField
    ///   └── ShelterZone (이 스크립트 + Collider2D(IsTrigger=true) + SpriteRenderer)
    /// </summary>
    public class ShelterZone : MonoBehaviour
    {
        // ─────────────────────────────────────────
        //  Inspector 노출 필드
        // ─────────────────────────────────────────

        [Header("쉼터 설정")]
        [Tooltip("플레이어 HP 초당 회복량")]
        [SerializeField] private int _playerHealPerSecond = 5;

        [Tooltip("KO 센트리 부활 시 회복할 HP 양. 0이면 전량 회복합니다.")]
        [SerializeField] private int _sentryReviveHealAmount = 0;

        [Tooltip("센트리 부활 처리 간격 (초). 한 번에 1기씩 부활합니다.")]
        [SerializeField] private float _reviveInterval = 2f;

        [Header("센트리 참조")]
        [Tooltip("타격 센트리")]
        [SerializeField] private StrikeSentry _strikeSentry;

        [Tooltip("사격 센트리")]
        [SerializeField] private ShootSentry _shootSentry;

        [Tooltip("벽 센트리")]
        [SerializeField] private WallSentry _wallSentry;

        [Header("쉼터 연출")]
        [Tooltip("쉼터 영역 SpriteRenderer. 플레이어 진입 시 밝아지는 연출에 사용합니다.")]
        [SerializeField] private SpriteRenderer _zoneSprite;

        [Tooltip("쉼터 평상시 색상")]
        [SerializeField] private Color _idleColor = new Color(0.3f, 1f, 0.3f, 0.2f);

        [Tooltip("플레이어 진입 시 쉼터 활성화 색상")]
        [SerializeField] private Color _activeColor = new Color(0.3f, 1f, 0.3f, 0.5f);

        // ─────────────────────────────────────────
        //  내부 상태 변수
        // ─────────────────────────────────────────

        /// <summary>플레이어가 현재 쉼터 안에 있는지 여부</summary>
        private bool _playerInside = false;

        /// <summary>플레이어 체력 컴포넌트 캐시</summary>
        private PlayerHealth _playerHealth;

        /// <summary>회복 코루틴 참조 (중복 실행 방지)</summary>
        private Coroutine _healCoroutine;

        /// <summary>부활 코루틴 참조</summary>
        private Coroutine _reviveCoroutine;

        // ─────────────────────────────────────────
        //  유니티 생명주기
        // ─────────────────────────────────────────

        private void Start()
        {
            // 쉼터 초기 색상 설정
            if (_zoneSprite != null)
                _zoneSprite.color = _idleColor;
        }

        // ─────────────────────────────────────────
        //  충돌 감지
        // ─────────────────────────────────────────

        /// <summary>플레이어가 쉼터에 진입하면 회복을 시작합니다.</summary>
        private void OnTriggerEnter2D(Collider2D other)
        {
            if (!other.CompareTag("Player")) return;

            _playerInside = true;
            _playerHealth = other.GetComponentInChildren<PlayerHealth>();

            // 쉼터 활성화 연출
            if (_zoneSprite != null)
                _zoneSprite.DOColor(_activeColor, 0.4f);

            // 회복 코루틴 시작
            if (_healCoroutine != null) StopCoroutine(_healCoroutine);
            _healCoroutine = StartCoroutine(HealRoutine());

            // 센트리 부활 코루틴 시작
            if (_reviveCoroutine != null) StopCoroutine(_reviveCoroutine);
            _reviveCoroutine = StartCoroutine(ReviveRoutine());

            Debug.Log("[ShelterZone] 플레이어 진입 — 회복 시작");
        }

        /// <summary>플레이어가 쉼터를 벗어나면 회복을 중단합니다.</summary>
        private void OnTriggerExit2D(Collider2D other)
        {
            if (!other.CompareTag("Player")) return;

            _playerInside = false;

            // 쉼터 비활성화 연출
            if (_zoneSprite != null)
                _zoneSprite.DOColor(_idleColor, 0.4f);

            // 회복 코루틴 중단
            if (_healCoroutine != null)
            {
                StopCoroutine(_healCoroutine);
                _healCoroutine = null;
            }

            if (_reviveCoroutine != null)
            {
                StopCoroutine(_reviveCoroutine);
                _reviveCoroutine = null;
            }

            Debug.Log("[ShelterZone] 플레이어 이탈 — 회복 중단");
        }

        // ─────────────────────────────────────────
        //  플레이어 HP 회복 코루틴
        // ─────────────────────────────────────────

        /// <summary>
        /// 플레이어가 쉼터 안에 있는 동안 매 초 HP를 회복합니다.
        /// </summary>
        private IEnumerator HealRoutine()
        {
            while (_playerInside)
            {
                if (_playerHealth != null && !_playerHealth.IsDead)
                    _playerHealth.Heal(_playerHealPerSecond);

                yield return new WaitForSeconds(1f);
            }
        }

        // ─────────────────────────────────────────
        //  센트리 부활 코루틴
        // ─────────────────────────────────────────

        /// <summary>
        /// KO 상태인 센트리를 _reviveInterval 마다 1기씩 순서대로 부활시킵니다.
        /// 모든 센트리가 살아있으면 대기합니다.
        /// </summary>
        private IEnumerator ReviveRoutine()
        {
            while (_playerInside)
            {
                yield return new WaitForSeconds(_reviveInterval);

                if (!_playerInside) yield break;

                // KO 상태인 센트리를 우선순위 순서대로 부활
                // 우선순위: 타격 → 사격 → 벽 (변경 가능)
                if (_strikeSentry != null && _strikeSentry.IsKnockedOut)
                {
                    ReviveSentry(_strikeSentry);
                }
                else if (_shootSentry != null && _shootSentry.IsKnockedOut)
                {
                    ReviveSentry(_shootSentry);
                }
                else if (_wallSentry != null && _wallSentry.IsKnockedOut)
                {
                    ReviveSentry(_wallSentry);
                }
            }
        }

        /// <summary>
        /// 개별 센트리를 부활시키고 부활 연출을 재생합니다.
        /// </summary>
        private void ReviveSentry(SentryBase sentry)
        {
            sentry.Revive(_sentryReviveHealAmount);

            // 쉼터 강조 연출: 부활 시 더 밝게 펄스
            if (_zoneSprite != null)
                _zoneSprite.DOColor(Color.white, 0.15f)
                    .SetLoops(2, LoopType.Yoyo)
                    .OnComplete(() => _zoneSprite.color = _activeColor);

            Debug.Log($"<color=lime>[ShelterZone] {sentry.SentryName} 부활!</color>");
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            // 쉼터 범위를 초록색으로 시각화
            Gizmos.color = new Color(0f, 1f, 0f, 0.3f);
            Collider2D col = GetComponent<Collider2D>();
            if (col != null)
                Gizmos.DrawCube(col.bounds.center, col.bounds.size);
        }
#endif
    }
}