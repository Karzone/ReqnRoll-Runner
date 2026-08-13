namespace ReqnrollRunner.Core.Model
{
    /// <summary>The unit test framework a Reqnroll project generates code for.</summary>
    public enum RunnerKind
    {
        /// <summary>No <c>Reqnroll.*</c> runner package could be identified.</summary>
        Unknown = 0,
        NUnit = 1,
        XUnit = 2,
        MsTest = 3,
    }
}
