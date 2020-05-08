﻿using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using NitroSharp.NsScript.Primitives;
using NitroSharp.NsScript.Utilities;

namespace NitroSharp.NsScript.VM
{
    public sealed class NsScriptVM
    {
        private readonly struct ThreadAction
        {
            public enum ActionKind
            {
                Create,
                Terminate,
                Suspend,
                Resume
            }

            public readonly ThreadContext Thread;
            public readonly ActionKind Kind;
            public readonly TimeSpan? Timeout;

            public static ThreadAction Create(ThreadContext thread)
                => new ThreadAction(thread, ActionKind.Create, null);

            public static ThreadAction Terminate(ThreadContext thread)
                => new ThreadAction(thread, ActionKind.Terminate, null);

            public static ThreadAction Suspend(ThreadContext thread, TimeSpan? timeout)
                => new ThreadAction(thread, ActionKind.Suspend, timeout);

            public static ThreadAction Resume(ThreadContext thread)
                => new ThreadAction(thread, ActionKind.Resume, null);

            private ThreadAction(ThreadContext thread, ActionKind kind, TimeSpan? timeout)
            {
                Thread = thread;
                Kind = kind;
                Timeout = timeout;
            }
        }

        private readonly NsxModuleLocator _moduleLocator;
        private readonly Dictionary<string, NsxModule> _loadedModules;
        private readonly BuiltInFunctionDispatcher _builtInCallDispatcher;
        private readonly List<ThreadContext> _threads;
        private readonly Dictionary<string, ThreadContext> _threadMap;
        private readonly Queue<ThreadAction> _pendingThreadActions;
        private readonly Stopwatch _timer;
        private readonly ConstantValue[] _globals;
        private readonly Stack<CubicBezierSegment> _bezierSegmentStack;

        public NsScriptVM(
            NsxModuleLocator moduleLocator,
            Stream globalVarLookupTableStream)
        {
            _loadedModules = new Dictionary<string, NsxModule>(16);
            _moduleLocator = moduleLocator;
            _builtInCallDispatcher = new BuiltInFunctionDispatcher();
            _threads = new List<ThreadContext>();
            _threadMap = new Dictionary<string, ThreadContext>();
            _pendingThreadActions = new Queue<ThreadAction>();
            _timer = Stopwatch.StartNew();
            _globals = new ConstantValue[5000];
            GlobalVarLookup = GlobalVarLookupTable.Load(globalVarLookupTableStream);
            SystemVariables = new SystemVariableLookup(this);
            _bezierSegmentStack = new Stack<CubicBezierSegment>();
        }

        internal GlobalVarLookupTable GlobalVarLookup { get; }
        public SystemVariableLookup SystemVariables { get; }
        public ThreadContext? MainThread { get; private set; }
        public ThreadContext? CurrentThread { get; private set; }
        public IReadOnlyList<ThreadContext> Threads => _threads;

        public ThreadContext CreateThread(string name, string symbol, bool start = false)
            => CreateThread(name, CurrentThread!.CurrentFrame.Module.Name, symbol, start);

        public void ActivateDialogueBlock(in DialogueBlockToken blockToken)
        {
            var frame = new CallFrame(
                blockToken.Module,
                (ushort)blockToken.SubroutineIndex,
                pc: blockToken.Offset
            );
            CurrentThread!.CallFrameStack.Push(frame);
        }

        public ThreadContext CreateThread(string name, string moduleName, string symbol, bool start = true)
        {
            NsxModule module = GetModule(moduleName);
            ushort subIndex = (ushort)module.LookupSubroutineIndex(symbol);
            var frame = new CallFrame(module, subIndex);
            var thread = new ThreadContext(name, ref frame);
            _pendingThreadActions.Enqueue(ThreadAction.Create(thread));
            MainThread ??= thread;
            if (!start)
            {
                CommitSuspendThread(thread, null);
            }
            return thread;
        }

        public NsxModule GetModule(string name)
        {
            if (!_loadedModules.TryGetValue(name, out NsxModule? module))
            {
                Stream stream = _moduleLocator.OpenModule(name);
                module = NsxModule.LoadModule(stream, name);
                _loadedModules.Add(name, module);
            }

            return module;
        }

