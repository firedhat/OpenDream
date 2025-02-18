﻿using OpenDreamRuntime.Input;
using OpenDreamRuntime.Objects.MetaObjects;
using OpenDreamRuntime.Procs.DebugAdapter;
using OpenDreamShared;
using Robust.Server.ServerStatus;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.ContentPack;
using Robust.Shared.Timing;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
namespace OpenDreamRuntime {
    public sealed class EntryPoint : GameServer {
        [Dependency] private readonly IEntitySystemManager _entitySystemManager = default!;
        [Dependency] private readonly IDreamManager _dreamManager = default!;
        [Dependency] private readonly IConfigurationManager _configManager = default!;
        [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
        [Dependency] private readonly IDreamDebugManager _debugManager = default!;

        private DreamCommandSystem? _commandSystem;

        public override void Init() {
            IoCManager.Resolve<IStatusHost>().SetMagicAczProvider(new DefaultMagicAczProvider(
                new DefaultMagicAczInfo("Content.Client", new[] {"OpenDreamClient", "OpenDreamShared"}),
                IoCManager.Resolve<IDependencyCollection>()));

            IComponentFactory componentFactory = IoCManager.Resolve<IComponentFactory>();
            componentFactory.DoAutoRegistrations();

            ServerContentIoC.Register();

            // This needs to happen after all IoC registrations, but before IoC.BuildGraph();
            foreach (var callback in TestingCallbacks) {
                var cast = (ServerModuleTestingCallbacks) callback;
                cast.ServerBeforeIoC?.Invoke();
            }

            IoCManager.BuildGraph();
            IoCManager.InjectDependencies(this);
            componentFactory.GenerateNetIds();

            _configManager.OverrideDefault(CVars.NetLogLateMsg, false); // Disable since disabling prediction causes timing errors otherwise.
            _configManager.OverrideDefault(CVars.GameAutoPauseEmpty, false); // TODO: world.sleep_offline can control this
            _configManager.SetCVar(CVars.GridSplitting, false); // Grid splitting should never be used

            _prototypeManager.LoadDirectory(new ResourcePath("/Resources/Prototypes"));
        }

        public override void PostInit() {
            _commandSystem = _entitySystemManager.GetEntitySystem<DreamCommandSystem>();

            int debugAdapterPort = _configManager.GetCVar(OpenDreamCVars.DebugAdapterLaunched);
            if (debugAdapterPort == 0) {
                _dreamManager.PreInitialize(_configManager.GetCVar<string>(OpenDreamCVars.JsonPath));
                _dreamManager.StartWorld();
            } else {
                // The debug manager is responsible for running _dreamManager.PreInitialize() and .StartWorld()
                _debugManager.Initialize(debugAdapterPort);
            }
        }

        protected override void Dispose(bool disposing) {
            // Write every savefile to disk
            foreach (var savefile in DreamMetaObjectSavefile.ObjectToSavefile.Values) {
                savefile.Flush();
            }

            _dreamManager.Shutdown();
            _debugManager.Shutdown();
        }

        public override void Update(ModUpdateLevel level, FrameEventArgs frameEventArgs) {
            if (level == ModUpdateLevel.PostEngine) {
                _commandSystem!.RunRepeatingCommands();
                _dreamManager.Update();
                _debugManager.Update();
            }
        }
    }
}
