using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using DG.Tweening;
namespace SENTRY
{
    /// <summary>
    /// 플레이어의 체력(HP)을 관리하는 컴포넌트.
    /// 
    /// [설계 의도]
    /// - 피격 처리, 무적 시간, 사망 연출을 담당합니다.
    /// - 플레이어가 직접 전투하지 않으므로 '피격만 받는' 구조입니다.
    ///   (센트리가 전투를 담당하고, 플레이어는 적과 접촉 시 데미지를 받습니다.)
    /// - isDead 프로퍼티를 외부에서 읽어 Enemy, BattleManager 등이 상태를 확인합니다.
    /// 
    /// [히어라키 위치]
    /// Player (GameObject)
    ///   ├── PlayerController
    ///   └── PlayerHealth (이 스크립트)
    ///         └── PlayerSprite (SpriteRenderer 참조용 자식)
    /// </summary>
    public class PlayerHealth : MonoBehaviour
    {
        // ─────────────────────────────────────────
        //  Inspector 노출 필드
        // ─────────────────────────────────────────

        [Header("체력 설정")]
        [Tooltip("플레이어의 최대 HP")]
        [SerializeField] private int _maxHp = 100;

        [Header("UI 참조")]
        [Tooltip("HP를 표시할 Slider UI. 없으면 UI 갱신이 생략됩니다.")]
        [SerializeField] private Slider _hpSlider;

        [Header("무적 설정")]
        [Tooltip("피격 후 무적 상태가 유지되는 시간 (초)")]
        [SerializeField] private float _invincibilityDuration = 1f;

        [Header("스프라이트 참조")]
        [Tooltip("피격 깜빡임 연출에 사용할 SpriteRenderer")]
        [SerializeField] private SpriteRenderer _spriteRenderer;

        [Tooltip("레이어 전환 대상 게임오브젝트 (보통 자기 자신 or PlayerSprite 오브젝트)")]
        [SerializeField] private GameObject _playerObject;

        [Header("레이어 이름")]
        [Tooltip("일반 상태 레이어 이름. Project Settings > Tags and Layers에서 확인하세요.")]
        [SerializeField] private string _playerLayerName = "Player";

        [Tooltip("무적 상태 레이어 이름. 적의 공격이 이 레이어를 무시해야 합니다.")]
        [SerializeField] private string _invincibleLayerName = "InvinciblePlayer";

        // ─────────────────────────────────────────
        //  내부 상태 변수
        // ─────────────────────────────────────────

        /// <summary>현재 HP. _maxHp 이하, 0 이상의 값을 가집니다.</summary>
        private int _currentHp;

        /// <summary>현재 무적 상태 여부. 무적일 때는 TakeDamage가 무시됩니다.</summary>
        private bool _isInvincible = false;

        // ─────────────────────────────────────────
        //  외부 공개 프로퍼티
        // ─────────────────────────────────────────

        /// <summary>
        /// 플레이어 사망 여부. true가 되면 더 이상 데미지를 받지 않습니다.
        /// Enemy, BattleManager 등에서 읽어 공격 중단 여부를 결정합니다.
        /// </summary>
        public bool IsDead { get; private set; } = false;

        /// <summary>현재 HP를 외부에서 읽을 수 있는 프로퍼티</summary>
        public int CurrentHp => _currentHp;

        /// <summary>최대 HP를 외부에서 읽을 수 있는 프로퍼티</summary>
        public int MaxHp => _maxHp;

        // ─────────────────────────────────────────
        //  유니티 생명주기
        // ─────────────────────────────────────────

        private void Awake()
        {
            // HP를 최대값으로 초기화
            _currentHp = _maxHp;
        }

        private void Start()
        {
            // HP Slider가 연결되어 있으면 초기값 설정
            if (_hpSlider != null)
            {
                _hpSlider.maxValue = _maxHp;
                _hpSlider.value = _currentHp;
            }
        }

