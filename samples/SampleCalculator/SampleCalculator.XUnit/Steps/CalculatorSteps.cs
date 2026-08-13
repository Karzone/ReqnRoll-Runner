using Reqnroll;
using SampleCalculator.Support;

namespace SampleCalculator.Steps
{
    /// <summary>
    /// Step definitions for both the English and German feature files. Put a breakpoint anywhere in
    /// here and use "Debug Scenario" from the feature file to check the attach path end to end.
    /// </summary>
    [Binding]
    public sealed class CalculatorSteps
    {
        private readonly Calculator _calculator = new Calculator();

        [Given(@"the calculator is on")]
        [Given(@"der Rechner ist eingeschaltet")]
        public void GivenTheCalculatorIsOn() => _calculator.TurnOn();

        [Given(@"I entered (\d+)")]
        [Given(@"ich habe (\d+) eingegeben")]
        public void GivenIEntered(int value) => _calculator.Enter(value);

        [When(@"I press add")]
        [When(@"ich auf addieren drücke")]
        public void WhenIPressAdd() => _calculator.Add();

        [When(@"I press multiply")]
        public void WhenIPressMultiply() => _calculator.Multiply();

        [When(@"I press subtract")]
        public void WhenIPressSubtract() => _calculator.Subtract();

        [Then(@"the result should be (\d+)")]
        [Then(@"sollte das Ergebnis (\d+) sein")]
        public void ThenTheResultShouldBe(int expected)
        {
            if (_calculator.Result != expected)
            {
                throw new ReqnrollException(
                    "Expected " + expected + " but the calculator produced " + _calculator.Result + ".");
            }
        }
    }
}
