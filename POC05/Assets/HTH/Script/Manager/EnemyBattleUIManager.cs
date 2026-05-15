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
    /// [슬롯 구성]
    ///   NameText  : 적 이름
    ///   LevelText : 레벨
    ///   HpFill    : HP 바 (Image Filled)
    ///   HpText    : HP 수치 텍스트
    ///   SkillFill : 스킬 게이지 (Image Filled) — Enemy.SkillGauge / MaxSkillGauge
    ///   StunIcon  : 기절 상태 아이콘 (기절 중 표시)
    ///   KoIcon    : KO 아이콘 (사망 시 표시)
    ///
    /// [히어라키 위치]
    /// Canvas
    ///   └── EnemyHUDPanel
    ///         ├── EnemySlot1
    ///         │     ├── NameText
    ///         │     ├── LevelText
    ///         │     ├── HpFill    (Image Filled)
    ///         │     ├── HpText
    ///         │     ├── SkillFill (Image Filled)
    ///         │     ├── StunIcon  (GameObject)
    ///         │     └── KoIcon    (GameObject)
    ///         ├── EnemySlot2
    ///         └── EnemySlot3
    /// </summary>
    public class EnemyBattleUIManager : MonoBehaviour
    {
        public static EnemyBattleUIManager Instance { get; private set; }

        [Header("슬라이드 패널")]
        [SerializeField] private RectTransform _enemyHudPanel;
        [SerializeField] private bool _slideOnX = true;
        [SerializeField] private bool _slideFromPositive = true;
        [SerializeField] private float _slideDistance = 600f;
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
        [SerializeField] private Image _enemy1SkillFill;
        [SerializeField] private GameObject _enemy1StunIcon;
        [SerializeField] private GameObject _enemy1KoIcon;

        // ─────────────────────────────────────────
        //  Inspector — 슬롯 2
        // ─────────────────────────────────────────

        [Header("슬롯 2")]
        [SerializeField] private TMP_Text _enemy2NameText;
        [SerializeField] private TMP_Text _enemy2LevelText;
        [SerializeField] private Image _enemy2HpFill;
        [SerializeField] private TMP_Text _enemy2HpText;
        [SerializeField] private Image _enemy2SkillFill;
        [SerializeField] private GameObject _enemy2StunIcon;
        [SerializeField] private GameObject _enemy2KoIcon;

        // ─────────────────────────────────────────
        //  Inspector — 슬롯 3
        // ─────────────────────────────────────────

        [Header("슬롯 3")]
        [SerializeField] private TMP_Text _enemy3NameText;
        [SerializeField] private TMP_Text _enemy3LevelText;
        [SerializeField] private Image _enemy3HpFill;
        [SerializeField] private TMP_Text _enemy3HpText;
        [SerializeField] private Image _enemy3SkillFill;
        [SerializeField] private GameObject _enemy3StunIcon;
        [SerializeField] private GameObject _enemy3KoIcon;

        [Header("갱신 설정")]
        [SerializeField] private float _hudRefreshRate = 0.1f;

        // ─────────────────────────────────────────
        //  Inspector — 마커 설정
        // ─────────────────────────────────────────

        [Header("슬롯 RectTransform (능력 마커 기준점)")]
        [Tooltip("능력1 마커가 이동할 기준점. 슬롯 루트 RectTransform을 연결하세요.")]
        [SerializeField] private RectTransform _slot1Rect;

        [SerializeField] private RectTransform _slot2Rect;

        [SerializeField] private RectTransform _slot3Rect;

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
        }

        /// <summary>
        /// BattleField 하위에 배치된 경우 BattleField가 SetActive(true)될 때
        /// OnEnable()이 호출됩니다.
        /// Awake()가 씬 시작 시 비활성으로 실행되지 않았더라도
        /// 이 시점에 Instance를 재설정합니다.
        /// </summary>
        private void OnEnable()
        {
            if (Instance == null)
                Instance = this;
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
                Debug.LogWarning("[EnemyBattleUIManager] ★ _enemyHudPanel 미연결");
            }

            SetKoIcon(0, false);
            SetKoIcon(1, false);
            SetKoIcon(2, false);

            StartCoroutine(HudRefreshRoutine());
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
        /// 적이 피격당해 HP가 변경됐을 때 Enemy.TakeDamage()에서 호출합니다.
        /// Enemy 자체에 HP UI가 없으므로 이 메서드가 HP 바 갱신을 전담합니다.
        /// </summary>
        public void OnHpChanged(Enemy enemy)
        {
            if (enemy == null) return;

            for (int i = 0; i < _enemies.Length; i++)
            {
                if (_enemies[i] != enemy) continue;
                RefreshSlotHp(i);
                return;
            }
        }

        // ─────────────────────────────────────────
        //  마커 위치 갱신
        // ─────────────────────────────────────────

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

        /// <summary>
        /// 현재 등록된(살아있는) 적 슬롯 수를 반환합니다.
        /// PlayerAbility에서 Q 능력 선택 가능 슬롯 수 확인에 사용합니다.
        /// </summary>
        public int ActiveSlotCount
        {
            get
            {
                int count = 0;
                foreach (var e in _enemies)
                    if (e != null && !e.IsDead) count++;
                return count;
            }
        }

        /// <summary>
        /// 슬롯 인덱스(0~2)에 해당하는 Enemy를 반환합니다.
        /// PlayerAbility Q 능력 확정 시 호출합니다.
        /// </summary>
        public Enemy GetEnemyBySlot(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= _enemies.Length) return null;
            return _enemies[slotIndex];
        }

        /// <summary>
        /// 슬롯 인덱스(0~2)에 해당하는 슬롯 루트 RectTransform을 반환합니다.
        /// AbilityMarkerController가 UI 마커를 이동할 목표 위치로 사용합니다.
        /// </summary>
        public RectTransform GetSlotRect(int slotIndex) => slotIndex switch
        {
            0 => _slot1Rect,
            1 => _slot2Rect,
            2 => _slot3Rect,
            _ => null
        };

        // ─────────────────────────────────────────
        //  HUD 주기 갱신
        // ─────────────────────────────────────────

        private IEnumerator HudRefreshRoutine()
        {
            while (true)
            {
                for (int i = 0; i < _enemies.Length; i++)
                {
                    if (_enemies[i] == null || _enemies[i].IsDead) continue;
                    RefreshSlotHp(i);
                    RefreshSlotSkill(i);
                    RefreshSlotStun(i);
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

        private void RefreshSlotSkill(int i)
        {
            Enemy e = _enemies[i];
            if (e == null) return;
            float ratio = e.MaxSkillGauge > 0 ? e.SkillGauge / e.MaxSkillGauge : 0f;
            SetSkillFill(i, ratio);
        }

        private void RefreshSlotStun(int i)
        {
            Enemy e = _enemies[i];
            if (e == null) return;
            SetStunIcon(i, e.IsStunned);
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

        private void SetSkillFill(int i, float ratio)
        {
            Image fill = i switch { 0 => _enemy1SkillFill, 1 => _enemy2SkillFill, 2 => _enemy3SkillFill, _ => null };
            if (fill != null) fill.fillAmount = ratio;
        }

        private void SetStunIcon(int i, bool active)
        {
            GameObject icon = i switch { 0 => _enemy1StunIcon, 1 => _enemy2StunIcon, 2 => _enemy3StunIcon, _ => null };
            if (icon != null) icon.SetActive(active);
        }

        private void SetKoIcon(int i, bool active)
        {
            GameObject icon = i switch { 0 => _enemy1KoIcon, 1 => _enemy2KoIcon, 2 => _enemy3KoIcon, _ => null };
            if (icon != null) icon.SetActive(active);
        }
    }
}