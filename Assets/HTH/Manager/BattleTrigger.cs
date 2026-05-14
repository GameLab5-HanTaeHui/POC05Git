using UnityEngine;
using DG.Tweening;

namespace SENTRY
{
    /// <summary>
    /// 탐색 필드(2D 사이드뷰)에 배치되는 적 캐릭터 오브젝트.
    /// 플레이어와 접촉 시 배틀 필드(2.5D 쿼터뷰)로 전환합니다.
    ///
    /// [변경 사항 - Revision3]
    /// - _encounterData (BattleEncounterDataSO)를 보유합니다.
    /// - 플레이어와 접촉 시 이 SO를 BattleManager에 전달합니다.
    /// - BattleManager → EnemySpawner로 전달되어 해당 인카운터의 적이 소환됩니다.
    /// - 각 BattleTrigger마다 다른 SO를 연결하면
    ///   구역별로 다른 적 구성을 가질 수 있습니다.
    ///
    /// [인카운터 흐름]
    /// BattleTrigger._encounterData (SO)
    ///   → BattleManager.StartBattle(player, encounterData)
    ///   → EnemySpawner.SpawnStart(encounterData, player)
    ///   → SO의 spawnEntries 순서대로 적 소환
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
                 "Project → Create → SENTRY → BattleEncounterData 로 생성하세요.\n" +
                 "각 BattleTrigger마다 다른 SO를 연결하면 구역별로 다른 적이 등장합니다.")]
        [SerializeField] private BattleEncounterDataSO _encounterData;

        [Header("센트리 참조 (위치 저장용)")]
        [Tooltip("위치 저장에 사용할 타격 센트리 Transform.\n" +
                 "미연결 시 자동 탐색합니다.")]
        [SerializeField] private Transform _strikeSentryTransform;

        [Tooltip("위치 저장에 사용할 사격 센트리 Transform")]
        [SerializeField] private Transform _shootSentryTransform;

        [Tooltip("위치 저장에 사용할 벽 센트리 Transform")]
        [SerializeField] private Transform _wallSentryTransform;

        [Header("배회 설정")]
        [Tooltip("탐색 필드(2D 사이드뷰) 배회 이동 속도")]
        [SerializeField] private float _wanderSpeed = 1.5f;

        [Tooltip("시작 위치 기준 좌우 배회 반경")]
        [SerializeField] private float _wanderRadius = 3f;

        [Header("접촉 연출")]
        [Tooltip("플레이어 접촉 시 느낌표 연출 여부")]
        [SerializeField] private bool _playExclamationEffect = true;

        [Tooltip("느낌표 연출 지속 시간 (초)")]
        [SerializeField] private float _exclamationDuration = 0.5f;

        // ─────────────────────────────────────────
        //  내부 상태 변수
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

            // Inspector 미연결 시 자동 탐색
            TryAutoFindSentries();

            // 인카운터 데이터 미연결 경고
            if (_encounterData == null)
                Debug.LogWarning($"[BattleTrigger] {gameObject.name} — " +
                                 "_encounterData가 연결되지 않았습니다.");

            WanderLoop();
        }

        // ─────────────────────────────────────────
        //  센트리 자동 탐색
        // ─────────────────────────────────────────

        /// <summary>
        /// Inspector에서 센트리가 연결되지 않은 경우 씬에서 자동으로 탐색합니다.
        /// </summary>
        private void TryAutoFindSentries()
        {
            if (_strikeSentryTransform == null)
            {
                StrikeSentry s = FindFirstObjectByType<StrikeSentry>();
                if (s != null) _strikeSentryTransform = s.transform;
            }
            if (_shootSentryTransform == null)
            {
                ShootSentry s = FindFirstObjectByType<ShootSentry>();
                if (s != null) _shootSentryTransform = s.transform;
            }
            if (_wallSentryTransform == null)
            {
                WallSentry s = FindFirstObjectByType<WallSentry>();
                if (s != null) _wallSentryTransform = s.transform;
            }
        }

        // ─────────────────────────────────────────
        //  배회 연출 (2D 사이드뷰 — X축 좌우만)
        // ─────────────────────────────────────────

        /// <summary>
        /// 탐색 필드에서 X축 좌우로 배회합니다.
        /// 포켓몬스터의 야생 포켓몬 배회처럼 자연스러운 이동감을 만듭니다.
        /// </summary>
        private void WanderLoop()
        {
            if (_triggered) return;

            float targetX = _startPosition.x + Random.Range(-_wanderRadius, _wanderRadius);
            Vector3 target = new Vector3(targetX, transform.position.y, transform.position.z);
            float duration = Vector3.Distance(transform.position, target) / _wanderSpeed;

            // 이동 방향에 따라 스프라이트 좌우 반전
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
            if (_encounterData == null)
            {
                Debug.LogWarning($"[BattleTrigger] {gameObject.name} — " +
                                 "EncounterData가 없어 배틀을 시작할 수 없습니다.");
                return;
            }

            _triggered = true;
            transform.DOKill();

            if (_playExclamationEffect)
            {
                transform.DOPunchScale(Vector3.one * 0.5f, _exclamationDuration, 5, 0.5f)
                    .OnComplete(() => TriggerBattle(other.transform));
            }
            else
            {
                TriggerBattle(other.transform);
            }
        }

        /// <summary>
        /// 배틀 전환 처리.
        /// ① 위치 저장 → ② 필드 전환 → ③ 배틀 시작(인카운터 데이터 전달) 순서로 호출합니다.
        /// </summary>
        private void TriggerBattle(Transform player)
        {
            Debug.Log($"[BattleTrigger] 배틀 시작! 인카운터: {_encounterData.encounterName}");

            // ① 접촉 시점 위치 저장 (탐색 필드 복귀용)
            if (FieldManager.Instance != null)
                FieldManager.Instance.SaveReturnPositions(
                    player,
                    _strikeSentryTransform,
                    _shootSentryTransform,
                    _wallSentryTransform);

            // ② 필드 전환 (페이드 + Cinemachine 전환)
            if (FieldManager.Instance != null)
                FieldManager.Instance.EnterBattle(player);

            // ③ 배틀 시작 — 인카운터 데이터를 BattleManager에 전달
            if (BattleManager.Instance != null)
                BattleManager.Instance.StartBattle(player, _encounterData);

            gameObject.SetActive(false);
        }
    }
}