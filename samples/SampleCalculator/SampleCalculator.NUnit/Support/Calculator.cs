using System.Collections.Generic;
using System.Linq;

namespace SampleCalculator.Support
{
    /// <summary>The system under test. Deliberately trivial — the sample exists to exercise the runner.</summary>
    public sealed class Calculator
    {
        private readonly List<int> _entries = new List<int>();

        public bool IsOn { get; private set; }

        public int Result { get; private set; }

        public void TurnOn() => IsOn = true;

        public void Enter(int value) => _entries.Add(value);

        public void Add() => Result = _entries.Sum();

        public void Multiply() => Result = _entries.Aggregate(1, (a, b) => a * b);

        public void Subtract() => Result = _entries.Skip(1).Aggregate(_entries.FirstOrDefault(), (a, b) => a - b);
    }
}
