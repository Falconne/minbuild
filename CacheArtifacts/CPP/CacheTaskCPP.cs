using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Build.Framework;

namespace MinBuild
{
    public abstract class CacheTaskCPP : CacheTaskParent
    {
        [Required]
        public string Inputs { protected get; set; }

        [Required]
        public string BuildConfig { protected get; set; }

        protected override string GetCacheType()
        {
            return "cpp";
        }

        protected string GetTLogCacheLocation()
        {
            var inputFiles = ParseFileList(Inputs).ToList();
            var cppInput = GetHashForFiles(inputFiles);
            cppInput = GetHashForContent(cppInput + BuildConfig);
            return GetCacheDirForHash(cppInput);
        }
    }
}
