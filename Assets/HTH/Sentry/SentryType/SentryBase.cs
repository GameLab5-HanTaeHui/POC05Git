using UnityEngine;
using DG.Tweening;
using System.Collections;

namespace SENTRY
{
    /// <summary>
    /// 모든 센트리(소환수)의 공통 기능을 담당하는 베이스 클래스.
    ///
    /// [필드별 동작 구분]
    ///
    /// ▶ 탐색 필드 (2D 사이드뷰)
    ///   - 플레이어 뒤를 포메이션 오프셋으로 따라다닙니다.
    ///   - _formationOffset은 2D 사이드뷰 기준 X/Y 오프셋입니다.
    ///   - Rigidbody2D linearVelocity 방식으로 이동합니다.
    ///
    /// ▶ 배틀 필드 (2.5D 쿼터뷰)
    ///   - FieldManager.SentryBattleSpawnPoints[i] 위치로 배치됩니다.
    ///   - 배틀 중에는 StopFollowing() 상태로 전투 AI가 이동을 제어합니다.
    ///   - 쿼터뷰 좌표계(X: 좌우, Y: 상하 원근감)를 사용합니다.
    ///   - FormationWorldPosition은 배틀 종료 후 복귀 목표로만 사용합니다.
    ///     (탐색 필드 복귀 후 플레이어 포메이션 위치)
    ///
    /// [히어라키 위치]
    /// Sentries
    ///   ├── StrikeSentry (SentryBase 상속)
    ///   ├── ShootSentry  (SentryBase 상속)
    ///   └── WallSentry   (SentryBase 상속)
    /// </summary>
    public class SentryBase : MonoBehaviour
    {
        // ─────────────────────────────────────────
        //  Inspector 노출 필드
        // ─────────────────────────────────────────

        [Header("센트리 기본 정보")]
        [Tooltip("인스펙터 확인용 이름 (코드 동작에 영향 없음)")]
        [SerializeField] private string _sentryName = "Sentry";

        [Header("체력 설정")]
        [Tooltip("센트리의 초기 최대 HP")]
        [SerializeField] private int _maxHp = 150;

        [Header("성장 데이터")]
        [Tooltip("레벨업 스탯 증가를 정의하는 ScriptableObject.\n" +
                 "Project 창 → Create → SENTRY → SentryGrowthDataSO 로 생성하세요.\n" +
                 "연결하지 않으면 인스펙터 기본값으로 동작합니다.")]
        [SerializeField] private SentryGrowthDataSO _growthData;

        [Header("경험치 / 레벨 (GrowthData 미연결 시 사용)")]
        [Tooltip("GrowthData가 없을 때 사용할 기본 레벨업 요구 경험치")]
        [SerializeField] private int _baseExpToLevelUp = 100;

        [Tooltip("GrowthData가 없을 때 사용할 최대 레벨")]
        [SerializeField] private int _maxLevel = 10;

        [Header("탐색 필드 추종 설정 (2D 사이드뷰)")]
        [Tooltip("플레이어를 따라다닐 이동 속도 (탐색 필드 전용)")]
        [SerializeField] private float _followSpeed = 4f;

        [Tooltip("포메이션 정지 반경.\n" +
                 "이 거리 안에 들어오면 이동을 멈춥니다.")]
        [SerializeField] private float _followStopDistance = 1.5f;

        [Tooltip("탐색 필드(2D 사이드뷰) 기준 플레이어 포메이션 오프셋.\n" +
                 "X: 좌우 간격 / Y: 높이 차이\n" +
                 "예) Strike(-1.5, 0) / Shoot(-3, 0) / Wall(-2.2, 0.5)")]
        [SerializeField] private Vector2 _formationOffset = new Vector2(-1f, 0f);

        [Header("연출 참조")]
        [Tooltip("피격 / 무적 / 과부화 / 회복 연출용 SpriteRenderer")]
        [SerializeField] private SpriteRenderer _spriteRenderer;

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

        // ─────────────────────────────────────────
        //  외부 공개 프로퍼티
        // ─────────────────────────────────────────

