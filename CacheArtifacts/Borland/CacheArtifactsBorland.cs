using Microsoft.Build.Framework;

namespace MinBuild.Borland
{
    public class CacheArtifactsBorland : CacheTaskBorland
    {
        [Required]
        public string InputHash { set; private get; }

        public override bool Execute()
        {
            var inputFiles = ParseInputFiles();
            var outputFiles = GetOutputFiles();
            WriteSourceMapFile(inputFiles, outputFiles, WorkDir);
            CacheBuildArtifacts(inputFiles, outputFiles, InputHash);
            return true;
        }
    }
}
