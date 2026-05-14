using UnityEngine;

namespace SENTRY
{
    /// <summary>
    /// 플레이어 이동 및 점프를 담당하는 컨트롤러.
    /// 
    /// [설계 의도]
    /// - 대쉬 기능은 새로운 컨셉에서 제거되었습니다.
    /// - 이동은 방향키(Horizontal 축)로만 동작합니다.
    /// - 2단 점프는 유지합니다. (매트로배니아 이동감 유지)
    /// - 플레이어의 공격 기능은 이 클래스에 없습니다. (센트리가 대신 전투)
    /// 
    /// [히어라키 위치]
    /// Player (GameObject)
    ///   ├── PlayerController (이 스크립트)
    ///   ├── PlayerHealth
    ///   ├── Rigidbody2D
    ///   └── Collider2D
    /// </summary>
    public class PlayerController : MonoBehaviour
    {
        // ─────────────────────────────────────────
        //  Inspector 노출 필드
        // ─────────────────────────────────────────

        [Header("컴포넌트 참조")]
        [Tooltip("물리 이동에 사용할 Rigidbody2D. 비워두면 자동으로 가져옵니다.")]
        [SerializeField] private Rigidbody2D _rigid2D;

        [Header("이동 설정")]
        [Tooltip("좌우 이동 속도 (단위: 유니티 유닛/초)")]
        [SerializeField] private float _movePower = 5f;

        [Header("점프 설정")]
        [Tooltip("점프 시 가하는 순간 힘의 크기")]
        [SerializeField] private float _jumpPower = 300f;

        [Tooltip("허용하는 최대 점프 횟수. 2로 설정하면 2단 점프.")]
        [SerializeField] private int _maxJumps = 2;

        [Tooltip("바닥 감지에 사용할 레이어. Inspector에서 Ground 레이어를 할당하세요.")]
        [SerializeField] private LayerMask _groundLayer;

        [Tooltip("바닥 감지 레이캐스트의 길이. 캐릭터 키에 맞춰 조정하세요.")]
        [SerializeField] private float _groundCheckDistance = 1.1f;

        // ─────────────────────────────────────────
        //  내부 상태 변수
        // ─────────────────────────────────────────

        /// <summary>현재 수평 입력값 (-1 ~ 1)</summary>
        private float _moveInput;

        /// <summary>현재까지 점프한 횟수. 착지 시 0으로 리셋됩니다.</summary>
        private int _currentJumps = 0;

        /// <summary>현재 땅에 닿아 있는지 여부</summary>
        private bool _isGrounded;

        /// <summary>현재 바라보는 방향 (1f = 오른쪽, -1f = 왼쪽)</summary>
        private float _facingDirection = 1f;

        // ─────────────────────────────────────────
        //  유니티 생명주기
        // ─────────────────────────────────────────

        private void Start()
        {
            // Rigidbody2D가 Inspector에서 비어 있으면 자동으로 가져옵니다.
            if (_rigid2D == null)
                _rigid2D = GetComponent<Rigidbody2D>();
        }

        private void Update()
        {
            // 매 프레임: 바닥 감지 → 입력 처리 → 점프 처리
            CheckGrounded();
            HandleMoveInput();
            HandleJumpInput();
        }

        // ─────────────────────────────────────────
        //  바닥 감지
        // ─────────────────────────────────────────

        /// <summary>
        /// 캐릭터 발 아래로 레이캐스트를 쏴서 바닥 여부를 판정합니다.
        /// 바닥에 닿으면 점프 횟수를 초기화합니다.
        /// </summary>
        private void CheckGrounded()
        {
            // 아래 방향으로 레이를 쏴 groundLayer와 충돌하는지 확인
            _isGrounded = Physics2D.Raycast(
                transform.position,
                Vector2.down,
                _groundCheckDistance,
                _groundLayer
            );

            // 착지 시 점프 카운트 초기화
            if (_isGrounded)
                _currentJumps = 0;
        }

        // ─────────────────────────────────────────
        //  이동 처리
        // ─────────────────────────────────────────

        /// <summary>
        /// 수평 입력(방향키 / A·D)을 읽어 이동 방향을 결정하고 Rigidbody에 속도를 적용합니다.
        /// 키를 떼면 X축 속도를 즉시 0으로 만들어 미끄러짐을 방지합니다.
        /// </summary>
        private void HandleMoveInput()
        {
            _moveInput = Input.GetAxis("Horizontal");

            if (_moveInput != 0f)
            {
                // 이동 방향 기억 (스프라이트 플립 등에 활용 가능)
                _facingDirection = Mathf.Sign(_moveInput);
                _rigid2D.linearVelocityX = _moveInput * _movePower;
            }
            else
            {
                // 입력이 없을 때 즉시 정지 (물리 마찰 없이 딱딱한 이동감 구현)
                _rigid2D.linearVelocityX = 0f;
            }
        }

        // ─────────────────────────────────────────
        //  점프 처리
        // ─────────────────────────────────────────

        /// <summary>
        /// 스페이스바 입력 시 점프를 시도합니다.
        /// _maxJumps 횟수 이하일 때만 점프가 허용됩니다. (기본값 2 = 2단 점프)
        /// </summary>
        private void HandleJumpInput()
        {
            if (!Input.GetKeyDown(KeyCode.Space)) return;

            // 최대 점프 횟수 초과 시 무시
            if (_currentJumps >= _maxJumps) return;

            _currentJumps++;

            // [중요] 공중에서 2단 점프 시 기존 낙하 속도를 먼저 제거해야
            //         올바른 높이로 점프됩니다.
            _rigid2D.linearVelocityY = 0f;

            _rigid2D.AddForce(Vector2.up * _jumpPower);
        }
#if UNITY_EDITOR
        // ─────────────────────────────────────────
        //  기즈모 (에디터 전용)
        // ─────────────────────────────────────────

        /// <summary>
        /// 씬 뷰에서 바닥 감지 레이의 길이를 녹색 선으로 시각화합니다.
        /// 빌드에는 포함되지 않습니다.
        /// </summary>
        private void OnDrawGizmos()
        {
            Gizmos.color = Color.green;
            Gizmos.DrawLine(
                transform.position,
                transform.position + Vector3.down * _groundCheckDistance
            );
        }
#endif
        // ─────────────────────────────────────────
        //  외부 접근용 프로퍼티
        // ─────────────────────────────────────────

        /// <summary>현재 플레이어가 바라보는 방향 (1 = 오른쪽, -1 = 왼쪽)</summary>
        public float FacingDirection => _facingDirection;

        /// <summary>현재 플레이어가 땅에 닿아 있는지 여부</summary>
        public bool IsGrounded => _isGrounded;
    }
}