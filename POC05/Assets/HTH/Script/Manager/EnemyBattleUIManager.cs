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
    /// [설계 의도]
    /// PlayerBattleUIManager와 동일한 3슬롯 고정 패널 구조를 사용합니다.
    /// 슬롯 1~3이 씬에 미리 배치되어 있으며,
    /// EnemySpawner가 소환한 적 수에 따라 슬롯을 활성화/비활성화합니다.
    ///
    ///   소환된 적 1마리 → 슬롯1 활성, 슬롯2·3 비활성
    ///   소환된 적 2마리 → 슬롯1·2 활성, 슬롯3 비활성
    ///   소환된 적 3마리 → 슬롯1·2·3 모두 활성
    ///
    /// [슬롯 내용]
    /// - 적 이름 텍스트
    /// - 적 레벨 텍스트 (Enemy.Level 프로퍼티)
    /// - HP 바 (Image Filled)
    /// - HP 수치 텍스트
    /// - KO 아이콘 (사망 시 표시)
    /// ※ EXP 바 없음 (적에게는 EXP 불필요)
    ///
    /// [등록 방식]
    /// EnemySpawner.SpawnEnemy()에서 소환 직후 RegisterEnemy()를 호출합니다.
    /// Enemy.Die()에서 OnEnemyDied()를 호출해 KO 아이콘을 표시합니다.
    /// BattleManager.EndBattle() → UIManager.SetBattleHudActive(false) 시
    /// SlideOut() 완료 후 ClearAllSlots()로 전체 초기화됩니다.
    ///
    /// [히어라키 위치]
    /// --- Managers ---
    ///   └── EnemyBattleUIManager (이 스크립트)
    ///
    /// Canvas
    ///   └── EnemyHUDPanel           ← _enemyHudPanel (슬라이드 루트)
    ///         ├── EnemySlot1        ← _slotRoot1
    ///         │     ├── NameText    ← _enemy1NameText
    ///         │     ├── LevelText   ← _enemy1LevelText
    ///         │     ├── HpFill      ← _enemy1HpFill
    ///         │     ├── HpText      ← _enemy1HpText
    ///         │     └── KoIcon      ← _enemy1KoIcon
    ///         ├── EnemySlot2        ← _slotRoot2
    ///         └── EnemySlot3        ← _slotRoot3
    /// </summary>
    public class EnemyBattleUIManager : MonoBehaviour
    {
        // ─────────────────────────────────────────
        //  싱글턴
        // ─────────────────────────────────────────

        /// <summary>씬 어디서든 EnemyBattleUIManager.Instance로 접근합니다.</summary>
        public static EnemyBattleUIManager Instance { get; private set; }

        // ─────────────────────────────────────────
        //  Inspector — 슬라이드 패널
        // ─────────────────────────────────────────

        [Header("슬라이드 패널")]
        [Tooltip("적 HUD 루트 RectTransform. 이 오브젝트째로 슬라이드합니다.")]
        [SerializeField] private RectTransform _enemyHudPanel;

        [Header("슬라이드 방향")]
        [Tooltip("true = X축 / false = Y축")]
        [SerializeField] private bool _slideOnX = false;

        [Tooltip("true = 양수 방향 진입 (오른쪽/위) / false = 음수 방향 (왼쪽/아래)")]
        [SerializeField] private bool _slideFromPositive = true;

        [Tooltip("슬라이드 이동 거리 (px)")]
        [SerializeField] private float _slideDistance = 500f;

        [Header("슬라이드 애니메이션")]
        [Tooltip("슬라이드 인/아웃 소요 시간 (초)")]
        [SerializeField] private float _slideDuration = 0.5f;

        [Tooltip("슬라이드 인 Ease")]
        [SerializeField] private Ease _slideInEase = Ease.OutBack;

        [Tooltip("슬라이드 아웃 Ease")]
        [SerializeField] private Ease _slideOutEase = Ease.InBack;

        // ─────────────────────────────────────────
        //  Inspector — 슬롯 루트 (3개 고정)
        // ─────────────────────────────────────────

        [Header("적 슬롯 루트 (씬에 미리 배치, 3개 고정)")]
        [Tooltip("슬롯 1 루트 오브젝트. 첫 번째 소환 적에 대응합니다.")]
        [SerializeField] private GameObject _slotRoot1;

        [Tooltip("슬롯 2 루트 오브젝트. 두 번째 소환 적에 대응합니다.")]
        [SerializeField] private GameObject _slotRoot2;

        [Tooltip("슬롯 3 루트 오브젝트. 세 번째 소환 적에 대응합니다.")]
        [SerializeField] private GameObject _slotRoot3;

        // ─────────────────────────────────────────
        //  Inspector — 슬롯 1 UI 요소
        // ─────────────────────────────────────────

        [Header("슬롯 1 — 첫 번째 적")]
        [Tooltip("적 이름 텍스트")]
        [SerializeField] private TMP_Text _enemy1NameText;

        [Tooltip("적 레벨 텍스트 (예: Lv.5)")]
        [SerializeField] private TMP_Text _enemy1LevelText;

        [Tooltip("HP 바 Image (Image Type: Filled)")]
        [SerializeField] private Image _enemy1HpFill;

        [Tooltip("HP 수치 텍스트 (예: 45/100)")]
        [SerializeField] private TMP_Text _enemy1HpText;

        [Tooltip("KO 아이콘 GameObject. 적 사망 시 활성화됩니다.")]
        [SerializeField] private GameObject _enemy1KoIcon;

        // ─────────────────────────────────────────
        //  Inspector — 슬롯 2 UI 요소
        // ─────────────────────────────────────────

        [Header("슬롯 2 — 두 번째 적")]
        [Tooltip("적 이름 텍스트")]
        [SerializeField] private TMP_Text _enemy2NameText;

        [Tooltip("적 레벨 텍스트 (예: Lv.5)")]
        [SerializeField] private TMP_Text _enemy2LevelText;

        [Tooltip("HP 바 Image (Image Type: Filled)")]
        [SerializeField] private Image _enemy2HpFill;

        [Tooltip("HP 수치 텍스트")]
        [SerializeField] private TMP_Text _enemy2HpText;

        [Tooltip("KO 아이콘 GameObject")]
        [SerializeField] private GameObject _enemy2KoIcon;

        // ─────────────────────────────────────────
        //  Inspector — 슬롯 3 UI 요소
        // ─────────────────────────────────────────

        [Header("슬롯 3 — 세 번째 적")]
        [Tooltip("적 이름 텍스트")]
        [SerializeField] private TMP_Text _enemy3NameText;

        [Tooltip("적 레벨 텍스트 (예: Lv.5)")]
        [SerializeField] private TMP_Text _enemy3LevelText;

        [Tooltip("HP 바 Image (Image Type: Filled)")]
        [SerializeField] private Image _enemy3HpFill;

        [Tooltip("HP 수치 텍스트")]
        [SerializeField] private TMP_Text _enemy3HpText;

        [Tooltip("KO 아이콘 GameObject")]
        [SerializeField] private GameObject _enemy3KoIcon;

        // ─────────────────────────────────────────
        //  Inspector — 갱신 설정
        // ─────────────────────────────────────────

        [Header("갱신 설정")]
        [Tooltip("HP 바 갱신 주기 (초)")]
        [SerializeField] private float _hudRefreshRate = 0.1f;

        // ─────────────────────────────────────────
        //  내부 상태 변수
        // ─────────────────────────────────────────

        /// <summary>
        /// 슬롯 0~2에 연결된 Enemy 참조 배열.
        /// null이면 해당 슬롯이 비어있는 상태입니다.
        /// </summary>
        private Enemy[] _enemies = new Enemy[3];

        /// <summary>적 HUD 패널의 화면 내 기본 앵커 위치 (슬라이드 인 목표)</summary>
        private Vector2 _panelOnPos;

        /// <summary>현재 슬라이드 Tween (중복 실행 방지)</summary>
        private Tween _slideTween;

        // ─────────────────────────────────────────
        //  유니티 생명주기
        // ─────────────────────────────────────────

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else { Destroy(gameObject); return; }
        }

        private void Start()
        {
            // 화면 내 기본 위치 저장 후 화면 밖으로 이동
            if (_enemyHudPanel != null)
            {
                _panelOnPos = _enemyHudPanel.anchoredPosition;
                _enemyHudPanel.anchoredPosition =
                    GetOffscreenPos(_panelOnPos, _slideOnX, _slideFromPositive, _slideDistance);
            }

            // 모든 슬롯 초기 비활성화
            SetSlotActive(0, false);
            SetSlotActive(1, false);
            SetSlotActive(2, false);

            // HP 주기 갱신 코루틴 시작
            StartCoroutine(HudRefreshRoutine());
        }

        // ─────────────────────────────────────────
        //  슬라이드 인 / 아웃
        // ─────────────────────────────────────────

        /// <summary>
        /// 배틀 진입 시 적 HUD 패널을 화면 밖→안으로 슬라이드합니다.
        /// UIManager.SetBattleHudActive(true)에서 호출합니다.
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
        /// 배틀 종료 시 적 HUD 패널을 화면 안→밖으로 슬라이드합니다.
        /// UIManager.SetBattleHudActive(false)에서 호출합니다.
        /// 슬라이드 아웃 완료 후 ClearAllSlots()가 자동 호출됩니다.
        /// </summary>
        public void SlideOut()
        {
            if (_enemyHudPanel == null) return;
            Vector2 offPos =
                GetOffscreenPos(_panelOnPos, _slideOnX, _slideFromPositive, _slideDistance);

            _slideTween?.Kill();
            _slideTween = _enemyHudPanel
                .DOAnchorPos(offPos, _slideDuration)
                .SetEase(_slideOutEase)
                .OnComplete(ClearAllSlots);
        }

        /// <summary>Bool 설정 기반으로 화면 밖 오프셋 위치를 계산합니다.</summary>
        /// <param name="onPos">화면 내 기준 anchoredPosition</param>
        /// <param name="slideOnX">true = X축 / false = Y축</param>
        /// <param name="fromPositive">true = 양수 방향 / false = 음수 방향</param>
        /// <param name="distance">이동 거리 (px)</param>
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
        /// 소환된 적을 빈 슬롯에 등록하고 해당 슬롯을 활성화합니다.
        /// EnemySpawner.SpawnEnemy()에서 소환 직후 호출합니다.
        /// 최대 3마리까지 슬롯 0→1→2 순서로 등록됩니다.
        /// </summary>
        /// <param name="enemy">등록할 Enemy 컴포넌트</param>
        public void RegisterEnemy(Enemy enemy)
        {
            if (enemy == null) return;

            for (int i = 0; i < _enemies.Length; i++)
            {
                if (_enemies[i] == null)
                {
                    _enemies[i] = enemy;
                    SetSlotActive(i, true);
                    SetKoIcon(i, false);
                    SetSlotName(i, enemy.name);
                    SetSlotLevel(i, enemy.Level);
                    RefreshSlotHp(i);

                    Debug.Log($"[EnemyBattleUI] 슬롯{i + 1} 등록: {enemy.name} Lv.{enemy.Level}");
                    return;
                }
            }

            Debug.LogWarning("[EnemyBattleUI] 슬롯이 꽉 찼습니다. 최대 3마리까지 등록 가능합니다.");
        }

        /// <summary>
        /// 적 사망 시 해당 슬롯에 KO 아이콘을 표시하고 HP 바를 0으로 설정합니다.
        /// Enemy.Die()에서 직접 호출합니다.
        /// </summary>
        /// <param name="enemy">사망한 Enemy 컴포넌트</param>
        public void OnEnemyDied(Enemy enemy)
        {
            if (enemy == null) return;

            for (int i = 0; i < _enemies.Length; i++)
            {
                if (_enemies[i] == enemy)
                {
                    SetKoIcon(i, true);
                    SetHpFill(i, 0f, 0, _enemies[i].MaxHp);

                    Debug.Log($"[EnemyBattleUI] 슬롯{i + 1} KO: {enemy.name}");
                    return;
                }
            }
        }

        /// <summary>
        /// 배틀 종료 시 모든 슬롯 Enemy 참조를 비우고 슬롯을 비활성화합니다.
        /// SlideOut() DOTween OnComplete 콜백에서 호출됩니다.
        /// </summary>
        private void ClearAllSlots()
        {
            for (int i = 0; i < _enemies.Length; i++)
            {
                _enemies[i] = null;
                SetSlotActive(i, false);
            }

            Debug.Log("[EnemyBattleUI] 전체 슬롯 초기화 완료");
        }

        // ─────────────────────────────────────────
        //  HP 주기 갱신 코루틴
        // ─────────────────────────────────────────

        /// <summary>
        /// _hudRefreshRate 주기마다 등록된 Enemy의 HP 바와 텍스트를 갱신합니다.
        /// </summary>
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

        /// <summary>지정 슬롯의 HP 바와 텍스트를 Enemy 데이터 기준으로 갱신합니다.</summary>
        /// <param name="slotIndex">슬롯 인덱스 (0~2)</param>
        private void RefreshSlotHp(int slotIndex)
        {
            Enemy enemy = _enemies[slotIndex];
            if (enemy == null) return;

            float ratio = enemy.MaxHp > 0
                ? (float)enemy.CurrentHp / enemy.MaxHp
                : 0f;

            SetHpFill(slotIndex, ratio, enemy.CurrentHp, enemy.MaxHp);
        }

        // ─────────────────────────────────────────
        //  슬롯 UI 헬퍼 (슬롯 인덱스 기반 switch 분기)
        // ─────────────────────────────────────────

        /// <summary>슬롯 루트 오브젝트를 활성/비활성화합니다.</summary>
        /// <param name="slotIndex">슬롯 인덱스 (0~2)</param>
        /// <param name="active">활성화 여부</param>
        private void SetSlotActive(int slotIndex, bool active)
        {
            switch (slotIndex)
            {
                case 0: if (_slotRoot1 != null) _slotRoot1.SetActive(active); break;
                case 1: if (_slotRoot2 != null) _slotRoot2.SetActive(active); break;
                case 2: if (_slotRoot3 != null) _slotRoot3.SetActive(active); break;
            }
        }

        /// <summary>슬롯의 이름 텍스트를 설정합니다.</summary>
        /// <param name="slotIndex">슬롯 인덱스 (0~2)</param>
        /// <param name="enemyName">표시할 이름 문자열</param>
        private void SetSlotName(int slotIndex, string enemyName)
        {
            TMP_Text nameText = slotIndex switch
            {
                0 => _enemy1NameText,
                1 => _enemy2NameText,
                2 => _enemy3NameText,
                _ => null
            };

            if (nameText != null) nameText.text = enemyName;
        }

        /// <summary>
        /// 슬롯의 레벨 텍스트를 설정합니다.
        /// RegisterEnemy() 호출 시 Enemy.Level로 초기화됩니다.
        /// </summary>
        /// <param name="slotIndex">슬롯 인덱스 (0~2)</param>
        /// <param name="level">표시할 레벨 수치</param>
        private void SetSlotLevel(int slotIndex, int level)
        {
            TMP_Text levelText = slotIndex switch
            {
                0 => _enemy1LevelText,
                1 => _enemy2LevelText,
                2 => _enemy3LevelText,
                _ => null
            };

            if (levelText != null) levelText.text = $"Lv.{level}";
        }

        /// <summary>슬롯의 HP 바 fillAmount와 수치 텍스트를 설정합니다.</summary>
        /// <param name="slotIndex">슬롯 인덱스 (0~2)</param>
        /// <param name="ratio">HP 비율 (0f ~ 1f)</param>
        /// <param name="current">현재 HP 수치</param>
        /// <param name="max">최대 HP 수치</param>
        private void SetHpFill(int slotIndex, float ratio, int current, int max)
        {
            Image hpFill = slotIndex switch
            {
                0 => _enemy1HpFill,
                1 => _enemy2HpFill,
                2 => _enemy3HpFill,
                _ => null
            };

            TMP_Text hpText = slotIndex switch
            {
                0 => _enemy1HpText,
                1 => _enemy2HpText,
                2 => _enemy3HpText,
                _ => null
            };

            if (hpFill != null)
                hpFill.fillAmount = ratio;

            if (hpText != null)
                hpText.text = max > 0 ? $"{current}/{max}" : "0/0";
        }

        /// <summary>슬롯의 KO 아이콘 활성 상태를 설정합니다.</summary>
        /// <param name="slotIndex">슬롯 인덱스 (0~2)</param>
        /// <param name="active">true = KO 아이콘 표시</param>
        private void SetKoIcon(int slotIndex, bool active)
        {
            GameObject koIcon = slotIndex switch
            {
                0 => _enemy1KoIcon,
                1 => _enemy2KoIcon,
                2 => _enemy3KoIcon,
                _ => null
            };

            if (koIcon != null) koIcon.SetActive(active);
        }
    }
}