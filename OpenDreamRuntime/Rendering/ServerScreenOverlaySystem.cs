﻿using OpenDreamRuntime.Objects;
using OpenDreamShared.Rendering;
using Robust.Server.GameStates;
using Robust.Server.Player;

namespace OpenDreamRuntime.Rendering {
    public sealed class ServerScreenOverlaySystem : SharedScreenOverlaySystem {
        [Dependency] private readonly IAtomManager _atomManager = default!;

        private readonly Dictionary<IPlayerSession, HashSet<EntityUid>> _sessionToScreenObjects = new();

        public override void Initialize() {
            SubscribeLocalEvent<ExpandPvsEvent>(HandleExpandPvsEvent);
        }

        public void AddScreenObject(DreamConnection connection, DreamObject screenObject) {
            EntityUid entityId = _atomManager.GetMovableEntity(screenObject);

            if (!_sessionToScreenObjects.TryGetValue(connection.Session, out HashSet<EntityUid> objects)) {
                objects = new HashSet<EntityUid>();
                _sessionToScreenObjects.Add(connection.Session, objects);
            }

            objects.Add(entityId);
            RaiseNetworkEvent(new AddScreenObjectEvent(entityId), connection.Session.ConnectedClient);
        }

        public void RemoveScreenObject(DreamConnection connection, DreamObject screenObject) {
            EntityUid entityId = _atomManager.GetMovableEntity(screenObject);

            _sessionToScreenObjects[connection.Session].Remove(entityId);
            RaiseNetworkEvent(new RemoveScreenObjectEvent(entityId), connection.Session.ConnectedClient);
        }

        private void HandleExpandPvsEvent(ref ExpandPvsEvent e) {
            if (_sessionToScreenObjects.TryGetValue(e.Session, out HashSet<EntityUid> objects)) {
                e.Entities.AddRange(objects);
            }
        }
    }
}
