using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Build.Framework;

namespace MinBuild.Borland
{
    abstract class CacheTaskBorland : CacheTaskParent
    {
        [Required]
        public string Makefile { private get; set; }

        protected override string GetCacheType()
        {
            return "borland";
        }

        protected IList<string> ParseInputFiles()
        {
            if (!File.Exists(Makefile))
            {
                throw new Exception(Makefile + " not found");
            }

            LogProjectMessage("Reading inputs from " + Makefile);
            var lines = File.ReadAllLines(Makefile);
            var sourcesFound = false;
            var sources = new List<string>();
            for (var i = 0; i < lines.Count(); i++)
            {
                var line = lines[i];
                if (!sourcesFound)
                {
                    if (!line.StartsWith("SOURCE="))
                        continue;

                    sourcesFound = true;
                    line = line.Replace("SOURCE=", "");
                }

                if (line.Contains("="))
                    break;

                line = line.Replace("\t", "");

                var lineSources = line.Split(' ');
                sources.AddRange(lineSources);
            }

            if (!sources.Any())
                throw new Exception("No sources found in " + Makefile);

            LogProjectMessage("Found sources: ");
            sources.Add(Makefile);
            foreach (var source in sources.Where(source => !File.Exists(source)))
            {
                Log.LogError(ProjectName + ": Missing input " + source);
                File.Create(source);
            }

            return sources;
        }

        protected string GetInputHash()
        {

            return GetHashForFiles(ParseInputFiles());
        }

        protected IList<string> GetOutputFile()
        {
            LogProjectMessage("Reading output from " + Makefile);
            var lines = File.ReadAllLines(Makefile);
            foreach (var line in lines)
            {
                if (!line.StartsWith("TARGET="))
                    continue;

                var cleanLine = line.Replace("\t", "");
                var parts = cleanLine.Split('=').ToList();
                parts.RemoveAt(0);
                return parts;
            }

            throw new Exception("Target not found in " + Makefile);
        }
    }
}
