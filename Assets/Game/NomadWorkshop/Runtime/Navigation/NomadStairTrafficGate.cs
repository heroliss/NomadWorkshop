using Game.NomadWorkshop.Simulation;
using UnityEngine;

namespace Game.NomadWorkshop.Navigation
{
    /// <summary>
    /// 狭窄楼梯的单人占用闸门。它只保护实验中的单一通道租约，
    /// 不替代正式居民路网；未来正式多层交通可以复用同一 ReservationLedger 契约。
    /// </summary>
    public sealed class NomadStairTrafficGate : MonoBehaviour
    {
        public const string ReservationKey = "stair:single-lane";
        private readonly ReservationLedger _reservations = new();
        private ReservationLease _lease;

        public bool IsOccupied => _lease != null && !_lease.IsReleased;
        public ulong CurrentOwner => IsOccupied ? _lease.OwnerId : 0UL;

        public bool TryAcquire(ulong ownerId)
        {
            if (ownerId == 0UL) return false;
            if (IsOccupied) return CurrentOwner == ownerId;
            if (!_reservations.TryAcquire(ownerId, new[] { ReservationKey }, out ReservationLease lease))
                return false;
            _lease = lease;
            return true;
        }

        /// <summary>只有当前持有人能释放；暂停、取消和原持有人重发目的地不交出通道。</summary>
        public bool Release(ulong ownerId)
        {
            if (!IsOccupied || ownerId != CurrentOwner) return false;
            _lease?.Dispose();
            _lease = null;
            return true;
        }

        private void OnDestroy() => _lease?.Dispose();
    }
}
