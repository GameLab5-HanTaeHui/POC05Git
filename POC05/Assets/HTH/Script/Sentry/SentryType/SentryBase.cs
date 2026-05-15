using UnityEngine;
using DG.Tweening;
using System.Collections;

namespace SENTRY
{
    /// <summary>
    /// 모든 센트리(소환수)의 공통 기능을 담당하는 베이스 클래스.
    ///
    /// [담당 영역 — Base vs 자식 클래스 분리 원칙]
    ///
    /// ▶ SentryBase (이 클래스)
    ///   - 센트리 기본 정보: 이름, HP, 레벨, EXP, 성장 데이터
    ///   - 탐색 필드 이동: 플레이어 포메이션 추종 (Rigidbody2D)
    ///   - 배틀 필드 물리: Kinematic 전환 / gravityScale 제어
    ///   - 상태 관리: KO / 무적 / 과부화 / 레이어 전환
    ///   - 경험치 / 레벨업
    ///
    /// ▶ StrikeSentry / ShootSentry / WallSentry (자식 클래스)
    ///   - 전투 전용 정보: 공격력, 공격 범위, 쿨타임, 이동 속도
    ///   - 스킬 게이지 / 고유 스킬 연출
    ///   - 적 탐색(FindTarget) / 전투 AI(HandleBattleAI)
    ///
    /// [레이어 상태 관리]
    ///   SentryLive  : 살아있음 — 적이 타겟팅 가능
    ///   SentryDead  : KO 상태  — 타겟팅 불가
    ///   SentryCombo : 콤보 중  — 무적 + 타겟팅 불가
    ///   → 레이어 변경은 이 클래스에서만 수행합니다.
    ///
    /// [레이어 설정 — Unity에서 반드시 추가하세요]
    ///   Edit → Project Settings → Tags and Layers
    ///   SentryLive / SentryDead / SentryCombo 3개 레이어 추가
    ///   센트리 프리팹 초기 레이어: SentryLive
    /// </summary>
    public class SentryBase : MonoBehaviour
    {
        // ─────────────────────────────────────────
        //  Inspector 노출 필드
        //  [Base 담당] 기본 정보 / HP / 레벨 / 탐색 이동 / 연출
        //  [자식 담당] 전투 스탯 (공격력, 범위, 쿨타임 등)
        // ─────────────────────────────────────────

        [Header("센트리 기본 정보")]
        [Tooltip("인스펙터 확인용 이름. SentryName 프로퍼티로 외부에서 참조합니다.")]
        [SerializeField] private string _sentryName = "Sentry";

        [Header("체력")]
        [Tooltip("초기 최대 HP. 레벨업 시 LevelUp()에서 증가합니다.")]
        [SerializeField] private int _maxHp = 150;

        [Header("성장 데이터")]
        [Tooltip("레벨업 스탯 증가 ScriptableObject.\n" +
                 "Project → Create → SENTRY → SentryGrowthDataSO")]
        [SerializeField] private SentryGrowthDataSO _growthData;

        [Header("경험치 / 레벨 (GrowthData 미연결 시)")]
        [Tooltip("GrowthData 없을 때 기본 레벨업 요구 경험치")]
        [SerializeField] private int _baseExpToLevelUp = 100;

        [Tooltip("GrowthData 없을 때 최대 레벨")]
        [SerializeField] private int _maxLevel = 10;

        [Header("탐색 필드 추종 설정 (2D 사이드뷰)")]
        [Tooltip("플레이어 추종 이동 속도 (탐색 필드 전용)")]
        [SerializeField] private float _followSpeed = 4f;

        [Tooltip("포메이션 정지 반경. 이 거리 안이면 이동 멈춤.")]
        [SerializeField] private float _followStopDistance = 1.5f;

        [Tooltip("플레이어 기준 포메이션 오프셋.\n" +
                 "예) Strike(-1.5, 0) / Shoot(-3, 0) / Wall(-2.2, 0.5)")]
        [SerializeField] private Vector2 _formationOffset = new Vector2(-1f, 0f);

        [Header("연출 참조")]
        [Tooltip("피격 / 무적 / 과부화 / 회복 연출용 SpriteRenderer")]
        [SerializeField] private SpriteRenderer _spriteRenderer;

        [Header("페이크 쿼터뷰 (배틀 필드)")]
        [Tooltip("Y 위치 기반 SortingOrder 갱신 배율. 기본값 10.")]
        [SerializeField] private float _sortingOrderScale = 10f;

