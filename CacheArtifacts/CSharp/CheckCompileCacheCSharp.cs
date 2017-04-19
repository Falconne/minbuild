using Microsoft.Build.Framework;

namespace MinBuild
{
    public class CheckCompileCacheCSharp : CacheTaskCSharp
    {
        [Output]
        public string InputHash { get; private set; }

        public override bool Execute()
        {
            var outputFiles = ParseFileList(Outputs);
            if (ShouldSkipCache(outputFiles))
            {
                InputHash = "SKIP";
                return true;
            }

            var inputFiles = ParseFileList(Inputs);
            CheckForMissingInputs(inputFiles);

            bool dummy;
            InputHash = RestoreCachedArtifactsIfPossible(inputFiles, outputFiles, out dummy);
            LogProjectMessage("InputHash returned: " + InputHash, MessageImportance.Low);

            return true;
        }
    }
}
