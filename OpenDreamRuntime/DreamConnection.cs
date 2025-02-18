using System.Threading.Tasks;
using System.Web;
using OpenDreamRuntime.Objects;
using OpenDreamRuntime.Objects.MetaObjects;
using OpenDreamRuntime.Procs;
using OpenDreamRuntime.Procs.Native;
using OpenDreamRuntime.Resources;
using OpenDreamShared.Dream.Procs;
using OpenDreamShared.Network.Messages;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Utility;

namespace OpenDreamRuntime {
    public sealed class DreamConnection {
        [Dependency] private readonly IDreamManager _dreamManager = default!;
        [Dependency] private readonly IDreamObjectTree _objectTree = default!;
        [Dependency] private readonly IAtomManager _atomManager = default!;
        [Dependency] private readonly DreamResourceManager _resourceManager = default!;

        [ViewVariables] private readonly Dictionary<string, (DreamObject Src, DreamProc Verb)> _availableVerbs = new();
        [ViewVariables] private readonly Dictionary<string, List<string>> _statPanels = new();
        [ViewVariables] private bool _currentlyUpdatingStat;

        [ViewVariables] public IPlayerSession? Session { get; private set; }
        [ViewVariables] public DreamObject? Client { get; private set; }
        [ViewVariables] public DreamObject? Mob {
            get => _mob;
            set {
                DebugTools.Assert(value == null || value.IsSubtypeOf(_objectTree.Mob));

                if (_mob != value) {
                    _mob?.SetVariableValue("ckey", DreamValue.Null);
                    _mob?.SetVariableValue("key", DreamValue.Null);
                    _mob?.SpawnProc("Logout");

                    if (value != null) {
                        // If the mob is already owned by another player, kick them out
                        if (_dreamManager.TryGetConnectionFromMob(value, out var existingMobOwner))
                            existingMobOwner.Mob = null;

                        _mob = value;
                        _mob.SetVariableValue("ckey", new(Session!.Name));
                        _mob.SetVariableValue("key", new(Session!.Name));
                        _mob.SpawnProc("Login", usr: _mob);
                    } else {
                        _mob = null;
                    }

                    UpdateAvailableVerbs();
                }

                if (_mob != null) {
                    Session!.AttachToEntity(_atomManager.GetMovableEntity(_mob));
                } else {
                    Session!.DetachFromEntity();
                }
            }
        }

        [ViewVariables] private string? _outputStatPanel;
        [ViewVariables] private string _selectedStatPanel;
        [ViewVariables] private readonly Dictionary<int, Action<DreamValue>> _promptEvents = new();
        [ViewVariables] private int _nextPromptEvent = 1;

        private DreamObject? _mob;

        public string SelectedStatPanel {
            get => _selectedStatPanel;
            set {
                _selectedStatPanel = value;

                var msg = new MsgSelectStatPanel() { StatPanel = value };
                Session?.ConnectedClient.SendMessage(msg);
            }
        }

        public DreamConnection() {
            IoCManager.InjectDependencies(this);
        }

        public void HandleConnection(IPlayerSession session) {
            var client = _objectTree.CreateObject(_objectTree.Client);

            Session = session;

            Client = client;
            Client.InitSpawn(new());
        }

        public void HandleDisconnection() {
            if (Session == null || Client == null) // Already disconnected?
                return;

            _mob?.SpawnProc("Logout"); // Don't null out the ckey here
            _mob = null;

            Session = null;

            Client.Delete(_dreamManager);
            Client = null;
        }

