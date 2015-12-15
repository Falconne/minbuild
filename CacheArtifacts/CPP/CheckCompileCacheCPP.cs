using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Build.Framework;

namespace MinBuild.CPP
{
    public class CheckCompileCacheCPP : CacheTaskCPP
    {
        [Required]
        public string Inputs { private get; set; }

        [Required]
        public string BuildConfig { private get; set; }


        public override bool Execute()
        {
            LogProjectMessage("Recompile requested, checking for cached artifacts");
            LogProjectMessage("Build configuration: " + BuildConfig);

            return true;
        }
    }
}
