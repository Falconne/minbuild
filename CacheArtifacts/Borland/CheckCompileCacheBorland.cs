using Microsoft.Build.Framework;

namespace MinBuild.Borland
{
    public class CheckCompileCacheBorland : CacheTaskBorland
    {
        [Output]
        public string InputHash { get; private set; }

        [Output]
        public bool RestoreSuccessful { get; private set; }

        public override bool Execute()
        {
            bool restoreSucessful;
            InputHash = RestoreCachedArtifactsIfPossible(ParseInputFiles(), GetOutputFiles(), out restoreSucessful);
            RestoreSuccessful = restoreSucessful;
            LogProjectMessage("InputHash returned: " + InputHash, MessageImportance.Normal);

            return true;
        }
    }
}