        public void UpdateAvailableVerbs() {
            _availableVerbs.Clear();
            var verbs = new List<(string, string, string)>();

            void AddVerbs(DreamObject src, IEnumerable<DreamValue> adding) {
                foreach (DreamValue mobVerb in adding) {
                    if (!mobVerb.TryGetValueAsProc(out var proc))
                        continue;

                    string verbName = proc.VerbName ?? proc.Name;
                    string verbId = verbName.ToLowerInvariant().Replace(" ", "-"); // Case-insensitive, dashes instead of spaces
                    if (_availableVerbs.ContainsKey(verbId)) {
                        // BYOND will actually show the user two verbs with different capitalization/dashes, but they will both execute the same verb.
                        // We make a warning and ignore the latter ones instead.
                        Logger.Warning($"User \"{Session.Name}\" has multiple verb commands named \"{verbId}\", ignoring all but the first");
                        continue;
                    }

                    _availableVerbs.Add(verbId, (src, proc));

                    // Don't send hidden verbs. Names starting with "." count as hidden.
                    if ((proc.Attributes & ProcAttributes.Hidden) == ProcAttributes.Hidden ||
                        verbName.StartsWith('.')) {
                        continue;
                    }

                    string? category = proc.VerbCategory;
                    // Explicitly null category is hidden from verb panels, "" category becomes the default_verb_category
                    if (category == string.Empty) {
                        // But if default_verb_category is null, we hide it from the verb panel
                        Client.GetVariable("default_verb_category").TryGetValueAsString(out category);
                    }

                    // Null category is serialized as an empty string and treated as hidden
                    verbs.Add((verbName, verbId, category ?? String.Empty));
                }
            }

            AddVerbs(Client, DreamMetaObjectClient.VerbLists[Client].GetValues());

            if (Mob != null) {
                AddVerbs(Mob, DreamMetaObjectAtom.VerbLists[Mob].GetValues());
            }

            var msg = new MsgUpdateAvailableVerbs() {
                AvailableVerbs = verbs.ToArray()
            };

            Session?.ConnectedClient.SendMessage(msg);
        }

        public void UpdateStat() {
            if (Session == null || Client == null || _currentlyUpdatingStat)
                return;

            _currentlyUpdatingStat = true;
            _statPanels.Clear();

            DreamThread.Run("Stat", async (state) => {
                try {
                    var statProc = Client.GetProc("Stat");

                    await state.Call(statProc, Client, Mob);
                    if (Session.Status == SessionStatus.InGame) {
                        var msg = new MsgUpdateStatPanels(_statPanels);
                        Session.ConnectedClient.SendMessage(msg);
                    }

                    return DreamValue.Null;
                } finally {
                    _currentlyUpdatingStat = false;
                }
            });
        }

        public void SetOutputStatPanel(string name) {
            if (!_statPanels.ContainsKey(name)) _statPanels.Add(name, new List<string>());

            _outputStatPanel = name;
        }

        public void AddStatPanelLine(string text) {
            if (_outputStatPanel == null || !_statPanels.ContainsKey(_outputStatPanel))
                SetOutputStatPanel("Stats");

            _statPanels[_outputStatPanel].Add(text);
        }

        public void HandleMsgSelectStatPanel(MsgSelectStatPanel message) {
            _selectedStatPanel = message.StatPanel;
        }

        public void HandleMsgPromptResponse(MsgPromptResponse message) {
            if (!_promptEvents.TryGetValue(message.PromptId, out var promptEvent)) {
                Logger.Warning($"{message.MsgChannel}: Received MsgPromptResponse for prompt {message.PromptId} which does not exist.");
                return;
            }

            DreamValue value = message.Type switch {
                DMValueType.Null => DreamValue.Null,
                DMValueType.Text or DMValueType.Message => new DreamValue((string)message.Value),
                DMValueType.Num => new DreamValue((float)message.Value),
                _ => throw new Exception("Invalid prompt response '" + message.Type + "'")
            };

            promptEvent.Invoke(value);
            _promptEvents.Remove(message.PromptId);
        }

        public void HandleMsgTopic(MsgTopic pTopic) {
            DreamList hrefList = DreamProcNativeRoot.params2list(_objectTree, HttpUtility.UrlDecode(pTopic.Query));
            DreamValue srcRefValue = hrefList.GetValue(new DreamValue("src"));
            DreamValue src = DreamValue.Null;

            if (srcRefValue.TryGetValueAsString(out var srcRef)) {
                src = _dreamManager.LocateRef(srcRef);
            }

            Client?.SpawnProc("Topic", usr: Mob, new(pTopic.Query), new(hrefList), src);
        }