        public void SuspendThread(ThreadContext thread, TimeSpan? timeout = null)
        {
            _pendingThreadActions.Enqueue(ThreadAction.Suspend(thread, timeout));
        }

        public void ResumeThread(ThreadContext thread)
        {
            _pendingThreadActions.Enqueue(ThreadAction.Resume(thread));
        }

        public void TerminateThread(ThreadContext thread)
        {
            _pendingThreadActions.Enqueue(ThreadAction.Terminate(thread));
        }

        public void SuspendMainThread()
        {
            Debug.Assert(MainThread != null);
            SuspendThread(MainThread);
        }

        public void ResumeMainThread()
        {
            Debug.Assert(MainThread != null);
            ResumeThread(MainThread);
        }

        public bool TryGetThread(string name, [NotNullWhen(true)] out ThreadContext? thread)
        {
            return _threadMap.TryGetValue(name, out thread);
        }

        public void Run(BuiltInFunctions builtins, CancellationToken cancellationToken)
        {
            builtins._vm = this;
            long? time = null;
            foreach (ThreadContext thread in _threads)
            {
                if (thread.SuspensionTime != null && thread.SleepTimeout != null)
                {
                    time ??= _timer.ElapsedTicks;
                    long delta = time.Value - thread.SuspensionTime.Value;
                    if (delta >= thread.SleepTimeout)
                    {
                        CommitResumeThread(thread);
                    }
                }
            }

            while (_threads.Count > 0 || _pendingThreadActions.Count > 0)
            {
                ProcessPendingThreadActions();
                int nbActive = 0;
                foreach (ThreadContext thread in _threads)
                {
                    if (thread.IsActive && !thread.Yielded)
                    {
                        CurrentThread = thread;
                        nbActive++;
                        TickResult tickResult = Tick(thread, builtins);
                        if (tickResult == TickResult.Yield)
                        {
                            thread.Yielded = true;
                            nbActive--;
                        }
                        else if (thread.DoneExecuting)
                        {
                            TerminateThread(thread);
                            nbActive--;
                        }
                    }
                }

                if (nbActive == 0)
                {
                    foreach (ThreadContext thread in _threads)
                    {
                        thread.Yielded = false;
                    }

                    return;
                }
            }
        }

        private void ProcessPendingThreadActions()
        {
            while (_pendingThreadActions.TryDequeue(out ThreadAction action))
            {
                ThreadContext thread = action.Thread;
                switch (action.Kind)
                {
                    case ThreadAction.ActionKind.Create:
                        if (_threadMap.TryAdd(thread.Name, thread))
                        {
                            _threads.Add(thread);
                        }
                        break;
                    case ThreadAction.ActionKind.Terminate:
                        CommitTerminateThread(thread);
                        break;
                    case ThreadAction.ActionKind.Suspend:
                        CommitSuspendThread(thread, action.Timeout);
                        break;
                    case ThreadAction.ActionKind.Resume:
                        CommitResumeThread(thread);
                        break;
                }
            }
        }

        private void CommitSuspendThread(ThreadContext thread, TimeSpan? timeoutOpt)
        {
            thread.SuspensionTime = TicksFromTimeSpan(_timer.Elapsed);
            if (timeoutOpt is TimeSpan timeout)
            {
                thread.SleepTimeout = TicksFromTimeSpan(timeout);
            }
        }

        private void CommitResumeThread(ThreadContext thread)
        {
            thread.SleepTimeout = null;
            thread.SuspensionTime = null;
        }

        private void CommitTerminateThread(ThreadContext thread)
        {
            _threads.Remove(thread);
            _threadMap.Remove(thread.Name);
        }

        private static long TicksFromTimeSpan(TimeSpan timespan)
            => (long)(timespan.TotalSeconds * Stopwatch.Frequency);

        internal ref ConstantValue GetGlobalVar(int index)
        {
            ref ConstantValue val = ref _globals[index];
            if (val.Type == BuiltInType.Uninitialized)
            {
                val = ConstantValue.Integer(0);
            }

            return ref val;
        }

        private enum TickResult
        {
            Ok,
            Yield
        }

