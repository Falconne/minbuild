using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace MinBuild
{
    public abstract class CacheTaskParent : Task
    {
        [Required]
        public string ProjectName { get; set; }

        [Required]
        public string Outputs { get; set; }

        [Required]
        public string BranchVersion { get; set; }

        protected static IEnumerable<string> ParseFileList(string raw)
        {
            var nonVersionInfoFiles =
                raw.Split(';').Where(x => 
                    !string.IsNullOrWhiteSpace(x));

            var uniqueFiles = nonVersionInfoFiles.Select(y => y.Trim()).OrderBy(z => z).Distinct();
            return uniqueFiles;
        }

        protected bool ShouldSkipCache(IEnumerable<string> outputFiles)
        {
            var hashset = new HashSet<string>();
            if (outputFiles.Any(file => !hashset.Add(Path.GetFileName(file))))
            {
                Log.LogMessage(MessageImportance.High, "Cache cannot be used with duplicate output files");
                return true;
            }

            return false;
        }

        protected void LogProjectMessage(string message, 
            MessageImportance importance = MessageImportance.High)
        {
            Log.LogMessage(importance, string.Format("{0}: {1}", ProjectName, message));
        }

        protected string BranchCacheLocation { get { return Path.Combine(CacheLocation, BranchVersion); } }

        protected string PrecomputedCacheLocation { get { return Path.Combine(CacheLocation, "Precomputed"); } }

        private const string CacheLocation = @"c:\temp\minbuild";

    }
}