        public void OutputDreamValue(DreamValue value) {
            if (value.TryGetValueAsDreamObject(out var outputObject)) {
                if (outputObject?.IsSubtypeOf(_objectTree.Sound) == true) {
                    UInt16 channel = (UInt16)outputObject.GetVariable("channel").GetValueAsInteger();
                    UInt16 volume = (UInt16)outputObject.GetVariable("volume").GetValueAsInteger();
                    DreamValue file = outputObject.GetVariable("file");

                    var msg = new MsgSound() {
                        Channel = channel,
                        Volume = volume
                    };

                    if (!file.TryGetValueAsDreamResource(out var soundResource)) {
                        if (file.TryGetValueAsString(out var soundPath)) {
                            soundResource = _resourceManager.LoadResource(soundPath);
                        } else if (file != DreamValue.Null) {
                            throw new ArgumentException($"Cannot output {value}", nameof(value));
                        }
                    }

                    msg.ResourceId = soundResource?.Id;
                    if (soundResource?.ResourcePath is { } resourcePath) {
                        if (resourcePath.EndsWith(".ogg"))
                            msg.Format = MsgSound.FormatType.Ogg;
                        else if (resourcePath.EndsWith(".wav"))
                            msg.Format = MsgSound.FormatType.Wav;
                        else
                            throw new Exception($"Sound {value} is not a supported file type");
                    }

                    Session?.ConnectedClient.SendMessage(msg);
                    return;
                }
            }

            OutputControl(value.Stringify(), null);
        }

        public void OutputControl(string message, string? control) {
            var msg = new MsgOutput() {
                Value = message,
                Control = control
            };

            Session?.ConnectedClient.SendMessage(msg);
        }

        public void HandleCommand(string fullCommand) {
            // TODO: Arguments are a little more complicated than "split by spaces"
            // e.g. strings can be passed
            string[] args = fullCommand.Split(' ');
            string command = args[0].ToLowerInvariant().Replace(" ", "-"); // Case-insensitive, dashes instead of spaces

            switch (command) {
                //TODO: Maybe move these verbs to DM code?
                case ".north": Client?.SpawnProc("North"); break;
                case ".east": Client?.SpawnProc("East"); break;
                case ".south": Client?.SpawnProc("South"); break;
                case ".west": Client?.SpawnProc("West"); break;
                case ".northeast": Client?.SpawnProc("Northeast"); break;
                case ".southeast": Client?.SpawnProc("Southeast"); break;
                case ".southwest": Client?.SpawnProc("Southwest"); break;
                case ".northwest": Client?.SpawnProc("Northwest"); break;
                case ".center": Client?.SpawnProc("Center"); break;

                default: {
                    if (_availableVerbs.TryGetValue(command, out var value)) {
                        (DreamObject verbSrc, DreamProc verb) = value;

                        DreamThread.Run(fullCommand, async (state) => {
                            DreamValue[] arguments;
                            if (verb.ArgumentNames != null) {
                                arguments = new DreamValue[verb.ArgumentNames.Count];

                                // TODO: this should probably be done on the client, shouldn't it?
                                if (args.Length == 1) { // No args given; prompt the client for them
                                    for (int i = 0; i < verb.ArgumentNames.Count; i++) {
                                        String argumentName = verb.ArgumentNames[i];
                                        DMValueType argumentType = verb.ArgumentTypes[i];
                                        DreamValue argumentValue = await Prompt(argumentType, title: String.Empty, // No settable title for verbs
                                            argumentName, defaultValue: String.Empty); // No default value for verbs

                                        arguments[i] = argumentValue;
                                    }
                                } else { // Attempt to parse the given arguments
                                    for (int i = 0; i < verb.ArgumentNames.Count; i++) {
                                        DMValueType argumentType = verb.ArgumentTypes[i];

                                        if (argumentType == DMValueType.Text) {
                                            arguments[i] = new(args[i+1]);
                                        } else {
                                            Logger.Error($"Parsing verb args of type {argumentType} is unimplemented; ignoring command ({fullCommand})");
                                            return DreamValue.Null;
                                        }
                                    }
                                }
                            } else {
                                arguments = Array.Empty<DreamValue>();
                            }

                            await state.Call(verb, verbSrc, Mob, arguments);
                            return DreamValue.Null;
                        });
                    }

                    break;
                }
            }
        }

        public Task<DreamValue> Prompt(DMValueType types, String title, String message, String defaultValue) {
            var task = MakePromptTask(out var promptId);
            var msg = new MsgPrompt() {
                PromptId = promptId,
                Title = title,
                Message = message,
                Types = types,
                DefaultValue = defaultValue
            };

            Session.ConnectedClient.SendMessage(msg);
            return task;
        }