        private TickResult Tick(ThreadContext thread, BuiltInFunctions builtins)
        {
            if (thread.CallFrameStack.Count == 0)
            {
                return TickResult.Ok;
            }

            ref CallFrame frame = ref thread.CurrentFrame;
            NsxModule thisModule = frame.Module;
            Subroutine subroutine = thisModule.GetSubroutine(frame.SubroutineIndex);
            var program = new BytecodeStream(subroutine.Code, frame.ProgramCounter);
            ref ValueStack<ConstantValue> stack = ref thread.EvalStack;
            while (true)
            {
                Opcode opcode = program.NextOpcode();
                ushort varToken = 0;
                ConstantValue? imm = opcode switch
                {
                    Opcode.LoadImm => readConst(ref program, thisModule),
                    Opcode.LoadImm0 => ConstantValue.Integer(0),
                    Opcode.LoadImm1 => ConstantValue.Integer(1),
                    Opcode.LoadImmTrue => ConstantValue.True,
                    Opcode.LoadImmFalse => ConstantValue.False,
                    Opcode.LoadImmNull => ConstantValue.Null,
                    Opcode.LoadImmEmptyStr => ConstantValue.EmptyString,
                    Opcode.LoadVar => GetGlobalVar((varToken = program.DecodeToken())),
                    Opcode.LoadArg0 => stack[frame.ArgStart + 0],
                    Opcode.LoadArg1 => stack[frame.ArgStart + 1],
                    Opcode.LoadArg2 => stack[frame.ArgStart + 2],
                    Opcode.LoadArg3 => stack[frame.ArgStart + 3],
                    Opcode.LoadArg => stack[frame.ArgStart + program.ReadByte()],
                    _ => null
                };

                if (imm.HasValue)
                {
                    if (varToken != 0 && varToken == SystemVariables.PresentProcess)
                    {
                        string subName = thisModule.GetSubroutineName(frame.SubroutineIndex);
                        imm = _globals[varToken] = ConstantValue.String(subName);
                    }

                    ConstantValue value = imm.Value;
                    stack.Push(ref value);
                    continue;
                }

                switch (opcode)
                {
                    case Opcode.StoreVar:
                        int index = program.DecodeToken();
                        GetGlobalVar(index) = stack.Pop();
                        break;
                    case Opcode.StoreArg0:
                        stack[frame.ArgStart + 0] = stack.Pop();
                        break;
                    case Opcode.StoreArg1:
                        stack[frame.ArgStart + 1] = stack.Pop();
                        break;
                    case Opcode.StoreArg2:
                        stack[frame.ArgStart + 2] = stack.Pop();
                        break;
                    case Opcode.StoreArg3:
                        stack[frame.ArgStart + 3] = stack.Pop();
                        break;
                    case Opcode.StoreArg:
                        stack[frame.ArgStart + program.ReadByte()] = stack.Pop();
                        break;

                    case Opcode.Binary:
                        var opKind = (BinaryOperatorKind)program.ReadByte();
                        ConstantValue op2 = stack.Pop();
                        ConstantValue op1 = stack.Pop();
                        stack.Push(BinOp(op1, opKind, op2));
                        break;
                    case Opcode.Equal:
                        op2 = stack.Pop();
                        op1 = stack.Pop();
                        stack.Push(op1 == op2);
                        break;
                    case Opcode.NotEqual:
                        op2 = stack.Pop();
                        op1 = stack.Pop();
                        stack.Push(op1 != op2);
                        break;

#pragma warning disable IDE0059
                    case Opcode.Neg:
                        ref ConstantValue val = ref stack.Peek();
                        val = val.Type switch
                        {
                            BuiltInType.Integer => ConstantValue.Integer(-val.AsInteger()!.Value),
                            BuiltInType.Float => ConstantValue.Float(-val.AsFloat()!.Value),
                            _ => ThrowHelper.Unreachable<ConstantValue>()
                        };
                        break;
                    case Opcode.Inc:
                        val = ref stack.Peek();
                        Debug.Assert(val.Type == BuiltInType.Integer); // TODO: runtime error
                        val = ConstantValue.Integer(val.AsInteger()!.Value + 1);
                        break;
                    case Opcode.Dec:
                        val = ref stack.Peek();
                        Debug.Assert(val.Type == BuiltInType.Integer); // TODO: runtime error
                        val = ConstantValue.Integer(val.AsInteger()!.Value - 1);
                        break;
                    case Opcode.Delta:
                        val = ref stack.Peek();
                        Debug.Assert(val.Type == BuiltInType.Integer);
                        val = ConstantValue.Delta(val.AsInteger()!.Value);
                        break;
                    case Opcode.Invert:
                        val = ref stack.Peek();
                        Debug.Assert(val.AsBool() != null);
                        val = ConstantValue.Boolean(!val.AsBool()!.Value);
                        break;
#pragma warning restore IDE0059

                    case Opcode.Call:
                        ushort subroutineToken = program.DecodeToken();
                        ushort argCount = program.ReadByte();
                        ushort argStart = (ushort)(stack.Count - argCount);
                        frame.ProgramCounter = program.Position;
                        var newFrame = new CallFrame(frame.Module, subroutineToken, 0, argStart, argCount);
                        thread.CallFrameStack.Push(newFrame);
                        if (CurrentThread == MainThread)
                        {
                            string name = thisModule.GetSubroutineRuntimeInfo(subroutineToken)
                                .SubroutineName;
                            for (int i = 0; i < thread.CallFrameStack.Count; i++)
                            {
                                Console.Write(" ");
                            }
                            Console.WriteLine("near: " + name);
                        }
                        return TickResult.Ok;
                    case Opcode.CallFar:
                        ushort importTableIndex = program.DecodeToken();
                        subroutineToken = program.DecodeToken();
                        argCount = program.ReadByte();
                        argStart = (ushort)(stack.Count - argCount);
                        string externalModuleName = thisModule.Imports[importTableIndex];
                        NsxModule externalModule = GetModule(externalModuleName);
                        newFrame = new CallFrame(externalModule, subroutineToken, 0, argStart, argCount);
                        thread.CallFrameStack.Push(newFrame);
                        frame.ProgramCounter = program.Position;
                        if (CurrentThread == MainThread)
                        {
                            string name = externalModule.GetSubroutineRuntimeInfo(subroutineToken)
                                .SubroutineName;
                            for (int i = 0; i < thread.CallFrameStack.Count; i++)
                            {
                                Console.Write(" ");
                            }
                            Console.WriteLine("far: " + name);
                        }
                        return TickResult.Ok;
                    case Opcode.Jump:
                        int @base = program.Position - 1;
                        int offset = program.DecodeOffset();
                        program.Position = @base + offset;
                        break;
                    case Opcode.JumpIfTrue:
                        @base = program.Position - 1;
                        ConstantValue condition = stack.Pop();
                        offset = program.DecodeOffset();
                        Debug.Assert(condition.Type == BuiltInType.Boolean);
                        if (condition.AsBool()!.Value)
                        {
                            program.Position = @base + offset;
                        }
                        break;
                    case Opcode.JumpIfFalse:
                        @base = program.Position - 1;
                        condition = stack.Pop();
                        offset = program.DecodeOffset();
                        if (!condition.AsBool()!.Value)
                        {
                            program.Position = @base + offset;
                        }
                        break;
                    case Opcode.Return:
                        if (thread.CallFrameStack.Count > 0)
                        {
                            thread.CallFrameStack.Pop();
                        }
                        return TickResult.Ok;
                    case Opcode.BezierStart:
                        _bezierSegmentStack.Clear();
                        break;
                    case Opcode.BezierEndSeg:
                        static BezierControlPoint popPoint(ref ValueStack<ConstantValue> stack)
                        {
                            var x = NsCoordinate.FromValue(stack.Pop());
                            var y = NsCoordinate.FromValue(stack.Pop());
                            return new BezierControlPoint(x, y);
                        }

                        var seg = new CubicBezierSegment(
                            popPoint(ref stack),
                            popPoint(ref stack),
                            popPoint(ref stack),
                            popPoint(ref stack)
                        );
                        _bezierSegmentStack.Push(seg);
                        break;
                    case Opcode.BezierEnd:
                        var curve = new CompositeBezier(_bezierSegmentStack.ToImmutableArray());
                        stack.Push(ConstantValue.BezierCurve(curve));
                        break;
                    case Opcode.Dispatch:
                        var func = (BuiltInFunction)program.ReadByte();
                        argCount = program.ReadByte();
                        ReadOnlySpan<ConstantValue> args = stack.AsSpan(stack.Count - argCount, argCount);
                        switch (func)
                        {
                            default:
                                if (CurrentThread == MainThread)
                                {
                                    //Console.Write($"Built-in: {func.ToString()}(");
                                    //foreach (ref readonly ConstantValue cv in args)
                                    //{
                                    //    Console.Write(cv.ConvertToString() + ", ");
                                    //}
                                    //Console.Write(")\r\n");
                                }
                                ConstantValue? result = _builtInCallDispatcher.Dispatch(builtins, func, args);
                                stack.Pop(argCount);
                                if (result != null)
                                {
                                    stack.Push(result.Value);
                                }
                                break;

                            case BuiltInFunction.log:
                                ConstantValue arg = stack.Pop();
                                Console.WriteLine($"[VM]: {arg.ConvertToString()}");
                                break;
                            case BuiltInFunction.fail:
                                string subName = thisModule.GetSubroutineRuntimeInfo(
                                    frame.SubroutineIndex).SubroutineName;
                                Console.WriteLine($"{subName} + {program.Position - 1}: test failed.");
                                break;
                            case BuiltInFunction.fail_msg:
                                ConstantValue message = stack.Pop();
                                subName = thisModule.GetSubroutineRuntimeInfo(frame.SubroutineIndex)
                                    .SubroutineName;
                                Console.WriteLine($"{subName} + {program.Position - 1}: {message.ToString()}.");
                                break;
                        }
                        frame.ProgramCounter = program.Position;
                        return TickResult.Ok;

                    case Opcode.ActivateText:
                        ushort textId = program.DecodeToken();
                        ref readonly var srti = ref thisModule.GetSubroutineRuntimeInfo(
                            frame.SubroutineIndex
                        );
                        (string box, string textName) = srti.DialogueBlockInfos[textId];
                        SystemVariables.CurrentBoxName = ConstantValue.String(box);
                        SystemVariables.CurrentTextName = ConstantValue.String(textName);
                        break;

                    case Opcode.SelectStart:
                        break;
                    case Opcode.IsPressed:
                        string choiceName = thisModule.GetString(program.DecodeToken());
                        bool pressed = builtins.IsPressed(new EntityPath(choiceName));
                        stack.Push(ConstantValue.Boolean(pressed));
                        break;
                    case Opcode.SelectEnd:
                        frame.ProgramCounter = program.Position;
                        return TickResult.Yield;
                    case Opcode.PresentText:
                        string text = thisModule.GetString(program.DecodeToken());
                        builtins.BeginDialogueLine(text);
                        break;
                    case Opcode.AwaitInput:
                        builtins.WaitForInput();
                        frame.ProgramCounter = program.Position;
                        return TickResult.Ok;
                }
            }

            static ConstantValue readConst(ref BytecodeStream stream, NsxModule module)
            {
                Immediate imm = stream.DecodeImmediateValue();
                return imm.Type switch
                {
                    BuiltInType.Integer => ConstantValue.Integer(imm.IntegerValue),
                    BuiltInType.DeltaInteger => ConstantValue.Delta(imm.IntegerValue),
                    BuiltInType.BuiltInConstant => ConstantValue.BuiltInConstant(imm.Constant),
                    BuiltInType.String => ConstantValue.String(module.GetString(imm.StringToken)),
                    _ => ThrowHelper.Unreachable<ConstantValue>()
                };
            }
        }