        public int CurrentHp => _currentHp;
        public int MaxHp => _maxHp;
        public int CurrentLevel => _currentLevel;
        public int CurrentExp => _currentExp;

        /// <summary>
        /// 현재 레벨 기준 레벨업 요구 경험치 (UI 표시용).
        /// GrowthData 연결 여부에 따라 자동 분기됩니다.
        /// </summary>
        public int RequiredExp => _growthData != null
            ? _growthData.GetRequiredExp(_currentLevel)
            : _baseExpToLevelUp * _currentLevel;

        /// <summary>최대 레벨 (UI 표시용)</summary>
        public int MaxLevel => _growthData != null ? _growthData.maxLevel : _maxLevel;

        /// <summary>기절(KO) 여부. true면 전투 AI가 멈추고 쉼터 부활 대기.</summary>
        public bool IsKnockedOut { get; private set; } = false;

        /// <summary>과부화 상태 여부 (자식 클래스 스탯 배율 적용용)</summary>
        public bool IsOverloaded { get; private set; } = false;

        /// <summary>과부화 공격력 배율</summary>
        public float OverloadDamageMultiplier { get; private set; } = 1f;

        /// <summary>과부화 이동 속도 배율</summary>
        public float OverloadSpeedMultiplier { get; private set; } = 1f;

        /// <summary>센트리 이름</summary>
        public string SentryName => _sentryName;

        /// <summary>
        /// 탐색 필드(2D 사이드뷰) 기준 포메이션 월드 좌표.
        /// 배틀 종료 후 탐색 필드로 복귀할 때의 목표 위치입니다.
        /// 배틀 필드 내 배치 위치는 FieldManager.SentryBattleSpawnPoints를 사용합니다.
        /// </summary>
        public Vector3 FormationWorldPosition
        {
            get
            {
                if (_playerTransform == null) return transform.position;
                return _playerTransform.position + (Vector3)(Vector2)_formationOffset;
            }
        }

        // ─────────────────────────────────────────
        //  초기화
        // ─────────────────────────────────────────

        /// <summary>
        /// 센트리를 초기화합니다. 소환 직후 반드시 호출하세요.
        /// </summary>
        public virtual void Init(Transform player)
        {
            _playerTransform = player;
            _currentHp = _maxHp;
            _currentExp = 0;
            _currentLevel = 1;
            IsKnockedOut = false;
            IsOverloaded = false;
            _isInvincible = false;
            _rb = GetComponent<Rigidbody2D>();
            _isFollowing = true;

            Debug.Log($"<color=cyan>[{_sentryName}]</color> 초기화 완료 (Lv.{_currentLevel})");
        }

        // ─────────────────────────────────────────
        //  유니티 생명주기
        // ─────────────────────────────────────────

        private void Update()
        {
            // 탐색 필드에서만 추종 이동
            // 배틀 필드에서는 StopFollowing() 상태이므로 이 블록이 실행되지 않음
            if (IsKnockedOut || _playerTransform == null || !_isFollowing) return;
            FollowPlayer();
        }

        // ─────────────────────────────────────────
        //  탐색 필드 추종 이동 (2D 사이드뷰)
        // ─────────────────────────────────────────

