using System;
using System.Collections.Generic;
using Game.NomadWorkshop.Simulation;
using UnityEngine;

namespace Game.NomadWorkshop.Foundation
{
    public sealed partial class NomadFoundationSystem
    {
        [SerializeField, Range(1, 3), Tooltip("新局居民数量；正式 Foundation 为三人，单人隔离回归显式配置为一人。")]
        private int initialResidentCount = 3;
        private readonly List<FoundationResidentExecution> _residents = new();
        private string _waterCanCarrierId = string.Empty;

        // 共享同步状态机在明确的居民作用域中执行。嵌套查询/世界操作退栈后恢复原执行者，
        // 从不替换 Model 属性或把 View 的只读订阅切到另一人。
        private readonly struct ResidentScope : IDisposable
        {
            private readonly NomadFoundationSystem _system;
            private readonly FoundationResidentExecution _previous;
            internal ResidentScope(NomadFoundationSystem system, FoundationResidentExecution resident)
            { _system = system; _previous = system._resident; system._resident = resident; }
            public void Dispose() => _system._resident = _previous;
        }

        private ResidentScope UseResident(FoundationResidentExecution resident) => new(this, resident);

        private void RecreateResidentExecutions(int count)
        {
            DisposeResidentExecutions();
            _model.EnsureResidentCount(count);
            foreach (var record in _model.Residents)
                _residents.Add(new FoundationResidentExecution(record.State));
            _resident = _residents[0];
        }

        private void DisposeResidentExecutions(bool publishProjection = true)
        {
            foreach (var resident in _residents)
            {
                using var scope = UseResident(resident);
                // 复位仍通知活着的 View；销毁时只释放租约，不能向已失效 Context 的 View 发 R3 通知。
                if (publishProjection) ReleaseActiveTasks();
                resident.Dispose();
            }
            _residents.Clear();
            _resident = null;
        }

        private FoundationResidentExecution FindStopVisitor(FoundationStopVisitKind? kind = null)
        {
            foreach (var resident in _residents)
                if (resident.StopVisit != null && (!kind.HasValue || resident.StopVisit.Kind == kind.Value))
                    return resident;
            return null;
        }

        private FoundationResidentExecution FindResident(string stableId)
        {
            foreach (var resident in _residents)
                if (string.Equals(resident.StableId, stableId, StringComparison.Ordinal)) return resident;
            return null;
        }

        private bool AreAllResidentsAboard
        {
            get
            {
                foreach (var resident in _residents)
                    if (resident.StopVisit != null ||
                        !IsResidentPoseClear(deckLayout.LocalToPose(resident.State.ResidentLocalPosition.Value)))
                        return false;
                return _residents.Count > 0;
            }
        }

        private bool HasLivingResident
        {
            get
            {
                foreach (var resident in _residents)
                    if (resident.Wellbeing is { IsAlive: true }) return true;
                return false;
            }
        }

        private void WakeResidents()
        {
            foreach (var resident in _residents) resident.DecisionRetryRemaining = 0f;
        }

#if UNITY_EDITOR
        /// <summary>隔离测试显式选择人口，再经同一 ResetScenario 创建共享世界与个人执行器。</summary>
        public void ConfigureResidentCountForTests(int count)
        {
            if (count < 1 || count > 3) throw new ArgumentOutOfRangeException(nameof(count));
            initialResidentCount = count;
        }
#endif
    }
}
