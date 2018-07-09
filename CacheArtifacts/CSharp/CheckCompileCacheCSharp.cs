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
            var inputFiles = ParseFileList(Inputs);
            if (ShouldSkipCache(inputFiles, outputFiles))
            {
                RestoreSuccessful = false;
                InputHash = "SKIP";
                return true;
            }

            CheckForMissingInputs(inputFiles);

            InputHash = RestoreCachedArtifactsIfPossible(inputFiles, outputFiles, out var restoreSuccessful);
            RestoreSuccessful = restoreSuccessful;
            LogProjectMessage($"RestoreSuccessful : {RestoreSuccessful}", MessageImportance.Normal);

            LogProjectMessage($"InputHash returned: {InputHash}", MessageImportance.Low);

            return true;
        }
    }
}