        /// <summary>
        /// 탐색 필드(2D 사이드뷰)에서 플레이어의 포메이션 위치로 이동합니다.
        /// 배틀 필드에서는 StopFollowing()으로 비활성화됩니다.
        /// </summary>
        private void FollowPlayer()
        {
            // 2D 사이드뷰 기준 포메이션 목표 위치
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

        /// <summary>
        /// 추종을 멈춥니다.
        /// 배틀 필드 진입 시 BattleManager / ComboManager가 호출합니다.
        /// </summary>
        public void StopFollowing()
        {
            _isFollowing = false;
            if (_rb != null) _rb.linearVelocity = Vector2.zero;
        }

        /// <summary>
        /// 추종을 재개합니다.
        /// 배틀 종료 후 탐색 필드 복귀 시 BattleManager / ComboManager가 호출합니다.
        /// </summary>
        public void StartFollowing() => _isFollowing = true;

        // ─────────────────────────────────────────
        //  체력 처리
        // ─────────────────────────────────────────

        /// <summary>
        /// 데미지를 입힙니다. 무적 또는 기절 상태면 무시합니다.
        /// 배틀 필드(쿼터뷰)에서 Enemy.TakeDamage 연계로 호출됩니다.
        /// </summary>
        public virtual void TakeDamage(int damage)
        {
            if (_isInvincible || IsKnockedOut) return;

            _currentHp -= damage;
            _currentHp = Mathf.Max(_currentHp, 0);

            Debug.Log($"<color=orange>[{_sentryName} 피격]</color> " +
                      $"-{damage} / 남은 HP: {_currentHp}");

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

        /// <summary>
        /// HP를 회복합니다.
        /// 탐색 필드 ShelterZone 또는 PlayerAbility 긴급 수리에서 호출합니다.
        /// KO 상태에서는 회복 불가 (ShelterZone의 Revive()로만 부활).
        /// </summary>
        public void Heal(int amount)
        {
            if (IsKnockedOut) return;

            _currentHp = Mathf.Min(_currentHp + amount, _maxHp);
            Debug.Log($"<color=lime>[{_sentryName} 회복]</color> " +
                      $"+{amount} HP / 현재: {_currentHp}");

            if (_spriteRenderer != null)
                _spriteRenderer.DOColor(Color.green, 0.1f)
                    .SetLoops(4, LoopType.Yoyo)
                    .OnComplete(() =>
                    {
                        if (_spriteRenderer != null)
                            _spriteRenderer.color = IsOverloaded ? Color.red : Color.white;
                    });
        }

        /// <summary>
        /// 탐색 필드 쉼터(ShelterZone)에서 KO 상태 센트리를 부활시킵니다.
        /// 배틀 필드에서는 호출되지 않습니다.
        /// </summary>
        public void Revive(int healAmount = 0)
        {
            if (!IsKnockedOut) return;

            IsKnockedOut = false;
            _currentHp = (healAmount <= 0) ? _maxHp : Mathf.Min(healAmount, _maxHp);
            _isFollowing = true;

            transform.localScale = Vector3.zero;
            transform.DOScale(Vector3.one, 0.4f).SetEase(Ease.OutBack);
            if (_spriteRenderer != null) _spriteRenderer.color = Color.white;

            Debug.Log($"<color=lime>[{_sentryName} 부활]</color> HP: {_currentHp}");
        }

        // ─────────────────────────────────────────
        //  기절
        // ─────────────────────────────────────────

        private void KnockOut()
        {
            IsKnockedOut = true;
            IsOverloaded = false;
            _isFollowing = false;
            if (_rb != null) _rb.linearVelocity = Vector2.zero;

            if (_spriteRenderer != null)
                _spriteRenderer.DOColor(Color.gray, 0.3f);

            transform.DOScale(new Vector3(1f, 0.3f, 1f), 0.3f).SetEase(Ease.OutBounce);

            Debug.Log($"<color=red>[{_sentryName} KO]</color> " +
                      "기절 — 탐색 필드 쉼터에서 부활 가능");
        }

        /// <summary>과부화 종료 후 PlayerAbility가 강제 기절 시 호출합니다.</summary>
        public void ForceKnockOut()
        {
            if (!IsKnockedOut) KnockOut();
        }

        // ─────────────────────────────────────────
        //  무적 (콤보 시스템 — 배틀 필드 전용)
        // ─────────────────────────────────────────

        /// <summary>
        /// 무적 상태를 설정합니다.
        /// 배틀 필드(쿼터뷰) 콤보 연출 중 ComboManager가 호출합니다.
        /// </summary>
        public void SetInvincible(bool on)
        {
            _isInvincible = on;
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
        //  과부화 (PlayerAbility — 배틀 필드 전용)
        // ─────────────────────────────────────────

        /// <summary>
        /// 과부화 상태를 설정합니다.
        /// 배틀 필드에서 PlayerAbility.OverloadRoutine()이 호출합니다.
        /// </summary>
        public void SetOverload(bool on, float damageMultiplier, float speedMultiplier)
        {
            IsOverloaded = on;
            OverloadDamageMultiplier = on ? damageMultiplier : 1f;
            OverloadSpeedMultiplier = on ? speedMultiplier : 1f;

            if (_spriteRenderer == null) return;

            if (on)
            {
                _spriteRenderer.DOKill();
                _spriteRenderer.DOColor(Color.red, 0.1f).SetLoops(-1, LoopType.Yoyo);
                transform.DOPunchScale(Vector3.one * 0.25f, 0.4f, 6, 0.5f);
                Debug.Log($"<color=red>[{_sentryName} 과부화 ON]</color> " +
                          $"공격력 x{damageMultiplier} / 속도 x{speedMultiplier}");
            }
            else
            {
                _spriteRenderer.DOKill();
                _spriteRenderer.color = Color.white;
                Debug.Log($"[{_sentryName}] 과부화 해제");
            }
        }

        // ─────────────────────────────────────────
        //  경험치 / 레벨 (SentryGrowthDataSO 연동)
        // ─────────────────────────────────────────

        /// <summary>
        /// 경험치를 획득합니다.
        /// 배틀 필드 종료 후 BattleManager.DistributeExp()에서 호출합니다.
        /// </summary>
        public void GainExp(int amount)
        {
            int maxLvl = _growthData != null ? _growthData.maxLevel : _maxLevel;
            if (_currentLevel >= maxLvl) return;

            _currentExp += amount;
            int required = RequiredExp;

            if (_currentExp >= required)
            {
                _currentExp -= required;
                LevelUp();
            }

            Debug.Log($"[{_sentryName}] EXP +{amount} ({_currentExp}/{RequiredExp})");
        }

        /// <summary>
        /// 레벨업 처리. GrowthData 기반으로 HP를 증가시킵니다.
        /// 자식 클래스에서 Override하여 고유 스탯(공격력/속도 등)을 추가 증가시킵니다.
        /// </summary>
        protected virtual void LevelUp()
        {
            _currentLevel++;

            float hpMult = _growthData != null ? _growthData.hpMultiplierPerLevel : 1.1f;
            _maxHp = Mathf.RoundToInt(_maxHp * hpMult);
            _currentHp = _maxHp;

            Debug.Log($"<color=yellow>[{_sentryName} 레벨업!]</color> " +
                      $"Lv.{_currentLevel} / 최대 HP: {_maxHp}");

            transform.DOPunchScale(Vector3.one * 0.3f, 0.4f, 5, 0.5f);

            // UIManager 레벨업 연출 호출
            if (UIManager.Instance != null)
                UIManager.Instance.PlayLevelUpEffect(_sentryName, _currentLevel);
        }

        /// <summary>GrowthData 공격력 증가 배율 반환. 자식 클래스 LevelUp()에서 사용.</summary>
        protected float GetDamageMultiplier() =>
            _growthData != null ? _growthData.damageMultiplierPerLevel : 1.1f;

        /// <summary>GrowthData 속도 증가량 반환. 자식 클래스 LevelUp()에서 사용.</summary>
        protected float GetSpeedBonus() =>
            _growthData != null ? _growthData.speedBonusPerLevel : 0.1f;

        /// <summary>GrowthData 스킬 게이지 충전량 증가값 반환.</summary>
        protected float GetSkillGaugeBonus() =>
            _growthData != null ? _growthData.skillGaugeBonusPerLevel : 2f;

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            // 추종 정지 반경 (하늘색) - 탐색 필드용
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, _followStopDistance);

            // 탐색 필드 포메이션 목표 위치 (녹색) - 플레이 중에만
            if (Application.isPlaying && _playerTransform != null)
            {
                Gizmos.color = Color.green;
                Vector3 fp = FormationWorldPosition;
                Gizmos.DrawSphere(fp, 0.15f);
                Gizmos.DrawLine(transform.position, fp);
            }
        }
#endif
    }
}