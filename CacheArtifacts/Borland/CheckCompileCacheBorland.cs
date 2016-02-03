using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
            InputHash = RestoreCachedArtifactsIfPossible(ParseInputFiles(), GetOutputFile(), out restoreSucessful);
            RestoreSuccessful = restoreSucessful;
            LogProjectMessage("InputHash returned: " + InputHash);

            return true;
        }
    }
}
