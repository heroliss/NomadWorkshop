using Game.NomadWorkshop.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.NomadWorkshop
{
    /// <summary>
    /// StarterJourneySample 的轻量运行入口。它只接住起步配置和最小拾荒 / 座位休息意图，
    /// 正式车辆、资源库存和 Command 接线成立后可替换本表现控制器，不把样板 UI 当作玩法真值。
    /// </summary>
    public sealed class NomadStarterJourneyController : MonoBehaviour
    {
        [Header("玩家化身")]
        [SerializeField] private string playerDisplayName = "旅人";
        [SerializeField] private NomadCharacterGender playerGender = NomadCharacterGender.Unspecified;
        [SerializeField] private int appearanceSeed = 1;

        [Header("起步拾荒")]
        [SerializeField, Min(1)] private int scrapCapacity = 3;
        [SerializeField, Min(0)] private int startingScrapCache = 5;
        [SerializeField] private GameObject[] scrapVisuals = System.Array.Empty<GameObject>();

        public NomadStarterJourneyProfile Profile { get; private set; }
        public NomadStarterScavengeState State { get; private set; }
        public NomadStarterScavengeResult LastScavengeResult { get; private set; }

        private void Awake()
        {
            string displayName = string.IsNullOrWhiteSpace(playerDisplayName)
                ? "旅人"
                : NomadResidentIdentity.ValidateDisplayName(playerDisplayName.Trim(), allowEmpty: false);
            Profile = new NomadStarterJourneyProfile(
                NomadStarterJourneyProfile.DefaultProfileId,
                new NomadResidentIdentity(displayName, playerGender, appearanceSeed, true),
                NomadStarterVehicleProfile.MicroCar);
            State = new NomadStarterScavengeState(scrapCapacity, initialCache: startingScrapCache);
            UpdateScrapVisuals();
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.eKey.wasPressedThisFrame) TryCollectScrap();
                if (keyboard.rKey.wasPressedThisFrame)
                    State.SetSeatRestIntent(!State.SeatRestIntent);
            }
            State.Advance(Time.deltaTime);
            UpdateScrapVisuals();
        }

        public bool TryCollectScrap()
        {
            LastScavengeResult = State.TryCollectScrap();
            return LastScavengeResult.Succeeded;
        }

        public void SetSeatRestIntent(bool active)
        {
            if (State != null) State.SetSeatRestIntent(active);
        }

        private void UpdateScrapVisuals()
        {
            if (scrapVisuals == null || State == null) return;
            for (var i = 0; i < scrapVisuals.Length; i++)
                if (scrapVisuals[i] != null)
                    scrapVisuals[i].SetActive(i < State.CacheRemaining);
        }

        private void OnGUI()
        {
            if (Profile == null || State == null) return;
            GUI.Box(new Rect(18f, 18f, 360f, 178f), "微型车 · 起步拾荒");
            GUI.Label(new Rect(34f, 48f, 330f, 22f),
                $"玩家：{Profile.PlayerIdentity.DisplayName} · 只有驾驶位");
            GUI.Label(new Rect(34f, 72f, 330f, 22f),
                $"车外废料：{State.CacheRemaining} · 车上废料：{State.CarriedScrap}/{State.ScrapCapacity}");
            GUI.Label(new Rect(34f, 96f, 330f, 22f),
                $"疲劳 {State.Fatigue:P0} · 健康 {State.Health:P0} · 心情 {State.Mood:P0}");
            GUI.Label(new Rect(34f, 120f, 330f, 22f),
                State.SeatRestIntent ? "当前意图：在驾驶位休息（恢复慢，会损耗健康与心情）" :
                "当前意图：等待玩家决定下一步");
            GUI.Label(new Rect(34f, 150f, 330f, 36f),
                "E 拾取一份废料　R 开关座位休息\n先活下来，再寻找能扩展空间的车辆。");
        }
    }
}
