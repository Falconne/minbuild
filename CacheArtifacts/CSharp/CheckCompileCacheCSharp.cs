using Microsoft.Build.Framework;

namespace MinBuild
{
    public class CheckCompileCacheCSharp : CacheTaskCSharp
    {
        [Output]
        public string InputHash { get; private set; }

        [Output]
        public bool RestoreSuccessful { get; private set; }

        public override bool Execute()
        {
            var outputFiles = ParseFileList(Outputs);
            if (ShouldSkipCache(outputFiles))
            {
                RestoreSuccessful = false;
                InputHash = "SKIP";
                return true;
            }

            var inputFiles = ParseFileList(Inputs);
            CheckForMissingInputs(inputFiles);

            InputHash = RestoreCachedArtifactsIfPossible(inputFiles, outputFiles, out var restoreSuccessful);
            RestoreSuccessful = restoreSuccessful;
            LogProjectMessage($"RestoreSuccessful : {RestoreSuccessful}", MessageImportance.Normal);

            LogProjectMessage($"InputHash returned: {InputHash}", MessageImportance.Low);

            return true;
        }
    }
}
