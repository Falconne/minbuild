using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Build.Framework;

namespace MinBuild.Borland
{
    public class CacheArtifactsBorland : CacheTaskBorland
    {
        [Required]
        public string InputHash { set; private get; }

        public override bool Execute()
        {
            return CacheBuildArtifacts(GetOutputFiles(), InputHash);
        }
    }
}
