using System;

namespace Game.NomadWorkshop.Foundation
{
    /// <summary>
    /// 将已提交旅途速度转换成一个有界的镜头前后偏移。它只保存表现缓冲，不读取模拟状态，
    /// 可在读档 / 空间重建时 Reset；使用指数响应保证同一目标在不同帧率下不会突然跳变。
    /// </summary>
    public sealed class FoundationJourneyCameraBuffer
    {
        private readonly double _response;
        private bool _initialized;

        public FoundationJourneyCameraBuffer(double response = 8d)
        {
            if (!IsFinite(response) || response <= 0d)
                throw new ArgumentOutOfRangeException(nameof(response));
            _response = response;
        }

        public double CurrentOffsetMeters { get; private set; }

        public void Reset(double offsetMeters = 0d)
        {
            ValidateFinite(offsetMeters, nameof(offsetMeters));
            CurrentOffsetMeters = offsetMeters;
            _initialized = true;
        }

        public double Update(double targetOffsetMeters, double deltaSeconds)
        {
            ValidateFinite(targetOffsetMeters, nameof(targetOffsetMeters));
            if (!IsFinite(deltaSeconds) || deltaSeconds < 0d)
                throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
            if (!_initialized)
            {
                Reset(targetOffsetMeters);
                return CurrentOffsetMeters;
            }
            if (deltaSeconds == 0d) return CurrentOffsetMeters;

            double alpha = 1d - Math.Exp(-_response * deltaSeconds);
            CurrentOffsetMeters += (targetOffsetMeters - CurrentOffsetMeters) * alpha;
            return CurrentOffsetMeters;
        }

        private static void ValidateFinite(double value, string parameterName)
        {
            if (!IsFinite(value)) throw new ArgumentOutOfRangeException(parameterName);
        }

        private static bool IsFinite(double value) =>
            !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
