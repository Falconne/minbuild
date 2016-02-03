using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Build.Framework;

namespace MinBuild.Borland
{
    class CheckCompileCacheBorland : CacheTaskBorland
    {
        [Output]
        public string InputHash { get; private set; }

        public override bool Execute()
        {
            bool dummy;
            InputHash = RestoreCachedArtifactsIfPossible(ParseInputFiles(), GetOutputFile(), out dummy);
            LogProjectMessage("InputHash returned: " + InputHash);

            return true;
        }
    }
}
