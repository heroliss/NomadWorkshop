using Game.Framework.Command;
using R3;

namespace Game.NomadWorkshop.Navigation
{
    public readonly struct GetStairTraversalStateCommand : ICommand<ReadOnlyReactiveProperty<StairTraversalState>>
    {
        public ReadOnlyReactiveProperty<StairTraversalState> Execute(ICommandContext ctx) =>
            ctx.GetModel<NomadStairTraversalModel>().State;
    }

    public readonly struct MoveStairTraversalCommand : ICommand<bool>
    {
        private readonly int _floor;
        public MoveStairTraversalCommand(int floor) => _floor = floor;
        public bool Execute(ICommandContext ctx) => ctx.GetSystem<NomadStairTraversalSystem>().MoveToFloor(_floor);
    }

    public readonly struct PauseStairTraversalCommand : ICommand
    {
        private readonly bool _paused;
        public PauseStairTraversalCommand(bool paused) => _paused = paused;
        public void Execute(ICommandContext ctx) => ctx.GetSystem<NomadStairTraversalSystem>().SetPaused(_paused);
    }

    public readonly struct CancelStairTraversalCommand : ICommand
    {
        public void Execute(ICommandContext ctx) => ctx.GetSystem<NomadStairTraversalSystem>().Cancel();
    }

    public readonly struct CaptureStairTraversalCommand : ICommand<string>
    {
        public string Execute(ICommandContext ctx) => ctx.GetSystem<NomadStairTraversalSystem>().CaptureCheckpoint();
    }

    public readonly struct RestoreStairTraversalCommand : ICommand<bool>
    {
        private readonly string _json;
        public RestoreStairTraversalCommand(string json) => _json = json;
        public bool Execute(ICommandContext ctx) => ctx.GetSystem<NomadStairTraversalSystem>().RestoreCheckpoint(_json);
    }
}
