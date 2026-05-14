using UnityEngine;
using DG.Tweening;
using System.Collections;

namespace SENTRY
{
    /// <summary>
    /// 벽 센트리(WallSentry) 고유 스킬 연출 전담 컴포넌트.
    ///
    /// [설계 의도]
    /// - 강한 밀치기 스킬 발동 시 아래의 연출을 순서대로 재생합니다:
    ///   1. 준비 자세: 스케일이 살짝 커지며 긴장감 조성 (차지 모션)
    ///   2. 돌진: 타겟 방향으로 빠르게 전진
    ///   3. 충격: 화면 흔들림 + 충격파 이펙트 + 데미지/기절 콜백
    ///   4. 복귀: 원래 위치로 천천히 돌아옴
    /// - 기존 MatterComboWall.cs의 LaunchWall/ExplodeAoE 연출 방식을 계승합니다.
    ///
    /// [히어라키 위치]
    /// WallSentry
    ///   ├── SentryBase
    ///   ├── WallSentry
    ///   └── SkillEffect_Wall (이 스크립트)
    /// </summary>
    public class SkillEffect_Wall : MonoBehaviour
    {
        // ─────────────────────────────────────────
        //  Inspector 노출 필드
        // ─────────────────────────────────────────

        [Header("차지 모션 설정")]
        [Tooltip("차지(준비 자세) 유지 시간 (초). 이 시간 동안 스케일이 커집니다.")]
        [SerializeField] private float _chargeDuration = 0.35f;

        [Tooltip("차지 시 커지는 스케일 배율")]
        [SerializeField] private float _chargeScaleMultiplier = 1.3f;

        [Header("돌진 설정")]
        [Tooltip("돌진 거리 (유니티 유닛)")]
        [SerializeField] private float _dashDistance = 1.2f;

        [Tooltip("돌진 소요 시간 (초). 짧을수록 빠릅니다.")]
        [SerializeField] private float _dashDuration = 0.1f;

        [Header("복귀 설정")]
        [Tooltip("스킬 후 원위치로 복귀하는 시간 (초)")]
        [SerializeField] private float _returnDuration = 0.4f;

        [Header("충격 이펙트")]
        [Tooltip("충격 지점에 생성할 이펙트 프리팹 (없으면 생략)")]
        [SerializeField] private GameObject _impactEffectPrefab;

        [Tooltip("충격 이펙트 크기 배율")]
        [SerializeField] private float _impactEffectScale = 2.0f;

        [Header("스프라이트")]
        [Tooltip("색상 연출에 사용할 SpriteRenderer")]
        [SerializeField] private SpriteRenderer _spriteRenderer;

        // ─────────────────────────────────────────
        //  내부 상태 변수
        // ─────────────────────────────────────────

        /// <summary>스킬 연출 재생 중 여부</summary>
        private bool _isPlaying = false;

        /// <summary>시작 위치 저장 (복귀용)</summary>
        private Vector3 _originPosition;

        /// <summary>시작 스케일 저장 (복귀용)</summary>
        private Vector3 _originScale;

        // ─────────────────────────────────────────
        //  외부 공개 프로퍼티
        // ─────────────────────────────────────────

        /// <summary>스킬 연출 재생 중 여부. WallSentry에서 AI 정지 판단에 사용합니다.</summary>
        public bool IsPlaying => _isPlaying;

        // ─────────────────────────────────────────
        //  스킬 연출 실행
        // ─────────────────────────────────────────

        /// <summary>
        /// 강한 밀치기 스킬 연출을 재생합니다.
        /// WallSentry.UseSkill()에서 호출합니다.
        /// </summary>
        /// <param name="target">밀칠 대상 적 Transform</param>
        /// <param name="onImpact">충격 시점에 호출할 콜백 (데미지 + Stun 적용)</param>
        public void PlaySkill(Transform target, System.Action onImpact)
        {
            if (_isPlaying || target == null) return;
            StartCoroutine(SkillRoutine(target, onImpact));
        }

        /// <summary>
        /// 차지 → 돌진 → 충격 → 복귀 순서의 연출 코루틴.
        /// </summary>
        private IEnumerator SkillRoutine(Transform target, System.Action onImpact)
        {
            _isPlaying = true;
            _originPosition = transform.position;
            _originScale = transform.localScale;

            Vector3 dir = (target.position - transform.position).normalized;

            // ── 1. 차지 모션 (긴장감 조성) ──
            // 스케일 증가 + 스프라이트 색상 강조
            transform.DOScale(_originScale * _chargeScaleMultiplier, _chargeDuration)
                .SetEase(Ease.OutBack);

            if (_spriteRenderer != null)
                _spriteRenderer.DOColor(Color.yellow, _chargeDuration);

            // 좌우 미세 흔들림으로 힘 모으는 느낌
            transform.DOShakePosition(_chargeDuration, 0.05f, 20, 0f);

            yield return new WaitForSeconds(_chargeDuration);

            // ── 2. 돌진 ──
            if (_spriteRenderer != null)
                _spriteRenderer.color = Color.white;

            // 스케일을 원래대로 복귀하면서 동시에 전진 (이 순간 가장 강렬한 순간)
            transform.DOScale(_originScale, _dashDuration);

            yield return transform
                .DOMove(_originPosition + dir * _dashDistance, _dashDuration)
                .SetEase(Ease.InExpo)
                .WaitForCompletion();

            // ── 3. 충격 ──
            // 데미지 + 기절 콜백 (WallSentry가 Enemy.TakeDamage, Enemy.Stun 호출)
            onImpact?.Invoke();

            // 충격파 이펙트 생성
            SpawnImpactEffect(target != null ? target.position : transform.position + dir);

            // 화면(센트리 자체) 강한 흔들림
            transform.DOShakePosition(0.3f, 0.4f, 20, 90f);

            // 스프라이트 흰색 플래시
            if (_spriteRenderer != null)
                _spriteRenderer.DOColor(Color.white * 4f, 0.04f)
                    .SetLoops(4, LoopType.Yoyo)
                    .OnComplete(() => _spriteRenderer.color = Color.white);

            yield return new WaitForSeconds(0.2f);

            // ── 4. 원위치 복귀 ──
            yield return transform
                .DOMove(_originPosition, _returnDuration)
                .SetEase(Ease.OutElastic)
                .WaitForCompletion();

            _isPlaying = false;
            Debug.Log("[SkillEffect_Wall] 밀치기 스킬 연출 완료");
        }

        // ─────────────────────────────────────────
        //  충격파 이펙트
        // ─────────────────────────────────────────

        /// <summary>
        /// 충격 지점에 이펙트 프리팹을 생성하고 확대→소멸시킵니다.
        /// </summary>
        private void SpawnImpactEffect(Vector3 position)
        {
            if (_impactEffectPrefab == null) return;

            GameObject fx = Instantiate(_impactEffectPrefab, position, Quaternion.identity);
            fx.transform.localScale = Vector3.one * 0.1f;

            // 빠르게 팽창 후 페이드아웃
            fx.transform.DOScale(Vector3.one * _impactEffectScale, 0.25f)
                .SetEase(Ease.OutQuad);

            SpriteRenderer fxSprite = fx.GetComponent<SpriteRenderer>();
            if (fxSprite != null)
                fxSprite.DOFade(0f, 0.25f).OnComplete(() => Destroy(fx));
            else
                Destroy(fx, 0.25f);
        }
    }
}