using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MinBuild
{
    public abstract class CacheTaskCPP : CacheTaskParent
    {
        protected override string GetCacheType()
        {
            return "cpp";
        }
    }
}