        // ─────────────────────────────────────────
        //  레이어 이름 상수
        // ─────────────────────────────────────────

        private const string LAYER_LIVE = "SentryLive";
        private const string LAYER_DEAD = "SentryDead";
        private const string LAYER_COMBO = "SentryCombo";

        // ─────────────────────────────────────────
        //  내부 상태 변수
        // ─────────────────────────────────────────

        private int _currentHp;
        private int _currentExp = 0;
        private int _currentLevel = 1;
        private Transform _playerTransform;
        private bool _isFollowing = false;
        private bool _isInvincible = false;
        private Rigidbody2D _rb;
        private float _savedGravityScale = 1f;
        private bool _isBattlePhysics = false;

        /// <summary>
        /// 배틀 필드 활성 여부.
        /// true이면 SortingOrder를 Y 위치 기반으로 매 프레임 갱신합니다.
        /// EnterBattlePhysics()보다 먼저 SetupForBattle()에서 true로 세팅되어
        /// DOMove 등장 연출 중에도 정렬이 적용됩니다.
        /// </summary>
        private bool _isInBattleField = false;

        // ─────────────────────────────────────────
        //  외부 공개 프로퍼티
        // ─────────────────────────────────────────

        public int CurrentHp => _currentHp;
        public int MaxHp => _maxHp;
        public int CurrentLevel => _currentLevel;
        public int CurrentExp => _currentExp;
        public string SentryName => _sentryName;

        public int RequiredExp => _growthData != null
            ? _growthData.GetRequiredExp(_currentLevel)
            : _baseExpToLevelUp * _currentLevel;

        public int MaxLevel => _growthData != null ? _growthData.maxLevel : _maxLevel;

        public bool IsKnockedOut { get; private set; } = false;
        public bool IsOverloaded { get; private set; } = false;
        public float OverloadDamageMultiplier { get; private set; } = 1f;
        public float OverloadSpeedMultiplier { get; private set; } = 1f;

        public Vector3 FormationWorldPosition => _playerTransform != null
            ? _playerTransform.position + (Vector3)(Vector2)_formationOffset
            : transform.position;

        // ─────────────────────────────────────────
        //  유니티 생명주기
        // ─────────────────────────────────────────

        private void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            if (_rb != null) _savedGravityScale = _rb.gravityScale;
            _currentHp = _maxHp;
        }

        private void Update()
        {
            if (!IsKnockedOut && _playerTransform != null && _isFollowing)
                FollowPlayer();

            // 배틀 필드에 있는 동안 Y 위치 기반 SortingOrder 갱신
            // _isInBattleField은 SetupForBattle()에서 true가 되므로
            // EnterBattlePhysics() 이전 DOMove 등장 연출 중에도 정렬이 적용됩니다.
            if (_isInBattleField && _spriteRenderer != null)
                _spriteRenderer.sortingOrder =
                    Mathf.RoundToInt(-transform.position.y * _sortingOrderScale);
        }

        // ─────────────────────────────────────────
        //  초기화
        // ─────────────────────────────────────────

        /// <summary>최초 소환 시 1회 호출. HP/EXP/레벨 초기화.</summary>
        public virtual void Init(Transform player)
        {
            _playerTransform = player;
            _currentHp = _maxHp;
            _currentExp = 0;
            _currentLevel = 1;
            IsKnockedOut = false;
            IsOverloaded = false;
            _isInvincible = false;
            _isBattlePhysics = false;
            _rb = GetComponent<Rigidbody2D>();
            if (_rb != null) _savedGravityScale = _rb.gravityScale;
            _isFollowing = true;

            SetLayer(LAYER_LIVE);
            Debug.Log($"<color=cyan>[{_sentryName}]</color> 초기화 완료 (Lv.{_currentLevel})");
        }

        /// <summary>배틀 진입마다 호출. 레벨·EXP·HP는 유지, 전투 플래그만 초기화.</summary>
        public void SetupForBattle(Transform player)
        {
            if (player != null) _playerTransform = player;
            IsOverloaded = false;
            _isInvincible = false;
            _isBattlePhysics = false;
            _isInBattleField = true;   // Y 보정 시작 — DOMove 등장 연출 중에도 적용
            if (_rb == null)
            {
                _rb = GetComponent<Rigidbody2D>();
                if (_rb != null) _savedGravityScale = _rb.gravityScale;
            }
            SetLayer(LAYER_LIVE);
            Debug.Log($"[{_sentryName}] 배틀 준비 — Lv.{_currentLevel} " +
                      $"HP:{_currentHp}/{_maxHp} EXP:{_currentExp}");
        }

