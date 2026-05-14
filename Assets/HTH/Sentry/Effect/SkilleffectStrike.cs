using UnityEngine;
using DG.Tweening;
using System.Collections;

namespace SENTRY
{
    /// <summary>
    /// 타격 센트리(StrikeSentry) 고유 스킬 연출 전담 컴포넌트.
    ///
    /// [설계 의도]
    /// - StrikeSentry.cs에서 스킬 판정(데미지/게이지)을 처리하고,
    ///   이 컴포넌트는 순수하게 '보여주는 것'만 담당합니다.
    /// - DOTween 시퀀스를 활용해 2연타 타격 모션을 연출합니다.
    ///   1타: 빠른 전진 + 충격파 스케일 이펙트
    ///   2타: 더 강한 전진 + 화면 흔들림(DOShakePosition) + 복귀
    /// - StrikeSentry.UseSkill()에서 PlaySkill()을 호출합니다.
    ///
    /// [히어라키 위치]
    /// StrikeSentry
    ///   ├── SentryBase
    ///   ├── StrikeSentry
    ///   └── SkillEffect_Strike (이 스크립트)
    /// </summary>
    public class SkillEffect_Strike : MonoBehaviour
    {
        // ─────────────────────────────────────────
        //  Inspector 노출 필드
        // ─────────────────────────────────────────

        [Header("1타 설정")]
        [Tooltip("1타 전진 거리")]
        [SerializeField] private float _firstHitDistance = 0.6f;

        [Tooltip("1타 전진 소요 시간 (초)")]
        [SerializeField] private float _firstHitDuration = 0.1f;

        [Header("2타 설정")]
        [Tooltip("2타 전진 거리. 1타보다 강하게 표현합니다.")]
        [SerializeField] private float _secondHitDistance = 1.0f;

        [Tooltip("2타 전진 소요 시간 (초)")]
        [SerializeField] private float _secondHitDuration = 0.12f;

        [Tooltip("2타 후 원위치 복귀 시간 (초)")]
        [SerializeField] private float _returnDuration = 0.25f;

        [Header("충격 이펙트")]
        [Tooltip("타격 시 생성할 충격파 이펙트 프리팹 (없으면 생략)")]
        [SerializeField] private GameObject _impactEffectPrefab;

        [Tooltip("SpriteRenderer. 피격 시 색상 연출에 사용합니다.")]
        [SerializeField] private SpriteRenderer _spriteRenderer;

        // ─────────────────────────────────────────
        //  내부 상태 변수
        // ─────────────────────────────────────────

        /// <summary>현재 스킬 연출 재생 중 여부 (외부 체크용)</summary>
        private bool _isPlaying = false;

        /// <summary>시작 위치 (복귀용)</summary>
        private Vector3 _originPosition;

        // ─────────────────────────────────────────
        //  외부 공개 프로퍼티
        // ─────────────────────────────────────────

        /// <summary>스킬 연출이 재생 중인지 여부. StrikeSentry에서 AI 정지 판단에 사용합니다.</summary>
        public bool IsPlaying => _isPlaying;

        // ─────────────────────────────────────────
        //  스킬 연출 실행
        // ─────────────────────────────────────────

        /// <summary>
        /// 2연타 스킬 연출을 재생합니다.
        /// StrikeSentry.UseSkill() 코루틴 안에서 호출합니다.
        /// </summary>
        /// <param name="target">타격 대상 적 Transform</param>
        /// <param name="onFirstHit">1타 시점에 호출할 콜백 (데미지 적용)</param>
        /// <param name="onSecondHit">2타 시점에 호출할 콜백 (데미지 적용)</param>
        public void PlaySkill(Transform target, System.Action onFirstHit, System.Action onSecondHit)
        {
            if (_isPlaying || target == null) return;
            StartCoroutine(SkillRoutine(target, onFirstHit, onSecondHit));
        }

        /// <summary>
        /// 2연타 연출 코루틴.
        /// DOTween Sequence로 타이밍을 정밀하게 제어합니다.
        /// </summary>
        private IEnumerator SkillRoutine(Transform target, System.Action onFirstHit, System.Action onSecondHit)
        {
            _isPlaying = true;
            _originPosition = transform.position;

            Vector3 dir = (target.position - transform.position).normalized;

            // ── 1타 ──
            // 빠르게 전진
            yield return transform
                .DOMove(transform.position + dir * _firstHitDistance, _firstHitDuration)
                .SetEase(Ease.OutQuad)
                .WaitForCompletion();

            // 1타 데미지 콜백
            onFirstHit?.Invoke();

            // 충격파 이펙트 생성
            SpawnImpactEffect(target.position);

            // 스프라이트 강조: 밝아졌다가 복귀
            if (_spriteRenderer != null)
                _spriteRenderer.DOColor(Color.white * 2f, 0.05f).SetLoops(2, LoopType.Yoyo);

            // 1타 후 잠깐 정지감
            yield return new WaitForSeconds(0.1f);

            // 약간 뒤로 빠졌다가 2타 준비
            yield return transform
                .DOMove(_originPosition + dir * (_firstHitDistance * 0.4f), 0.08f)
                .SetEase(Ease.InQuad)
                .WaitForCompletion();

            // ── 2타 ──
            // 더 강하게 전진
            yield return transform
                .DOMove(_originPosition + dir * _secondHitDistance, _secondHitDuration)
                .SetEase(Ease.OutExpo)
                .WaitForCompletion();

            // 2타 데미지 콜백
            onSecondHit?.Invoke();

            // 충격파 이펙트 (더 크게)
            SpawnImpactEffect(target.position, scale: 1.5f);

            // 화면 흔들림 (카메라 DOShake는 Camera 컴포넌트 필요 - 센트리 자체 흔들림으로 대체)
            transform.DOShakePosition(0.2f, 0.3f, 15, 90f);

            // 스프라이트 강렬한 흰색 플래시
            if (_spriteRenderer != null)
                _spriteRenderer.DOColor(Color.white * 3f, 0.04f).SetLoops(4, LoopType.Yoyo);

            yield return new WaitForSeconds(0.15f);

            // ── 원위치 복귀 ──
            yield return transform
                .DOMove(_originPosition, _returnDuration)
                .SetEase(Ease.OutBack)
                .WaitForCompletion();

            _isPlaying = false;
            Debug.Log("[SkillEffect_Strike] 2연타 연출 완료");
        }

        // ─────────────────────────────────────────
        //  충격파 이펙트
        // ─────────────────────────────────────────

        /// <summary>
        /// 타격 지점에 충격파 이펙트 프리팹을 생성하고 DOTween으로 확대→소멸시킵니다.
        /// _impactEffectPrefab이 없으면 생략됩니다.
        /// </summary>
        /// <param name="position">생성 위치</param>
        /// <param name="scale">이펙트 초기 스케일 배율 (기본 1.0)</param>
        private void SpawnImpactEffect(Vector3 position, float scale = 1.0f)
        {
            if (_impactEffectPrefab == null) return;

            GameObject fx = Instantiate(_impactEffectPrefab, position, Quaternion.identity);
            fx.transform.localScale = Vector3.one * scale;

            // 빠르게 커졌다가 사라지는 연출
            fx.transform.DOScale(Vector3.one * scale * 1.8f, 0.2f)
                .SetEase(Ease.OutQuad);

            SpriteRenderer fxSprite = fx.GetComponent<SpriteRenderer>();
            if (fxSprite != null)
                fxSprite.DOFade(0f, 0.2f).OnComplete(() => Destroy(fx));
            else
                Destroy(fx, 0.2f);
        }
    }
}