        // ─────────────────────────────────────────
        //  데미지 처리
        // ─────────────────────────────────────────

        /// <summary>
        /// 외부에서 호출하여 플레이어에게 데미지를 줍니다.
        /// 무적 상태이거나 이미 사망했다면 아무 일도 일어나지 않습니다.
        /// </summary>
        /// <param name="damage">입힐 데미지 양 (양수)</param>
        public void TakeDamage(int damage)
        {
            // 무적 상태 또는 이미 사망한 경우 무시
            if (_isInvincible || IsDead) return;

            _currentHp -= damage;
            _currentHp = Mathf.Max(_currentHp, 0); // 0 아래로 내려가지 않도록 클램프

            Debug.Log($"<color=orange>[플레이어 피격]</color> 데미지: {damage} / 남은 HP: {_currentHp}");

            // HP Slider를 DOTween으로 부드럽게 갱신
            if (_hpSlider != null)
                _hpSlider.DOValue(_currentHp, 0.2f);

            if (_currentHp <= 0)
            {
                // HP가 0이 되는 순간 사망 처리 (중복 호출 방지)
                IsDead = true;
                Debug.Log("<color=red>[플레이어 사망]</color>");
                Die();
            }
            else
            {
                // 살아 있다면 무적 코루틴 시작
                StartCoroutine(InvincibilityRoutine());
            }
        }

        /// <summary>
        /// 외부에서 HP를 회복시킵니다. (쉼터 구역, 긴급 수리 스킬 등에서 사용)
        /// 사망 후에는 회복이 불가합니다.
        /// </summary>
        /// <param name="amount">회복할 HP 양 (양수)</param>
        public void Heal(int amount)
        {
            if (IsDead) return;

            _currentHp = Mathf.Min(_currentHp + amount, _maxHp);

            if (_hpSlider != null)
                _hpSlider.DOValue(_currentHp, 0.3f);

            Debug.Log($"<color=green>[플레이어 회복]</color> +{amount} HP / 현재: {_currentHp}");
        }

        // ─────────────────────────────────────────
        //  무적 코루틴
        // ─────────────────────────────────────────

        /// <summary>
        /// 피격 직후 무적 시간을 부여하고 깜빡임 연출을 재생합니다.
        /// _invincibilityDuration 초 후 무적이 해제됩니다.
        /// </summary>
        private IEnumerator InvincibilityRoutine()
        {
            // 무적 시작: 레이어를 InvinciblePlayer로 전환
            _isInvincible = true;
            if (_playerObject != null)
                _playerObject.layer = LayerMask.NameToLayer(_invincibleLayerName);

            // 스프라이트를 빠르게 깜빡여 무적 상태를 시각적으로 표현
            if (_spriteRenderer != null)
                _spriteRenderer.DOFade(0.2f, 0.1f).SetLoops(-1, LoopType.Yoyo);

            yield return new WaitForSeconds(_invincibilityDuration);

            // 무적 종료: 레이어와 스프라이트 원상 복구
            _isInvincible = false;
            if (_playerObject != null)
                _playerObject.layer = LayerMask.NameToLayer(_playerLayerName);

            if (_spriteRenderer != null)
            {
                _spriteRenderer.DOKill();
                _spriteRenderer.color = Color.white; // 원래 색으로 복구
            }
        }

        // ─────────────────────────────────────────
        //  사망 처리
        // ─────────────────────────────────────────

        /// <summary>
        /// HP가 0이 되었을 때 호출됩니다.
        /// 사망 연출 후 GameManager에 게임오버를 알립니다.
        /// </summary>
        private void Die()
        {
            // GameManager가 존재할 경우 게임오버 처리 위임
            if (GameManager.Instance != null)
                GameManager.Instance.GameOver(false);

            // DOTween으로 캐릭터 축소 사망 연출
            transform.DOScale(Vector3.zero, 0.5f).SetEase(Ease.InBack);
        }
    }
}