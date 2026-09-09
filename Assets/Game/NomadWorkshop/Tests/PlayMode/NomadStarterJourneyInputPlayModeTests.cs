using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace Game.NomadWorkshop.PlayMode.Tests
{
    /// <summary>用合成 Input System 事件验证起步入口，避免只调用公开方法绕过每帧输入错误。</summary>
    public sealed class NomadStarterJourneyInputPlayModeTests
    {
        private GameObject _root;
        private Keyboard _keyboard;
        private Keyboard _previousKeyboard;
        private NomadStarterJourneyController _controller;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _previousKeyboard = Keyboard.current;
            _keyboard = InputSystem.AddDevice<Keyboard>();
            _root = new GameObject("StarterJourneyInputTest");
            _controller = _root.AddComponent<NomadStarterJourneyController>();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_keyboard != null && _keyboard.added) InputSystem.RemoveDevice(_keyboard);
            if (_previousKeyboard != null && _previousKeyboard.added) _previousKeyboard.MakeCurrent();
            if (_root != null) Object.Destroy(_root);
            yield return null;
        }

        [UnityTest]
        public IEnumerator KeyboardEvents_CollectScrapAndToggleSeatRest_WithoutLegacyInputErrors()
        {
            int cacheBefore = _controller.State.CacheRemaining;
            yield return Press(Key.E);
            Assert.That(_controller.State.CarriedScrap, Is.EqualTo(1));
            Assert.That(_controller.State.CacheRemaining, Is.EqualTo(cacheBefore - 1));

            yield return Press(Key.R);
            Assert.That(_controller.State.SeatRestIntent, Is.True);
            yield return Press(Key.R);
            Assert.That(_controller.State.SeatRestIntent, Is.False);
            LogAssert.NoUnexpectedReceived();
        }

        private IEnumerator Press(Key key)
        {
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(key));
            yield return null;
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            yield return null;
        }
    }
}
