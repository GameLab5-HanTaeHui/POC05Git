using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;
using System.Collections;

namespace SENTRY
{
    /// <summary>
    /// 배틀 필드의 적 캐릭터 HUD를 전담하는 싱글턴 매니저.
    ///
    /// [구조]
    /// - EnemySlot1~3은 씬에 항상 활성 상태로 배치합니다.
    /// - _enemyHudPanel(RectTransform) 전체가 화면 밖에서 슬라이드 인/아웃합니다.
    /// - 적이 소환될 때 SpawnEntries 순서대로 슬롯에 데이터(이름/레벨/HP)를 채웁니다.
    /// - SetActive / CanvasGroup alpha 같은 숨김 처리 없음.
    ///
    /// [히어라키 위치]
    /// Canvas
    ///   └── EnemyHUDPanel       ← _enemyHudPanel (RectTransform, 슬라이드 루트)
    ///         ├── EnemySlot1    ← 항상 활성
    ///         │     ├── NameText
    ///         │     ├── LevelText
    ///         │     ├── HpFill
    ///         │     ├── HpText
    ///         │     └── KoIcon
    ///         ├── EnemySlot2    ← 항상 활성
    ///         └── EnemySlot3    ← 항상 활성
    /// </summary>
    public class EnemyBattleUIManager : MonoBehaviour
    {
        // ─────────────────────────────────────────
        //  싱글턴
        // ─────────────────────────────────────────

        public static EnemyBattleUIManager Instance { get; private set; }

        // ─────────────────────────────────────────
        //  Inspector — 슬라이드 패널
        // ─────────────────────────────────────────

        [Header("슬라이드 패널")]
        [Tooltip("적 HUD 루트 RectTransform.\n" +
                 "이 오브젝트 전체가 화면 밖 → 안으로 슬라이드합니다.")]
        [SerializeField] private RectTransform _enemyHudPanel;

        [Header("슬라이드 방향")]
        [Tooltip("true = X축 / false = Y축")]
        [SerializeField] private bool _slideOnX = true;

        [Tooltip("true = 양수 방향 진입 (오른쪽/위) / false = 음수 방향 (왼쪽/아래)")]
        [SerializeField] private bool _slideFromPositive = true;

        [Tooltip("슬라이드 이동 거리 (px)")]
        [SerializeField] private float _slideDistance = 600f;

        [Header("슬라이드 애니메이션")]
        [SerializeField] private float _slideDuration = 0.5f;
        [SerializeField] private Ease _slideInEase = Ease.OutBack;
        [SerializeField] private Ease _slideOutEase = Ease.InBack;

        // ─────────────────────────────────────────
        //  Inspector — 슬롯 1
        // ─────────────────────────────────────────

        [Header("슬롯 1")]
        [SerializeField] private TMP_Text _enemy1NameText;
        [SerializeField] private TMP_Text _enemy1LevelText;
        [SerializeField] private Image _enemy1HpFill;
        [SerializeField] private TMP_Text _enemy1HpText;
        [SerializeField] private GameObject _enemy1KoIcon;

        // ─────────────────────────────────────────
        //  Inspector — 슬롯 2
        // ─────────────────────────────────────────

        [Header("슬롯 2")]
        [SerializeField] private TMP_Text _enemy2NameText;
        [SerializeField] private TMP_Text _enemy2LevelText;
        [SerializeField] private Image _enemy2HpFill;
        [SerializeField] private TMP_Text _enemy2HpText;
        [SerializeField] private GameObject _enemy2KoIcon;

        // ─────────────────────────────────────────
        //  Inspector — 슬롯 3
        // ─────────────────────────────────────────

        [Header("슬롯 3")]
        [SerializeField] private TMP_Text _enemy3NameText;
        [SerializeField] private TMP_Text _enemy3LevelText;
        [SerializeField] private Image _enemy3HpFill;
        [SerializeField] private TMP_Text _enemy3HpText;
        [SerializeField] private GameObject _enemy3KoIcon;

        [Header("갱신 설정")]
        [SerializeField] private float _hudRefreshRate = 0.1f;

