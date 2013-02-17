﻿using System;
using System.Collections.Generic;
using System.Linq;

namespace Sigil.Impl
{
    // Represents a type that *could be* anything
    internal class WildcardType { }

    public class StackTransition
    {
        internal int PoppedCount { get { return PoppedFromStack.Count(); } }

        // on the stack, first item is on the top of the stack
        internal IEnumerable<TypeOnStack> PoppedFromStack { get; private set; }

        // pushed onto the stack, first item is first pushed (ends up lowest on the stack)
        internal IEnumerable<TypeOnStack> PushedToStack { get; private set; }

        public StackTransition(IEnumerable<Type> popped, IEnumerable<Type> pushed)
            : this
            (
                popped.Select(s => TypeOnStack.Get(s)),
                pushed.Select(s => TypeOnStack.Get(s))
            )
        { }

        internal StackTransition(IEnumerable<TypeOnStack> popped, IEnumerable<TypeOnStack> pushed)
        {
            PoppedFromStack = popped.ToList().AsReadOnly();
            PushedToStack = pushed.ToList().AsReadOnly();
        }

        public override string ToString()
        {
            return "(" + string.Join(", ", PoppedFromStack.Select(p => p.ToString()).ToArray()) + ") => (" + string.Join(", ", PushedToStack.Select(p => p.ToString()).ToArray()) + ")";
        }

        public static StackTransition[] None() { return new[] { new StackTransition(Type.EmptyTypes, Type.EmptyTypes) }; }
        public static StackTransition[] Push<PushType>() { return Push(typeof(PushType)); }
        public static StackTransition[] Push(Type pushType) { return Push(TypeOnStack.Get(pushType)); }
        internal static StackTransition[] Push(TypeOnStack pushType) { return new[] { new StackTransition(new TypeOnStack[0], new[] { pushType }) }; }

        public static StackTransition[] Pop<PopType>() { return Pop(typeof(PopType)); }
        public static StackTransition[] Pop(Type popType) { return Pop(TypeOnStack.Get(popType)); }
        internal static StackTransition[] Pop(TypeOnStack popType) { return new[] { new StackTransition(new[] { popType }, new TypeOnStack[0]) }; }
    }

    public class VerifiableTracker
    {
        // When the stack is "unbased" or "baseless", underflowing it results in wildcards
        //   eventually they'll be fixed up to actual types
        private bool Baseless;
        private List<IEnumerable<StackTransition>> Transitions = new List<IEnumerable<StackTransition>>();

        public VerifiableTracker(bool baseless = false) { Baseless = baseless; }

        public IEnumerable<StackTransition> DuplicateTop()
        {
            // TODO: deal with the "empty" case
            var last = Transitions[Transitions.Count - 1];

            return
                last.Select(
                     l => new StackTransition(new TypeOnStack[0], l.PushedToStack)
                );
        }

        public bool Transition(IEnumerable<StackTransition> legalTransitions)
        {
            Transitions.Add(legalTransitions);
            var ret = CollapseAndVerify();

            // revert!
            if(!ret) Transitions.RemoveAt(Transitions.Count - 1);

            return ret;
        }

        public bool Incoming(VerifiableTracker other)
        {
            var old = Transitions;

            Transitions = new List<IEnumerable<StackTransition>>();
            Transitions.AddRange(other.Transitions);
            Transitions.AddRange(old);

            var ret = CollapseAndVerify();

            // revert!
            if (!ret) Transitions = old;

            return ret;
        }

        public bool CollapseAndVerify()
        {
            var runningStack = new Stack<IEnumerable<TypeOnStack>>();

            for (var i = 0; i < Transitions.Count; i++)
            {
                var ops = Transitions[i];

                var legal =
                    ops.Where(
                        w =>
                        {
                            var onStack = runningStack.Peek(Baseless, w.PoppedCount);

                            if (onStack == null)
                            {
                                return false;
                            }

                            for (var j = 0; j < w.PoppedCount; j++)
                            {
                                var shouldBe = w.PoppedFromStack.ElementAt(j);
                                var actuallyIs = onStack[j];

                                if (!actuallyIs.Any(a => shouldBe.IsAssignableFrom(a)))
                                {
                                    return false;
                                }
                            }

                            return true;
                        }
                    ).ToList();

                if (legal.Count == 0)
                {
                    return false;
                }

                if (legal.GroupBy(g => new { a = g.PoppedCount, b = g.PushedToStack.Count() }).Count() > 1)
                {
                    throw new Exception("Shouldn't be possible; legal transitions should have same push/pop #s");
                }

                // No reason to do all this work again
                Transitions[i] = legal;

                var toPop = legal.First().PoppedCount;

                if (toPop > runningStack.Count && !Baseless)
                {
                    return false;
                }

                for (var j = 0; j < toPop && runningStack.Count > 0; j++)
                {
                    runningStack.Pop();
                }

                var toPush = new List<TypeOnStack>();

                foreach (var op in legal)
                {
                    toPush.AddRange(op.PushedToStack);
                }

                if(toPush.Count > 0)
                {
                    runningStack.Push(toPush.Distinct().ToList());
                }
            }

            return true;
        }

        public VerifiableTracker Clone()
        {
            return
                new VerifiableTracker
                {
                    Baseless = Baseless,
                    Transitions = Transitions.ToList()
                };
        }
    }
}