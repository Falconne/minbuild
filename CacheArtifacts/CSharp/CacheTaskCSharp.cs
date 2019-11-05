using Microsoft.Build.Framework;

namespace MinBuild
{
    public abstract class CacheTaskCSharp : CacheTaskParent
    {
        [Required]
        public string Outputs { get; set; }

        [Required]
        public string Inputs { protected get; set; }

        [Required]
        public string NuGetPackageFolders { get; set; }


        protected CacheTaskCSharp()
        {
            SetNuGetPackageFolders(NuGetPackageFolders);
        }

        protected override string GetCacheType()
        {
            return "chasrp";
        }
    }
}