        // ─────────────────────────────────────────
        //  레이어 제어 (상태 관리 핵심)
        // ─────────────────────────────────────────

        /// <summary>
        /// 게임오브젝트 레이어를 변경합니다.
        /// 레이어 이름은 LAYER_LIVE / LAYER_DEAD / LAYER_COMBO 상수를 사용합니다.
        /// </summary>
        private void SetLayer(string layerName)
        {
            int layer = LayerMask.NameToLayer(layerName);
            if (layer == -1)
            {
                Debug.LogWarning($"[{_sentryName}] ★ 레이어 '{layerName}'이 존재하지 않습니다.\n" +
                                 "Edit → Project Settings → Tags and Layers에서 추가하세요.");
                return;
            }
            gameObject.layer = layer;
        }

        // ─────────────────────────────────────────
        //  탐색 필드 추종 이동 (2D 사이드뷰)
        // ─────────────────────────────────────────

        private void FollowPlayer()
        {
            Vector2 targetPos = (Vector2)_playerTransform.position + _formationOffset;
            float dist = Vector2.Distance(transform.position, targetPos);
            if (dist <= _followStopDistance)
            {
                if (_rb != null) _rb.linearVelocity = Vector2.zero;
                return;
            }
            Vector2 dir = (targetPos - (Vector2)transform.position).normalized;
            if (_rb != null) _rb.linearVelocity = dir * _followSpeed;
        }

        public void StopFollowing()
        {
            _isFollowing = false;
            if (_rb != null) _rb.linearVelocity = Vector2.zero;
        }

        public void StartFollowing() => _isFollowing = true;

        // ─────────────────────────────────────────
        //  페이크 쿼터뷰 물리 전환
        // ─────────────────────────────────────────

        /// <summary>배틀 진입 시 호출. gravityScale=0, Kinematic 전환.</summary>
        public void EnterBattlePhysics()
        {
            if (_rb == null) return;
            _savedGravityScale = _rb.gravityScale;
            _rb.gravityScale = 0f;
            _rb.bodyType = RigidbodyType2D.Kinematic;
            _rb.linearVelocity = Vector2.zero;
            _isBattlePhysics = true;
        }

        /// <summary>탐색 복귀 시 호출. Dynamic + 원래 gravityScale 복원.</summary>
        public void ExitBattlePhysics()
        {
            if (_rb == null) return;
            _rb.bodyType = RigidbodyType2D.Dynamic;
            _rb.gravityScale = _savedGravityScale;
            _isBattlePhysics = false;
            _isInBattleField = false;
            if (_spriteRenderer != null) _spriteRenderer.sortingOrder = 0;
        }

        /// <summary>배틀 필드 이동. Kinematic 상태에서 MovePosition 사용.</summary>
        public void BattleMove(Vector2 direction, float speed)
        {
            if (_rb == null || !_isBattlePhysics) return;
            _rb.MovePosition(_rb.position + direction * speed * Time.fixedDeltaTime);
        }

        /// <summary>배틀 필드 이동 정지.</summary>
        public void BattleStop()
        {
            if (_rb == null) return;
            _rb.linearVelocity = Vector2.zero;
        }

        // ─────────────────────────────────────────
        //  체력 처리
        // ─────────────────────────────────────────

        /// <summary>데미지 수신. 무적/KO 상태면 무시.</summary>
        public virtual void TakeDamage(int damage)
        {
            if (_isInvincible || IsKnockedOut) return;

            _currentHp = Mathf.Max(_currentHp - damage, 0);

            transform.DOShakePosition(0.2f, 0.15f, 10, 90f);
            if (_spriteRenderer != null)
                _spriteRenderer.DOColor(Color.red, 0.05f)
                    .SetLoops(4, LoopType.Yoyo)
                    .OnComplete(() =>
                    {
                        if (_spriteRenderer != null)
                            _spriteRenderer.color = IsOverloaded ? Color.red : Color.white;
                    });

            if (_currentHp <= 0) KnockOut();
        }

        /// <summary>HP 회복. KO 상태에선 불가.</summary>
        public void Heal(int amount)
        {
            if (IsKnockedOut) return;
            _currentHp = Mathf.Min(_currentHp + amount, _maxHp);
            if (_spriteRenderer != null)
                _spriteRenderer.DOColor(Color.green, 0.1f)
                    .SetLoops(4, LoopType.Yoyo)
                    .OnComplete(() =>
                    {
                        if (_spriteRenderer != null)
                            _spriteRenderer.color = IsOverloaded ? Color.red : Color.white;
                    });
        }