        public async Task<DreamValue> PromptList(DMValueType types, DreamList list, String title, String message, DreamValue defaultValue) {
            List<DreamValue> listValues = list.GetValues();

            List<string> promptValues = new(listValues.Count);
            for (int i = 0; i < listValues.Count; i++) {
                DreamValue value = listValues[i];

                if (types.HasFlag(DMValueType.Obj) && !value.TryGetValueAsDreamObjectOfType(_objectTree.Movable, out _))
                    continue;
                if (types.HasFlag(DMValueType.Mob) && !value.TryGetValueAsDreamObjectOfType(_objectTree.Mob, out _))
                    continue;
                if (types.HasFlag(DMValueType.Turf) && !value.TryGetValueAsDreamObjectOfType(_objectTree.Turf, out _))
                    continue;
                if (types.HasFlag(DMValueType.Area) && !value.TryGetValueAsDreamObjectOfType(_objectTree.Area, out _))
                    continue;

                promptValues.Add(value.Stringify());
            }

            if (promptValues.Count == 0)
                return DreamValue.Null;

            var task = MakePromptTask(out var promptId);
            var msg = new MsgPromptList() {
                PromptId = promptId,
                Title = title,
                Message = message,
                CanCancel = (types & DMValueType.Null) == DMValueType.Null,
                DefaultValue = defaultValue.Stringify(),
                Values = promptValues.ToArray()
            };

            Session.ConnectedClient.SendMessage(msg);

            // The client returns the index of the selected item, this needs turned back into the DreamValue.
            var selectedIndex = await task;
            if (selectedIndex.TryGetValueAsInteger(out int index) && index < listValues.Count) {
                return listValues[index];
            }

            // Client returned an invalid value.
            // Return the first value in the list, or null if cancellable
            return msg.CanCancel ? DreamValue.Null : listValues[0];
        }

        public Task<DreamValue> WinExists(string controlId) {
            var task = MakePromptTask(out var promptId);
            var msg = new MsgWinExists() {
                PromptId = promptId,
                ControlId = controlId
            };

            Session.ConnectedClient.SendMessage(msg);

            return task;
        }

        public Task<DreamValue> Alert(String title, String message, String button1, String button2, String button3) {
            var task = MakePromptTask(out var promptId);
            var msg = new MsgAlert() {
                PromptId = promptId,
                Title = title,
                Message = message,
                Button1 = button1,
                Button2 = button2,
                Button3 = button3
            };

            Session.ConnectedClient.SendMessage(msg);
            return task;
        }

        private Task<DreamValue> MakePromptTask(out int promptId) {
            TaskCompletionSource<DreamValue> tcs = new();
            promptId = _nextPromptEvent++;

            _promptEvents.Add(promptId, response => {
                tcs.TrySetResult(response);
            });

            return tcs.Task;
        }

        public void BrowseResource(DreamResource resource, string filename) {
            if (resource.ResourceData == null)
                return;

            var msg = new MsgBrowseResource() {
                Filename = filename,
                Data = resource.ResourceData
            };

            Session?.ConnectedClient.SendMessage(msg);
        }

        public void Browse(string body, string? options) {
            string? window = null;
            Vector2i size = (480, 480);

            if (options != null) {
                foreach (string option in options.Split(',', ';', '&')) {
                    string optionTrimmed = option.Trim();

                    if (optionTrimmed != String.Empty) {
                        string[] optionSeparated = optionTrimmed.Split("=", 2);
                        string key = optionSeparated[0];
                        string value = optionSeparated[1];

                        if (key == "window") {
                            window = value;
                        } else if (key == "size") {
                            string[] sizeSeparated = value.Split("x", 2);

                            size = (int.Parse(sizeSeparated[0]), int.Parse(sizeSeparated[1]));
                        }
                    }
                }
            }

            var msg = new MsgBrowse() {
                Size = size,
                Window = window,
                HtmlSource = body
            };

            Session?.ConnectedClient.SendMessage(msg);
        }

        public void WinSet(string? controlId, string @params) {
            var msg = new MsgWinSet() {
                ControlId = controlId,
                Params = @params
            };

            Session?.ConnectedClient.SendMessage(msg);
        }

        public void WinClone(string controlId, string cloneId) {
            var msg = new MsgWinClone() { ControlId = controlId, CloneId = cloneId, };

            Session?.ConnectedClient.SendMessage(msg);
        }
    }
}