        private static ConstantValue BinOp(
            in ConstantValue left,
            BinaryOperatorKind opKind,
            in ConstantValue right)
        {
            return opKind switch
            {
                BinaryOperatorKind.Add => left + right,
                BinaryOperatorKind.Subtract => left - right,
                BinaryOperatorKind.Multiply => left * right,
                BinaryOperatorKind.Divide => left / right,
                BinaryOperatorKind.LessThan => left < right,
                BinaryOperatorKind.LessThanOrEqual => left <= right,
                BinaryOperatorKind.GreaterThan => left > right,
                BinaryOperatorKind.GreaterThanOrEqual => left >= right,
                BinaryOperatorKind.And => left && right,
                BinaryOperatorKind.Or => left || right,
                BinaryOperatorKind.Remainder => left & right,
                _ => throw new NotImplementedException()
            };
        }
    }

    public class SystemVariableLookup
    {
        private readonly NsScriptVM _vm;
        private readonly GlobalVarLookupTable _nameLookup;

        public readonly int PresentProcess;
        private readonly int _presentPreprocess;
        private readonly int _presentText;

        private readonly int _rButtonDown;
        private readonly int _x360ButtonStartDown;
        private readonly int _x360ButtonADown;
        private readonly int _x360ButtonBDown;
        private readonly int _x360ButtonYDown;

