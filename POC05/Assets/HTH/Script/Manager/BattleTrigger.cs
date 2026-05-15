using UnityEngine;
using DG.Tweening;

namespace SENTRY
{
    /// <summary>
    /// 탐색 필드(2D 사이드뷰)에 배치되는 적 캐릭터 오브젝트.
    ///
    /// [변경 사항]
    /// - 플레이어 접촉 시 즉시 배틀로 진입하지 않고
    ///   전투/도망 선택창을 먼저 띄웁니다. (포켓몬스터 레퍼런스)
    /// - 선택창에 적 캐릭터 스탯(이름·레벨·HP)을 미리 보여줍니다.
    /// - 도망 선택 시 BattleTrigger가 잠시 비활성화 후 재활성됩니다.
    ///
    /// [흐름]
    ///   플레이어 접촉
    ///     → 느낌표 연출
    ///     → UIManager.ShowEncounterPanel(encounterData) 호출 (적 스탯 패널)
    ///     → 플레이어 선택 대기
    ///         ├── 전투 → TriggerBattle()
    ///         └── 도망 → FleeEncounter()
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
        [Tooltip("이 BattleTrigger가 발동할 인카운터 구성 SO.\n" +
                 "Project → Create → SENTRY → BattleEncounterData 로 생성하세요.")]
        [SerializeField] private BattleEncounterDataSO _encounterData;

        [Header("센트리 참조 (위치 저장용)")]
        [Tooltip("미연결 시 씬에서 자동 탐색합니다.")]
        [SerializeField] private Transform _strikeSentryTransform;
        [SerializeField] private Transform _shootSentryTransform;
        [SerializeField] private Transform _wallSentryTransform;

        [Header("배회 설정")]
        [Tooltip("탐색 필드 배회 이동 속도")]
        [SerializeField] private float _wanderSpeed = 1.5f;

        [Tooltip("시작 위치 기준 좌우 배회 반경")]
        [SerializeField] private float _wanderRadius = 3f;

        [Header("접촉 연출")]
        [Tooltip("플레이어 접촉 시 느낌표 연출 여부")]
        [SerializeField] private bool _playExclamationEffect = true;

        [Tooltip("느낌표 연출 지속 시간 (초)")]
        [SerializeField] private float _exclamationDuration = 0.5f;

        [Header("도망 설정")]
        [Tooltip("도망 선택 시 이 오브젝트가 비활성화되는 시간 (초).\n" +
                 "이후 재활성화됩니다.")]
        [SerializeField] private float _fleeCooldown = 4f;

        // ─────────────────────────────────────────
        //  내부 상태
        // ─────────────────────────────────────────

        /// <summary>이미 트리거되어 처리 중인지 여부</summary>
        private bool _triggered = false;

        /// <summary>선택창 대기 중인지 여부</summary>
        private bool _waitingForChoice = false;

        /// <summary>현재 접촉한 플레이어 Transform</summary>
        private Transform _pendingPlayer;

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
                Debug.LogWarning($"[BattleTrigger] {gameObject.name} — " +
                                 "_encounterData가 연결되지 않았습니다.");

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

            float targetX = _startPosition.x +
                             Random.Range(-_wanderRadius, _wanderRadius);
            Vector3 target = new Vector3(targetX,
                             transform.position.y, transform.position.z);
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
            if (_triggered || _waitingForChoice) return;
            if (BattleManager.Instance != null && BattleManager.Instance.IsInBattle) return;
            if (!other.CompareTag("Player")) return;
            if (_encounterData == null)
            {
                Debug.LogWarning($"[BattleTrigger] {gameObject.name} — EncounterData 없음");
                return;
            }

            _waitingForChoice = true;
            _pendingPlayer = other.transform;
            transform.DOKill();

            if (_playExclamationEffect)
            {
                transform.DOPunchScale(Vector3.one * 0.5f, _exclamationDuration, 5, 0.5f)
                    .OnComplete(ShowEncounterPanel);
            }
            else
            {
                ShowEncounterPanel();
            }
        }

        // ─────────────────────────────────────────
        //  선택창 표시
        // ─────────────────────────────────────────

        /// <summary>
        /// 적 스탯과 전투/도망 선택창을 엽니다.
        /// UIManager가 버튼 이벤트를 받아 OnPlayerChoseFight() / OnPlayerChoseFlee()를 호출합니다.
        /// </summary>
        private void ShowEncounterPanel()
        {
            BattleUIManager.Instance?.ShowEncounterPanel(_encounterData, this);
        }

        // ─────────────────────────────────────────
        //  선택 결과 콜백 (UIManager 버튼에서 호출)
        // ─────────────────────────────────────────

        /// <summary>
        /// [전투] 버튼 클릭 시 UIManager를 통해 호출됩니다.
        /// </summary>
        public void OnPlayerChoseFight()
        {
            if (!_waitingForChoice) return;
            _waitingForChoice = false;
            _triggered = true;

            BattleUIManager.Instance?.HideEncounterPanel();
            TriggerBattle(_pendingPlayer);
        }

        /// <summary>
        /// [도망] 버튼 클릭 시 UIManager를 통해 호출됩니다.
        /// _fleeCooldown 초 후 BattleTrigger가 재활성화됩니다.
        /// </summary>
        public void OnPlayerChoseFlee()
        {
            if (!_waitingForChoice) return;
            _waitingForChoice = false;

            BattleUIManager.Instance?.HideEncounterPanel();

            Debug.Log($"[BattleTrigger] {gameObject.name} — 도망 선택");

            // 잠시 비활성화 후 재배회 시작
            gameObject.SetActive(false);
            // DOTween은 비활성화 오브젝트에서 동작하지 않으므로
            // 외부 코루틴 없이 Invoke로 간단히 처리
            CancelInvoke(nameof(ReactivateTrigger));
            Invoke(nameof(ReactivateTrigger), _fleeCooldown);
        }

        private void ReactivateTrigger()
        {
            gameObject.SetActive(true);
            _startPosition = transform.position;
            WanderLoop();
        }

        // ─────────────────────────────────────────
        //  배틀 진입 처리
        // ─────────────────────────────────────────

        /// <summary>
        /// 전투 선택 후 실제 배틀 필드로 전환합니다.
        ///
        /// [변경] BattleManager.StartBattle()을 여기서 직접 호출하지 않습니다.
        ///        FieldManager.EnterBattle()에 encounterData를 함께 전달하면
        ///        FieldManager가 페이드 인 완료 후 자동으로 StartBattle()을 호출합니다.
        ///        이렇게 해야 화면이 밝아진 후 전투가 시작됩니다.
        /// </summary>
        private void TriggerBattle(Transform player)
        {
            Debug.Log($"[BattleTrigger] 배틀 전환! 인카운터: {_encounterData.encounterName}");

            if (FieldManager.Instance != null)
            {
                FieldManager.Instance.SaveReturnPositions(
                    player,
                    _strikeSentryTransform,
                    _shootSentryTransform,
                    _wallSentryTransform);

                // encounterData를 함께 전달 → FieldManager가 페이드 인 후 StartBattle() 호출
                FieldManager.Instance.EnterBattle(player, _encounterData);
            }

            gameObject.SetActive(false);
        }
    }
}