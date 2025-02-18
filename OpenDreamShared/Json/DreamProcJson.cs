﻿using OpenDreamShared.Dream.Procs;
using System.Collections.Generic;
using JetBrains.Annotations;

namespace OpenDreamShared.Json {
    public sealed class ProcDefinitionJson {
        public int OwningTypeId { get; set; }
        public string Name { get; set; }
        public bool IsVerb { get; set; }
        public int MaxStackSize { get; set; }
        public List<ProcArgumentJson>? Arguments { get; set; }
        public List<LocalVariableJson> Locals { get; set; }
        public ProcAttributes Attributes { get; set; } = ProcAttributes.None;
        public string Source { get; set; }
        public int Line { get; set; }
        public byte[]? Bytecode { get; set; }

        public string? VerbName { get; set; }
        public string? VerbCategory { get; set; } = null;
        public string? VerbDesc { get; set; }
        public sbyte? Invisibility { get; set; }
    }

    public sealed class ProcArgumentJson {
        public string Name { get; set; }
        public DMValueType Type { get; set; }
    }

    public sealed class LocalVariableJson {
        public int Offset { get; set; }
        public int? Remove { get; set; }
        public string Add { get; set; }
    }
}
