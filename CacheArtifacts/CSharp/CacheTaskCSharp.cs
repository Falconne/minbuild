using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Build.Framework;

namespace MinBuild
{
    public abstract class CacheTaskCSharp : CacheTaskParent
    {
        [Required]
        public string Outputs { get; set; }

        [Required]
        public string Inputs { protected get; set; }

        protected override string GetCacheType()
        {
            return "chasrp";
        }
    }
}
