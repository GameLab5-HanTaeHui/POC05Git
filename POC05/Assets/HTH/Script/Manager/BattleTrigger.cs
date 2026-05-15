using UnityEngine;
using DG.Tweening;

namespace SENTRY
{
    /// <summary>
    /// 탐색 필드(2D 사이드뷰)에 배치되는 배틀 트리거 오브젝트.
    ///
    /// [진입 흐름]
    ///   플레이어 접촉
    ///     → SaveReturnPositions() 위치 저장
    ///     → FieldManager.EnterBattleSequence() 호출
    ///         (이후 모든 흐름은 FieldManager가 전담합니다)
    ///
    /// [선택창 위치 변경]
    ///   기존: 접촉 → 선택창 → 검은화면 → 배틀 진입
    ///   변경: 접촉 → 검은화면 → 배틀 필드 연출 → 선택창 (배틀 필드에서 표시)
    ///   선택창 표시는 FieldManager.EnterBattleSequence() 내부에서 처리합니다.
    ///
    /// [도망 선택 시]
    ///   배틀 필드에서 선택 → FieldManager.ReturnToField() → 탐색 필드 복귀
    ///   이 오브젝트는 _fleeCooldown 초 후 재활성화됩니다.
    ///
    /// [히어라키 위치]
    /// ExplorationField
    ///   └── EncounterEnemy_1
    ///         ├── BattleTrigger (이 스크립트)
    ///         ├── Collider2D    (IsTrigger = true)
    ///         └── SpriteRenderer
    /// </summary>
    public class BattleTrigger : MonoBehaviour
    {
        // ─────────────────────────────────────────
        //  Inspector 노출 필드
        // ─────────────────────────────────────────

        [Header("인카운터 데이터")]
        [Tooltip("이 BattleTrigger가 발동할 인카운터 구성 SO.")]
        [SerializeField] private BattleEncounterDataSO _encounterData;

        [Header("센트리 참조 (위치 저장용)")]
        [Tooltip("미연결 시 씬에서 자동 탐색합니다.")]
        [SerializeField] private Transform _strikeSentryTransform;
        [SerializeField] private Transform _shootSentryTransform;
        [SerializeField] private Transform _wallSentryTransform;

        [Header("배회 설정")]
        [SerializeField] private float _wanderSpeed = 1.5f;
        [SerializeField] private float _wanderRadius = 3f;

        [Header("도망 재활성화")]
        [Tooltip("도망 선택 시 이 오브젝트가 비활성화되는 시간 (초)")]
        [SerializeField] private float _fleeCooldown = 4f;

        // ─────────────────────────────────────────
        //  내부 상태
        // ─────────────────────────────────────────

        private bool _triggered = false;
        private Vector3 _startPosition;
        private SpriteRenderer _spriteRenderer;

        // ─────────────────────────────────────────
        //  유니티 생명주기
        // ─────────────────────────────────────────

        private void Start()
        {
            _startPosition = transform.position;
            _spriteRenderer = GetComponent<SpriteRenderer>();
            TryAutoFindSentries();

            if (_encounterData == null)
                Debug.LogWarning($"[BattleTrigger] {gameObject.name} — _encounterData 미연결");

            WanderLoop();
        }

        // ─────────────────────────────────────────
        //  센트리 자동 탐색
        // ─────────────────────────────────────────

        private void TryAutoFindSentries()
        {
            if (_strikeSentryTransform == null)
            {
                var s = FindFirstObjectByType<StrikeSentry>();
                if (s != null) _strikeSentryTransform = s.transform;
            }
            if (_shootSentryTransform == null)
            {
                var s = FindFirstObjectByType<ShootSentry>();
                if (s != null) _shootSentryTransform = s.transform;
            }
            if (_wallSentryTransform == null)
            {
                var s = FindFirstObjectByType<WallSentry>();
                if (s != null) _wallSentryTransform = s.transform;
            }
        }

        // ─────────────────────────────────────────
        //  배회 연출
        // ─────────────────────────────────────────

        private void WanderLoop()
        {
            if (_triggered) return;

            float targetX = _startPosition.x + Random.Range(-_wanderRadius, _wanderRadius);
            Vector3 target = new Vector3(targetX, transform.position.y, transform.position.z);
            float duration = Vector3.Distance(transform.position, target) / _wanderSpeed;

            if (_spriteRenderer != null)
                _spriteRenderer.flipX = target.x < transform.position.x;

            transform.DOMove(target, duration)
                .SetEase(Ease.InOutSine)
                .OnComplete(WanderLoop);
        }

        // ─────────────────────────────────────────
        //  충돌 감지
        // ─────────────────────────────────────────

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (_triggered) return;
            if (BattleManager.Instance != null && BattleManager.Instance.IsInBattle) return;
            if (!other.CompareTag("Player")) return;
            if (_encounterData == null) return;

            _triggered = true;
            transform.DOKill();

            // 위치 저장
            FieldManager.Instance?.SaveReturnPositions(
                other.transform,
                _strikeSentryTransform,
                _shootSentryTransform,
                _wallSentryTransform);

            // 배틀 시퀀스 시작 — 이후 모든 흐름은 FieldManager가 전담
            FieldManager.Instance?.EnterBattleSequence(
                other.transform,
                _encounterData,
                _strikeSentryTransform,
                _shootSentryTransform,
                _wallSentryTransform);

            gameObject.SetActive(false);
        }

        // ─────────────────────────────────────────
        //  도망 처리 콜백 (FieldManager에서 호출)
        // ─────────────────────────────────────────

        /// <summary>
        /// 배틀 필드에서 [도망] 선택 시 FieldManager를 통해 호출됩니다.
        /// _fleeCooldown 초 후 재활성화되어 배회를 재개합니다.
        /// </summary>
        public void OnFleeCompleted()
        {
            _triggered = false;
            gameObject.SetActive(false);
            Invoke(nameof(Reactivate), _fleeCooldown);
        }

        private void Reactivate()
        {
            gameObject.SetActive(true);
            _startPosition = transform.position;
            WanderLoop();
        }
    }
}