        private readonly int _x360ButtonLeftDown;
        private readonly int _x360ButtonUpDown;
        private readonly int _x360ButtonRightDown;
        private readonly int _x360ButtonDownDown;

        private readonly int _x360ButtonLbDown;
        private readonly int _x360ButtonRbDown;

        public SystemVariableLookup(NsScriptVM vm)
        {
            _vm = vm;
            _nameLookup = vm.GlobalVarLookup;
            _presentPreprocess = Lookup("SYSTEM_present_preprocess");
            _presentText = Lookup("SYSTEM_present_text");
            PresentProcess = Lookup("SYSTEM_present_process");
            _rButtonDown = Lookup("SYSTEM_r_button_down");
            _x360ButtonStartDown = Lookup("SYSTEM_XBOX360_button_start_down");
            _x360ButtonADown = Lookup("SYSTEM_XBOX360_button_a_down");
            _x360ButtonBDown = Lookup("SYSTEM_XBOX360_button_b_down");
            _x360ButtonYDown = Lookup("SYSTEM_XBOX360_button_y_down");
            _x360ButtonLeftDown = Lookup("SYSTEM_XBOX360_button_left_down");
            _x360ButtonUpDown = Lookup("SYSTEM_XBOX360_button_up_down");
            _x360ButtonRightDown = Lookup("SYSTEM_XBOX360_button_right_down");
            _x360ButtonDownDown = Lookup("SYSTEM_XBOX360_button_down_down");
            _x360ButtonLbDown = Lookup("SYSTEM_XBOX360_button_lb_down");
            _x360ButtonRbDown = Lookup("SYSTEM_XBOX360_button_rb_down");
        }