        // ─────────────────────────────────────────
        //  내부 상태 변수
        // ─────────────────────────────────────────

        /// <summary>슬롯 0~2에 연결된 Enemy 참조 배열</summary>
        private Enemy[] _enemies = new Enemy[3];

        /// <summary>화면 내 기본 앵커 위치 (슬라이드 인 목표)</summary>
        private Vector2 _panelOnPos;

        private Tween _slideTween;

        // ─────────────────────────────────────────
        //  유니티 생명주기
        // ─────────────────────────────────────────

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else { Destroy(gameObject); return; }

            // ── 중요: 이 오브젝트는 Managers 루트에 배치하세요 ──
            // BattleField 하위에 배치하면 씬 시작 시 비활성 상태라
            // Awake()가 실행되지 않아 Instance가 null이 됩니다.
            // EnemyHUDPanel은 Canvas 하위에 두고,
            // 이 오브젝트(EnemyBattleUIManager)는 --- Managers --- 하위에 배치하세요.
            Debug.Log("[EnemyBattleUIManager] Awake — Instance 초기화 완료");
        }

        private void Start()
        {
            if (_enemyHudPanel != null)
            {
                _panelOnPos = _enemyHudPanel.anchoredPosition;
                _enemyHudPanel.anchoredPosition =
                    GetOffscreenPos(_panelOnPos, _slideOnX, _slideFromPositive, _slideDistance);
            }
            else
            {
                Debug.LogWarning("[EnemyBattleUIManager] ★ _enemyHudPanel 미연결 — Inspector 확인 필요");
            }

            SetKoIcon(0, false);
            SetKoIcon(1, false);
            SetKoIcon(2, false);

            StartCoroutine(HudRefreshRoutine());

            Debug.Log("[EnemyBattleUIManager] Start 완료");
        }

        // ─────────────────────────────────────────
        //  슬라이드 인 / 아웃
        // ─────────────────────────────────────────

        /// <summary>
        /// 배틀 진입 시 EnemyHUDPanel을 화면 밖 → 안으로 슬라이드합니다.
        /// BattleUIManager.SetBattleHudActive(true)에서 호출합니다.
        /// </summary>
        public void SlideIn()
        {
            if (_enemyHudPanel == null) return;
            _slideTween?.Kill();
            _slideTween = _enemyHudPanel
                .DOAnchorPos(_panelOnPos, _slideDuration)
                .SetEase(_slideInEase);
        }

        /// <summary>
        /// 배틀 종료 시 EnemyHUDPanel을 화면 안 → 밖으로 슬라이드합니다.
        /// 슬라이드 아웃 완료 후 ClearAllSlots()가 자동 호출됩니다.
        /// </summary>
        public void SlideOut()
        {
            if (_enemyHudPanel == null) return;

            Vector2 offPos = GetOffscreenPos(
                _panelOnPos, _slideOnX, _slideFromPositive, _slideDistance);

            _slideTween?.Kill();
            _slideTween = _enemyHudPanel
                .DOAnchorPos(offPos, _slideDuration)
                .SetEase(_slideOutEase)
                .OnComplete(ClearAllSlots);
        }

        private Vector2 GetOffscreenPos(
            Vector2 onPos, bool slideOnX, bool fromPositive, float distance)
        {
            float offset = fromPositive ? distance : -distance;
            return slideOnX
                ? new Vector2(onPos.x + offset, onPos.y)
                : new Vector2(onPos.x, onPos.y + offset);
        }

        // ─────────────────────────────────────────
        //  Enemy 등록 / 사망 처리
        // ─────────────────────────────────────────

