﻿using System;
using NitroSharp.NsScript;
using System.Linq;
using NitroSharp.NsScript.Execution;
using NitroSharp.Content;

namespace NitroSharp
{
    internal sealed partial class CoreLogic : EngineImplementation
    {
        private readonly EntityManager _entities;
        private readonly Game _game;

        public CoreLogic(Game game, EntityManager entities)
        {
            _game = game;
            _entities = entities;
        }

        private ContentManager Content => _game.Content;
        private bool IsAnimationInProgress => MainThread.SleepTimeout != TimeSpan.MaxValue;

        public void InitializeResources()
        {
            LoadPageIndicator();
        }

        private void SuspendMainThread()
        {
            Interpreter.SuspendThread(MainThread);
        }

        private void ResumeMainThread()
        {
            Interpreter.ResumeThread(MainThread);
        }

        public override void SetAlias(string entityName, string alias)
        {
            if (entityName != alias && _entities.TryGet(entityName, out var entity))
            {
                entity.SetAlias(alias);
            }
        }

        public override void RemoveEntity(string entityName)
        {
            foreach (var entity in _entities.Query(entityName))
            {
                if (!entity.IsLocked())
                {
                    _entities.Remove(entity);
                    var attachedThread = Interpreter.Threads.FirstOrDefault(x => entityName.StartsWith(x.Name));
                    if (attachedThread != null) Interpreter.TerminateThread(attachedThread);
                }
            }
        }

        public override void Delay(TimeSpan delay)
        {
            Interpreter.SuspendThread(CurrentThread, delay);
        }

        public override void WaitForInput()
        {
            if (_dialogueState.DialogueLine?.IsEmpty == true)
            {
                return;
            }

            Interpreter.SuspendThread(CurrentThread);
            _dialogueState.Clear = true;
        }

        public override void WaitForInput(TimeSpan timeout)
        {
            Interpreter.SuspendThread(CurrentThread, timeout);
        }

        public override void CreateThread(string name, string target)
        {
            bool startImmediately = _entities.Query(name + "*").Any();
            Interpreter.CreateThread(name, target, startImmediately);
            _entities.Create(name, replace: true);
        }

        public override void Request(string entityName, NsEntityAction action)
        {
            foreach (var e in _entities.Query(entityName))
            {
                RequestCore(e, action);
            }
        }

        private void RequestCore(Entity entity, NsEntityAction action)
        {
            switch (action)
            {
                case NsEntityAction.Lock:
                    entity.Lock();
                    break;

                case NsEntityAction.Unlock:
                    entity.Unlock();
                    break;

                //case NsEntityAction.ResetText:
                //    TextEntity?.Destroy();
                //    break;

                //case NsEntityAction.Hide:
                //    var visual = entity.GetComponent<Visual>();
                //    if (visual != null)
                //    {
                //        //visual.IsEnabled = false;
                //    }
                //    break;

                case NsEntityAction.Dispose:
                    //entity.Destroy();
                    break;

                case NsEntityAction.Start:
                    if (Interpreter.TryGetThread(entity.Name, out var thread))
                    {
                        Interpreter.ResumeThread(thread);
                    }
                    break;

                case NsEntityAction.Stop:
                    if (Interpreter.TryGetThread(entity.Name, out thread))
                    {
                        Interpreter.TerminateThread(thread);
                    }
                    break;
            }
        }
    }
}