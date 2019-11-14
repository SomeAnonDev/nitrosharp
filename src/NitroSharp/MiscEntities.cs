﻿using NitroSharp.Experimental;

namespace NitroSharp
{
    internal sealed class ThreadRecordStorage : EntityStorage
    {
        public ComponentStorage<InterpreterThreadInfo> Infos { get; }

        public ThreadRecordStorage(EntityHub hub, uint initialCapacity)
            : base(hub, initialCapacity)
        {
            Infos = AddComponentStorage<InterpreterThreadInfo>();
        }
    }

    internal readonly struct InterpreterThreadInfo
    {
        public InterpreterThreadInfo(string name, string module, string target)
        {
            Name = name;
            Module = module;
            Target = target;
        }

        public readonly string Name;
        public readonly string Module;
        public readonly string Target;
    }
}