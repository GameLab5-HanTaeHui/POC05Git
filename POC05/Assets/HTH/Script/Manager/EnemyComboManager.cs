using UnityEngine;
using System.Collections.Generic;

namespace SENTRY
{
    /// <summary>
    /// 배틀 필드의 적 콤보 순번 AI를 제어하는 싱글턴 컴포넌트.
    ///
    /// [설계 의도]
    /// BattleEncounterDataSO의 comboCount에 따라 소환된 적들이
    /// 순번을 돌아가며 공격하도록 조율합니다.
    ///
    ///   comboCount = 1 → 단독 공격 (기존 Enemy AI 그대로)
    ///   comboCount = 2 → EnemyA → EnemyB → EnemyA → ... 순환
    ///   comboCount = 3 → EnemyA → EnemyB → EnemyC → EnemyA → ... 순환
    ///
    /// [작동 원리]
    /// - 현재 순번(_currentAttackerIndex)의 Enemy만 IsMyTurn = true 상태가 됩니다.
    /// - Enemy.TryAttack()은 IsMyTurn이 false이면 공격을 건너뜁니다.
    /// - 현재 순번의 Enemy가 공격을 완료하면 AdvanceTurn()을 호출해 순번을 넘깁니다.
    /// - Enemy 사망 시 UnregisterEnemy()로 목록에서 제거 후 자동으로 다음 순번으로 이동합니다.
    ///
    /// [EnemyBattleUIManager 연동]
    /// - 등록: RegisterEnemy(enemy) — 인수 1개
    /// - 사망: OnEnemyDied(enemy)  — UnregisterEnemy 대신 사용
    /// - 순번 아이콘 갱신: EnemyBattleUIManager는 3슬롯 고정이므로 별도 갱신 불필요
    ///
    /// [히어라키 위치]
    /// BattleField
    ///   └── EnemyComboManager (이 스크립트)
    /// </summary>
    public class EnemyComboManager : MonoBehaviour
    {
        // ─────────────────────────────────────────
        //  싱글턴
        // ─────────────────────────────────────────

        /// <summary>씬 어디서든 EnemyComboManager.Instance로 접근합니다.</summary>
        public static EnemyComboManager Instance { get; private set; }

        // ─────────────────────────────────────────
        //  내부 상태 변수
        // ─────────────────────────────────────────

        /// <summary>현재 콤보 그룹에 참여 중인 Enemy 목록</summary>
        private List<Enemy> _members = new List<Enemy>();

        /// <summary>현재 공격 차례 인덱스</summary>
        private int _currentAttackerIndex = 0;

        /// <summary>comboCount = 1이면 콤보 순번 없이 모두 자유 공격</summary>
        private bool _isSingleMode = false;

        // ─────────────────────────────────────────
        //  외부 공개 프로퍼티
        // ─────────────────────────────────────────

        /// <summary>현재 등록된 Enemy 수</summary>
        public int MemberCount => _members.Count;

        // ─────────────────────────────────────────
        //  유니티 생명주기
        // ─────────────────────────────────────────

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);
        }

        // ─────────────────────────────────────────
        //  그룹 초기화
        // ─────────────────────────────────────────

        /// <summary>
        /// 콤보 그룹을 초기화합니다.
        /// EnemySpawner가 인카운터 시작 시 호출합니다.
        /// </summary>
        /// <param name="comboCount">인카운터의 콤보 수 (1=단독, 2=2순환, 3=3순환)</param>
        public void Initialize(int comboCount)
        {
            _members.Clear();
            _currentAttackerIndex = 0;
            _isSingleMode = (comboCount <= 1);

            Debug.Log($"[EnemyComboManager] 초기화 — comboCount: {comboCount}" +
                      $" / 단독 모드: {_isSingleMode}");
        }

        // ─────────────────────────────────────────
        //  Enemy 등록 / 해제
        // ─────────────────────────────────────────

        /// <summary>
        /// Enemy를 콤보 그룹에 등록합니다.
        /// EnemySpawner.SpawnEnemy()에서 소환 직후 호출합니다.
        /// </summary>
        /// <param name="enemy">등록할 Enemy 컴포넌트</param>
        public void RegisterEnemy(Enemy enemy)
        {
            if (enemy == null || _members.Contains(enemy)) return;
            _members.Add(enemy);

            // 단독 모드가 아니면 첫 번째 적에게만 공격 순번 부여
            if (!_isSingleMode)
                enemy.SetComboTurn(_members.Count == 1);

            // EnemyBattleUIManager 슬롯 등록 (인수 1개)
            EnemyBattleUIManager.Instance?.RegisterEnemy(enemy);

            Debug.Log($"[EnemyComboManager] 등록: {enemy.name} " +
                      $"(총 {_members.Count}명)");
        }

        /// <summary>
        /// Enemy 사망 시 콤보 그룹에서 제거하고 다음 순번으로 넘깁니다.
        /// Enemy.Die()에서 호출합니다.
        /// </summary>
        /// <param name="enemy">사망한 Enemy 컴포넌트</param>
        public void OnEnemyDied(Enemy enemy)
        {
            if (enemy == null) return;

            int removedIndex = _members.IndexOf(enemy);
            _members.Remove(enemy);

            // EnemyBattleUIManager KO 아이콘 표시
            EnemyBattleUIManager.Instance?.OnEnemyDied(enemy);

            if (_isSingleMode || _members.Count == 0) return;

            // 제거된 인덱스가 현재 공격자 이하이면 인덱스 보정
            if (removedIndex <= _currentAttackerIndex)
                _currentAttackerIndex = Mathf.Max(0, _currentAttackerIndex - 1);

            // 다음 순번으로 이동
            AdvanceTurn();

            Debug.Log($"[EnemyComboManager] 사망 처리: {enemy.name} " +
                      $"(남은 {_members.Count}명)");
        }

        // ─────────────────────────────────────────
        //  순번 제어
        // ─────────────────────────────────────────

        /// <summary>
        /// 현재 순번의 Enemy가 공격을 완료했을 때 호출합니다.
        /// 다음 순번으로 넘기고 각 Enemy의 SetComboTurn()을 갱신합니다.
        /// Enemy.TryAttack() 공격 완료 시점에 호출합니다.
        /// </summary>
        public void AdvanceTurn()
        {
            if (_isSingleMode || _members.Count == 0) return;

            // 인덱스 순환
            _currentAttackerIndex = (_currentAttackerIndex + 1) % _members.Count;

            // 전체 Enemy에게 현재 순번 권한 배포
            for (int i = 0; i < _members.Count; i++)
            {
                bool isMyTurn = (i == _currentAttackerIndex);
                _members[i].SetComboTurn(isMyTurn);
            }

            Debug.Log($"[EnemyComboManager] 순번 → {_currentAttackerIndex}번 " +
                      $"({_members[_currentAttackerIndex].name})");
        }

        /// <summary>
        /// 단독 모드 여부를 반환합니다.
        /// Enemy.TryAttack()에서 순번 제한 없이 공격할지 판단합니다.
        /// </summary>
        public bool IsSingleMode() => _isSingleMode;
    }
}