        /// <summary>
        /// 소환된 적을 슬롯에 순서대로 등록하고 데이터를 채웁니다.
        /// SpawnEntries 순서대로 슬롯 1 → 2 → 3 순으로 채워집니다.
        /// EnemyComboManager.RegisterEnemy() 내부에서 호출합니다.
        /// </summary>
        public void RegisterEnemy(Enemy enemy)
        {
            if (enemy == null) return;

            for (int i = 0; i < _enemies.Length; i++)
            {
                if (_enemies[i] != null) continue;

                _enemies[i] = enemy;
                SetKoIcon(i, false);
                SetSlotName(i, enemy.name);
                SetSlotLevel(i, enemy.Level);
                RefreshSlotHp(i);

                Debug.Log($"[EnemyBattleUI] 슬롯{i + 1} 등록: {enemy.name} Lv.{enemy.Level}");
                return;
            }

            Debug.LogWarning("[EnemyBattleUI] 슬롯 3개가 모두 채워졌습니다.");
        }

        /// <summary>
        /// 적 사망 시 KO 아이콘을 표시하고 HP 바를 0으로 갱신합니다.
        /// Enemy.Die()에서 직접 호출합니다.
        /// </summary>
        public void OnEnemyDied(Enemy enemy)
        {
            if (enemy == null) return;

            for (int i = 0; i < _enemies.Length; i++)
            {
                if (_enemies[i] != enemy) continue;

                SetKoIcon(i, true);
                SetHpFill(i, 0f, 0, enemy.MaxHp);

                Debug.Log($"[EnemyBattleUI] 슬롯{i + 1} KO: {enemy.name}");
                return;
            }
        }

        /// <summary>
        /// 배틀 종료 후 슬롯 데이터를 초기화합니다.
        /// SlideOut() 완료 후 자동 호출됩니다.
        /// </summary>
        private void ClearAllSlots()
        {
            for (int i = 0; i < _enemies.Length; i++)
            {
                _enemies[i] = null;
                SetSlotName(i, string.Empty);
                SetSlotLevel(i, 0);
                SetHpFill(i, 0f, 0, 0);
                SetKoIcon(i, false);
            }

            Debug.Log("[EnemyBattleUI] 슬롯 초기화 완료");
        }

        // ─────────────────────────────────────────
        //  HP 주기 갱신
        // ─────────────────────────────────────────

        private IEnumerator HudRefreshRoutine()
        {
            while (true)
            {
                for (int i = 0; i < _enemies.Length; i++)
                {
                    if (_enemies[i] == null || _enemies[i].IsDead) continue;
                    RefreshSlotHp(i);
                }
                yield return new WaitForSeconds(_hudRefreshRate);
            }
        }

        private void RefreshSlotHp(int i)
        {
            Enemy e = _enemies[i];
            if (e == null) return;
            float ratio = e.MaxHp > 0 ? (float)e.CurrentHp / e.MaxHp : 0f;
            SetHpFill(i, ratio, e.CurrentHp, e.MaxHp);
        }

        // ─────────────────────────────────────────
        //  슬롯 UI 헬퍼
        // ─────────────────────────────────────────

        private void SetSlotName(int i, string val)
        {
            TMP_Text t = i switch { 0 => _enemy1NameText, 1 => _enemy2NameText, 2 => _enemy3NameText, _ => null };
            if (t != null) t.text = val;
        }

        private void SetSlotLevel(int i, int level)
        {
            TMP_Text t = i switch { 0 => _enemy1LevelText, 1 => _enemy2LevelText, 2 => _enemy3LevelText, _ => null };
            if (t != null) t.text = level > 0 ? $"Lv.{level}" : string.Empty;
        }

        private void SetHpFill(int i, float ratio, int current, int max)
        {
            Image fill = i switch { 0 => _enemy1HpFill, 1 => _enemy2HpFill, 2 => _enemy3HpFill, _ => null };
            TMP_Text txt = i switch { 0 => _enemy1HpText, 1 => _enemy2HpText, 2 => _enemy3HpText, _ => null };

            if (fill != null) fill.fillAmount = ratio;
            if (txt != null) txt.text = max > 0 ? $"{current}/{max}" : string.Empty;
        }

        private void SetKoIcon(int i, bool active)
        {
            GameObject icon = i switch { 0 => _enemy1KoIcon, 1 => _enemy2KoIcon, 2 => _enemy3KoIcon, _ => null };
            if (icon != null) icon.SetActive(active);
        }
    }
}