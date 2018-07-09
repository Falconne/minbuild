using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using MinBuildTests;

namespace MinBuild.Tests
{
    [TestClass()]
    public class CheckCompileCacheCSharpTests
    {
        [TestMethod()]
        public void ChecksumIgnoresAssemblyVersioning()
        {
            var builder = new CheckCompileCacheCSharp();
            builder.BuildEngine = new MockedBuildEngine();
            builder.Inputs = Path.Combine(Directory.GetCurrentDirectory(), 
                "resources\\AssemblyInfo.cs");
            builder.Outputs = "foo";
            builder.CacheRoot = Path.Combine(Path.GetTempPath(), "MinBuildTestCache");
            builder.ProjectName = "Test";
            builder.BranchVersion = "1.0";
            builder.ShowContentHashes = false;
            builder.ShowRecompileReason = false;
            builder.AlwaysRestoreCache = false;
            builder.BuildConfig = "Test Config";

            builder.Execute();
            Assert.IsTrue(builder.InputHash.Equals("F0-76-6C-0E-42-FA-A8-F8-EE-15-EB-12-4E-87-E7-C6"));
        }
    }
}