        private int Lookup(string name)
        {
            _nameLookup.TryLookupSystemVariable(name, out int index);
            return index;
        }

        public ref ConstantValue CurrentSubroutineName => ref Var(PresentProcess);
        public ref ConstantValue CurrentBoxName => ref Var(_presentPreprocess);
        public ref ConstantValue CurrentTextName => ref Var(_presentText);
        public ref ConstantValue RightButtonDown => ref Var(_rButtonDown);

        public ref ConstantValue X360StartButtonDown => ref Var(_x360ButtonStartDown);
        public ref ConstantValue X360AButtonDown => ref Var(_x360ButtonADown);
        public ref ConstantValue X360BButtonDown => ref Var(_x360ButtonBDown);
        public ref ConstantValue X360YButtonDown => ref Var(_x360ButtonYDown);

        public ref ConstantValue X360LeftButtonDown => ref Var(_x360ButtonLeftDown);
        public ref ConstantValue X360UpButtonDown => ref Var(_x360ButtonUpDown);
        public ref ConstantValue X360RightButtonDown => ref Var(_x360ButtonRightDown);
        public ref ConstantValue X360DownButtonDown => ref Var(_x360ButtonDownDown);

        public ref ConstantValue X360LbButtonDown => ref Var(_x360ButtonLbDown);
        public ref ConstantValue X360RbButtonDown => ref Var(_x360ButtonRbDown);

        private ref ConstantValue Var(int index) => ref _vm.GetGlobalVar(index);
    }
}
