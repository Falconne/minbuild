using System;
using System.Collections.Generic;
using System.IO;
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
            var inputFiles = ParseFileList(Inputs).ToList();
            var cppInput = GetHashForFiles(inputFiles);
            cppInput = GetHashForContent(cppInput + BuildConfig);
            var tlogCacheLocation = GetCacheDirForHash(cppInput);
            if (string.IsNullOrWhiteSpace(tlogCacheLocation)) return true;

            var clReadTlog = Path.Combine(tlogCacheLocation, "CL.read.1.tlog");
            if (!File.Exists(clReadTlog))
            {
                LogProjectMessage(clReadTlog + " not found, skipping cache");
                return true;
            }
            var compileInputs = ReadTlogFile(clReadTlog, false);

            var clWriteLog = Path.Combine(tlogCacheLocation, "CL.write.1.tlog");

            return true;
        }

        private IEnumerable<string> ReadTlogFile(string file, bool ignoreComments)
        {
            var lines = File.ReadAllLines(file);
            return ignoreComments ? lines.Where(x => !x.StartsWith("^")) : lines;
        } 
    }
}
