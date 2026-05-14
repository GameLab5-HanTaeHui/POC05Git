using UnityEngine;
using DG.Tweening;
using System.Collections;

namespace SENTRY
{
    /// <summary>
    /// 사격 센트리(ShootSentry) 고유 스킬 연출 전담 컴포넌트.
    ///
    /// [설계 의도]
    /// - 3연발 발사 시 각 탄환마다 반동 연출과 총구 플래시를 재생합니다.
    /// - LineRenderer로 레이저 조준선을 짧게 표시한 뒤 탄환을 발사합니다.
    ///   (기존 ShootComboEffect.cs의 laserLineRenderer 연출을 계승)
    /// - ShootSentry.UseSkill()에서 PlaySkill()을 호출합니다.
    ///
    /// [히어라키 위치]
    /// ShootSentry
    ///   ├── SentryBase
    ///   ├── ShootSentry
    ///   └── SkillEffect_Shoot (이 스크립트 + LineRenderer)
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public class SkillEffect_Shoot : MonoBehaviour
    {
        // ─────────────────────────────────────────
        //  Inspector 노출 필드
        // ─────────────────────────────────────────

        [Header("조준선 설정")]
        [Tooltip("발사 전 조준선 표시 시간 (초)")]
        [SerializeField] private float _aimDuration = 0.3f;

        [Header("반동 설정")]
        [Tooltip("발사 시 센트리가 뒤로 밀리는 거리")]
        [SerializeField] private float _recoilDistance = 0.15f;

        [Tooltip("반동 소요 시간 (초)")]
        [SerializeField] private float _recoilDuration = 0.07f;

        [Header("탄환 발사 간격")]
        [Tooltip("3연발 각 탄환 사이의 간격 (초)")]
        [SerializeField] private float _burstInterval = 0.12f;

        [Header("총구 플래시")]
        [Tooltip("발사 시 총구에 생성할 플래시 이펙트 프리팹 (없으면 생략)")]
        [SerializeField] private GameObject _muzzleFlashPrefab;

        [Tooltip("총구 위치 Transform. 없으면 센트리 중앙 사용.")]
        [SerializeField] private Transform _firePoint;

        [Header("스프라이트")]
        [Tooltip("색상 연출에 사용할 SpriteRenderer")]
        [SerializeField] private SpriteRenderer _spriteRenderer;

        // ─────────────────────────────────────────
        //  내부 상태 변수
        // ─────────────────────────────────────────

        /// <summary>조준선 렌더러 캐시</summary>
        private LineRenderer _lineRenderer;

        /// <summary>스킬 연출 재생 중 여부</summary>
        private bool _isPlaying = false;

        // ─────────────────────────────────────────
        //  외부 공개 프로퍼티
        // ─────────────────────────────────────────

        /// <summary>스킬 연출 재생 중 여부. ShootSentry에서 AI 정지 판단에 사용합니다.</summary>
        public bool IsPlaying => _isPlaying;

        // ─────────────────────────────────────────
        //  유니티 생명주기
        // ─────────────────────────────────────────

        private void Awake()
        {
            _lineRenderer = GetComponent<LineRenderer>();
            if (_lineRenderer != null) _lineRenderer.enabled = false;
        }

        // ─────────────────────────────────────────
        //  스킬 연출 실행
        // ─────────────────────────────────────────

        /// <summary>
        /// 3연발 스킬 연출을 재생합니다.
        /// ShootSentry.UseSkill()에서 호출합니다.
        /// </summary>
        /// <param name="target">사격 대상 Transform</param>
        /// <param name="onEachShot">각 발사 시점에 호출할 콜백 (실제 탄환 생성)</param>
        public void PlaySkill(Transform target, System.Action onEachShot)
        {
            if (_isPlaying || target == null) return;
            StartCoroutine(SkillRoutine(target, onEachShot));
        }

        /// <summary>
        /// 조준 → 3연발 → 조준선 해제 순서의 연출 코루틴.
        /// </summary>
        private IEnumerator SkillRoutine(Transform target, System.Action onEachShot)
        {
            _isPlaying = true;

            // ── 1. 조준선 표시 ──
            ShowAimLine(target);

            // 스프라이트 조준 상태 강조 (살짝 밝아짐)
            if (_spriteRenderer != null)
                _spriteRenderer.DOColor(Color.cyan * 1.3f, _aimDuration);

            yield return new WaitForSeconds(_aimDuration);

            // ── 2. 3연발 발사 ──
            for (int i = 0; i < 3; i++)
            {
                if (target == null) break;

                // 조준선 업데이트 (타겟이 움직일 수 있으므로)
                UpdateAimLine(target);

                // 실제 탄환 생성 콜백 (ShootSentry가 Bullet을 Instantiate)
                onEachShot?.Invoke();

                // 총구 플래시 연출
                SpawnMuzzleFlash();

                // 반동 연출: 발사 방향 반대로 밀림
                Vector3 recoilDir = (transform.position - target.position).normalized;
                transform.DOPunchPosition(recoilDir * _recoilDistance, _recoilDuration, 5, 0.3f);

                // 스프라이트 흰색 플래시
                if (_spriteRenderer != null)
                    _spriteRenderer.DOColor(Color.white, 0.04f)
                        .SetLoops(2, LoopType.Yoyo)
                        .OnComplete(() => _spriteRenderer.color = Color.white);

                yield return new WaitForSeconds(_burstInterval);
            }

            // ── 3. 조준선 해제 ──
            HideAimLine();

            if (_spriteRenderer != null)
                _spriteRenderer.DOColor(Color.white, 0.15f);

            _isPlaying = false;
            Debug.Log("[SkillEffect_Shoot] 3연발 연출 완료");
        }

        // ─────────────────────────────────────────
        //  조준선 (기존 ShootComboEffect.cs 계승)
        // ─────────────────────────────────────────

        /// <summary>
        /// 센트리에서 타겟까지 레이저 조준선을 활성화합니다.
        /// </summary>
        private void ShowAimLine(Transform target)
        {
            if (_lineRenderer == null || target == null) return;

            _lineRenderer.enabled = true;
            _lineRenderer.positionCount = 2;
            _lineRenderer.SetPosition(0, transform.position);
            _lineRenderer.SetPosition(1, target.position);
        }

        /// <summary>
        /// 이미 표시된 조준선의 끝점을 타겟의 현재 위치로 업데이트합니다.
        /// </summary>
        private void UpdateAimLine(Transform target)
        {
            if (_lineRenderer == null || !_lineRenderer.enabled || target == null) return;

            _lineRenderer.SetPosition(0, transform.position);
            _lineRenderer.SetPosition(1, target.position);
        }

        /// <summary>조준선을 비활성화합니다.</summary>
        private void HideAimLine()
        {
            if (_lineRenderer != null)
                _lineRenderer.enabled = false;
        }

        // ─────────────────────────────────────────
        //  총구 플래시
        // ─────────────────────────────────────────

        /// <summary>
        /// 총구 위치에 플래시 이펙트를 생성하고 빠르게 소멸시킵니다.
        /// _muzzleFlashPrefab이 없으면 생략됩니다.
        /// </summary>
        private void SpawnMuzzleFlash()
        {
            if (_muzzleFlashPrefab == null) return;

            Vector3 spawnPos = (_firePoint != null) ? _firePoint.position : transform.position;
            GameObject flash = Instantiate(_muzzleFlashPrefab, spawnPos, Quaternion.identity);

            // 빠르게 커졌다가 소멸
            flash.transform.localScale = Vector3.zero;
            flash.transform.DOScale(Vector3.one * 0.6f, 0.05f).SetEase(Ease.OutQuad);

            SpriteRenderer flashSprite = flash.GetComponent<SpriteRenderer>();
            if (flashSprite != null)
                flashSprite.DOFade(0f, 0.08f).OnComplete(() => Destroy(flash));
            else
                Destroy(flash, 0.1f);
        }
    }
}