        /// <summary>쉼터에서 KO 센트리 부활.</summary>
        public void Revive(int healAmount = 0)
        {
            if (!IsKnockedOut) return;
            IsKnockedOut = false;
            _currentHp = healAmount <= 0 ? _maxHp : Mathf.Min(healAmount, _maxHp);
            _isFollowing = true;

            SetLayer(LAYER_LIVE);

            transform.localScale = Vector3.zero;
            transform.DOScale(Vector3.one, 0.4f).SetEase(Ease.OutBack);
            if (_spriteRenderer != null) _spriteRenderer.color = Color.white;
        }

        // ─────────────────────────────────────────
        //  KO 처리
        // ─────────────────────────────────────────

        /// <summary>
        /// HP 0 → KO 처리.
        /// 레이어를 SentryDead로 변경하여 적의 타겟팅 탐색에서 즉시 제외됩니다.
        /// </summary>
        private void KnockOut()
        {
            IsKnockedOut = true;
            IsOverloaded = false;
            _isFollowing = false;
            if (_rb != null) _rb.linearVelocity = Vector2.zero;

            // 레이어 변경 — 적의 Physics2D.OverlapCircleAll에서 제외됨
            SetLayer(LAYER_DEAD);

            if (_spriteRenderer != null)
                _spriteRenderer.DOColor(Color.gray, 0.3f);
            transform.DOScale(new Vector3(1f, 0.3f, 1f), 0.3f).SetEase(Ease.OutBounce);

            Debug.Log($"<color=red>[{_sentryName} KO]</color>");
            BattleManager.Instance?.OnSentryKnockedOut();
        }

        public void ForceKnockOut()
        {
            if (!IsKnockedOut) KnockOut();
        }

        // ─────────────────────────────────────────
        //  무적 (콤보 연출 중)
        // ─────────────────────────────────────────

        /// <summary>
        /// 무적 상태 설정.
        /// true: SentryCombo 레이어 → 적 타겟팅 불가 + 피격 무효
        /// false: SentryLive 레이어 → 복귀
        /// </summary>
        public void SetInvincible(bool on)
        {
            _isInvincible = on;

            // 레이어 변경으로 타겟팅 상태 결정
            SetLayer(on ? LAYER_COMBO : LAYER_LIVE);

            if (_spriteRenderer == null) return;
            if (on)
            {
                _spriteRenderer.DOKill();
                _spriteRenderer.DOFade(0.5f, 0.15f).SetLoops(-1, LoopType.Yoyo);
            }
            else
            {
                _spriteRenderer.DOKill();
                _spriteRenderer.color = IsOverloaded ? Color.red : Color.white;
            }
        }

        // ─────────────────────────────────────────
        //  과부화
        // ─────────────────────────────────────────

        public void SetOverload(bool on, float damageMult, float speedMult)
        {
            IsOverloaded = on;
            OverloadDamageMultiplier = on ? damageMult : 1f;
            OverloadSpeedMultiplier = on ? speedMult : 1f;
            if (_spriteRenderer != null)
                _spriteRenderer.color = on ? Color.red : Color.white;
        }

        // ─────────────────────────────────────────
        //  경험치 / 레벨업
        // ─────────────────────────────────────────

        public void AddExp(int amount)
        {
            if (IsKnockedOut) return;
            _currentExp += amount;
            Debug.Log($"[{_sentryName}] EXP +{amount} ({_currentExp}/{RequiredExp})");
            while (_currentExp >= RequiredExp && _currentLevel < MaxLevel)
            {
                _currentExp -= RequiredExp;
                LevelUp();
            }
        }

        protected virtual void LevelUp()
        {
            _currentLevel++;
            _maxHp = Mathf.RoundToInt(_maxHp * 1.1f);
            _currentHp = _maxHp;

            transform.DOPunchScale(Vector3.one * 0.4f, 0.4f, 5, 0.5f);
            if (_spriteRenderer != null)
                _spriteRenderer.DOColor(Color.yellow, 0.1f)
                    .SetLoops(6, LoopType.Yoyo)
                    .OnComplete(() =>
                    {
                        if (_spriteRenderer != null)
                            _spriteRenderer.color = Color.white;
                    });

            BattleUIManager.Instance?.PlayLevelUpEffect(_sentryName, _currentLevel);
            Debug.Log($"<color=yellow>[{_sentryName} 레벨업!]</color> Lv.{_currentLevel}");
        }
    }
}