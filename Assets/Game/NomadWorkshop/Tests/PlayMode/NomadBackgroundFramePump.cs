#if UNITY_EDITOR
using System;
using UnityEditor;

namespace Game.NomadWorkshop.PlayMode.Tests
{
    /// <summary>
    /// 可见动作测试的后台 PlayerLoop 会话。当前 Editor 在 NoThrottling 下仍可能只按约 250 ms
    /// 刷新未聚焦的 Game 视图，跳过短动作的可见阶段。主动请求下一帧，不改模拟时间、倍速或用户偏好；
    /// 测试 TearDown 释放，退出 Play 也会解除订阅，避免把后台持续渲染变成永久设置。
    /// </summary>
    internal sealed class NomadBackgroundFramePump : IDisposable
    {
        private bool _disposed;

        public NomadBackgroundFramePump()
        {
            EditorApplication.update += RequestNextFrame;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private void RequestNextFrame()
        {
            if (EditorApplication.isPlaying && !EditorApplication.isPaused)
                EditorApplication.QueuePlayerLoopUpdate();
        }

        private void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode) Dispose();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            EditorApplication.update -= RequestNextFrame;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        }
    }
}
#endif
