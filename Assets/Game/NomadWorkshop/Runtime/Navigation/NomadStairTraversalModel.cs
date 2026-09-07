using Game.Framework.Model;
using R3;
using UnityEngine;

namespace Game.NomadWorkshop.Navigation
{
    public enum StairTraversalPhase { Booting, Ready, Moving, Arrived, Cancelled, Blocked }

    /// <summary>一次原生身体提交后的原子投影；高度始终保留，不由动画提供位置证据。</summary>
    public readonly struct StairTraversalState
    {
        public readonly Vector3 Position;
        public readonly float Yaw;
        public readonly int TargetFloor;
        public readonly StairTraversalPhase Phase;
        public readonly bool Paused;
        public readonly int WaterMilliliters;
        public readonly string Status;

        public StairTraversalState(Vector3 position, float yaw, int floor, StairTraversalPhase phase,
            bool paused, int waterMilliliters, string status)
        {
            Position = position; Yaw = yaw; TargetFloor = floor; Phase = phase;
            Paused = paused; WaterMilliliters = waterMilliliters; Status = status;
        }
    }

    /// <summary>可删除的跨层实验数据面，不写入 Foundation 的正式保存协议。</summary>
    public sealed class NomadStairTraversalModel : MonoModelBase
    {
        public RP<StairTraversalState> State { get; } = new(default);
    }
}
