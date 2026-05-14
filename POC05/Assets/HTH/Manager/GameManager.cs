using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

namespace SENTRY
{/// <summary>
    /// 게임 전체 흐름을 관리하는 싱글턴 매니저.
    /// 
    /// [설계 의도]
    /// - 씬 전환, 게임오버/클리어 판정, 일시정지 등 전역 상태를 담당합니다.
    /// - 킬 카운트 승리 조건은 제거되었습니다.
    ///   (새 컨셉에서는 BattleManager가 전투 결과를 판단합니다.)
    /// - 다른 매니저들(BattleManager, UIManager 등)이 이 클래스를 참조합니다.
    /// 
    /// [히어라키 위치]
    /// --- Managers ---
    ///   └── GameManager (이 스크립트)
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        // ─────────────────────────────────────────
        //  싱글턴
        // ─────────────────────────────────────────

        /// <summary>
        /// 싱글턴 인스턴스. 씬 어디서든 GameManager.Instance로 접근합니다.
        /// </summary>
        public static GameManager Instance { get; private set; }

        // ─────────────────────────────────────────
        //  Inspector 노출 필드
        // ─────────────────────────────────────────

        [Header("UI 참조")]
        [Tooltip("패배 시 활성화할 패널 (Game Over UI)")]
        [SerializeField] private GameObject _failPanel;

        [Tooltip("클리어 시 활성화할 패널 (선택 사항)")]
        [SerializeField] private GameObject _clearPanel;

        // ─────────────────────────────────────────
        //  상태 프로퍼티
        // ─────────────────────────────────────────

        /// <summary>
        /// 게임이 종료 상태인지 여부.
        /// true일 때 EnemySpawner, BattleManager 등이 동작을 멈춥니다.
        /// </summary>
        public bool IsGameOver { get; private set; } = false;

        // ─────────────────────────────────────────
        //  유니티 생명주기
        // ─────────────────────────────────────────

        private void Awake()
        {
            // 싱글턴 패턴: 이미 인스턴스가 존재하면 중복 오브젝트를 파괴
            if (Instance == null)
                Instance = this;
            else
            {
                Destroy(gameObject);
                return;
            }

            // 씬 재시작 후 Time.timeScale이 0으로 굳어있는 경우를 방지
            Time.timeScale = 1f;
        }

        private void Start()
        {
            // 시작 시 결과 패널 비활성화
            if (_failPanel != null) _failPanel.SetActive(false);
            if (_clearPanel != null) _clearPanel.SetActive(false);
        }

        // ─────────────────────────────────────────
        //  게임 종료 처리
        // ─────────────────────────────────────────

        /// <summary>
        /// 게임 종료를 처리합니다. BattleManager, PlayerHealth 등에서 호출합니다.
        /// </summary>
        /// <param name="isClear">true = 클리어, false = 패배</param>
        public void GameOver(bool isClear)
        {
            // 이미 종료된 상태라면 중복 호출 무시
            if (IsGameOver) return;

            IsGameOver = true;

            // 게임 일시 정지 (모든 Update, 물리 연산 멈춤)
            Time.timeScale = 0f;

            if (isClear)
            {
                Debug.Log("<color=yellow>[게임 클리어]</color>");
                if (_clearPanel != null) _clearPanel.SetActive(true);
            }
            else
            {
                Debug.Log("<color=red>[게임 오버]</color>");
                if (_failPanel != null) _failPanel.SetActive(true);
            }
        }

        // ─────────────────────────────────────────
        //  버튼 연결용 퍼블릭 메서드
        // ─────────────────────────────────────────

        /// <summary>
        /// 현재 씬을 처음부터 다시 시작합니다. UI 버튼에 연결하세요.
        /// </summary>
        public void RestartGame()
        {
            Time.timeScale = 1f;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        /// <summary>
        /// 애플리케이션을 종료합니다. UI 버튼에 연결하세요.
        /// </summary>
        public void Quit()
        {
            Application.Quit();
        }
    }
}
