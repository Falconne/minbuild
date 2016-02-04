﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Build.Framework;

namespace MinBuild.Borland
{
    public abstract class CacheTaskBorland : CacheTaskParent
    {
        [Required]
        public string Makefile { private get; set; }

        [Required]
        public string WorkDir { private get; set; }

        protected override string GetCacheType()
        {
            return "borland";
        }

        protected IList<string> ParseInputFiles()
        {
            var mfloc = Path.Combine(WorkDir, Makefile);
            LogProjectMessage("Makefile " + mfloc);
            if (!File.Exists(mfloc))
            {
                throw new Exception(mfloc + " not found");
            }

            LogProjectMessage("Reading inputs from " + mfloc);
            var lines = File.ReadAllLines(mfloc);
            var sources = ParseSourceType("SOURCE=", lines).Where(x => !x.ToLower().EndsWith("_ver.rc")).ToList();
            sources.AddRange(ParseSourceType("LIBS=", lines));
            if (!sources.Any())
                throw new Exception("No sources found in " + mfloc);

            LogProjectMessage("Found sources: ");
            sources.Add(mfloc);
            sources.ForEach(x => LogProjectMessage(x));
            return sources;
        }

        protected string GetInputHash()
        {

            return GetHashForFiles(ParseInputFiles());
        }

        protected IList<string> GetOutputFile()
        {
            var mfloc = Path.Combine(WorkDir, Makefile);
            LogProjectMessage("Reading output from " + mfloc);
            var lines = File.ReadAllLines(mfloc);
            foreach (var line in lines)
            {
                if (!line.StartsWith("TARGET="))
                    continue;

                var cleanLine = line.Replace("\t", "");
                var parts = cleanLine.Split('=').ToList();
                parts.RemoveAt(0);
                return parts;
            }

            throw new Exception("Target not found in " + mfloc);
        }

        private List<string> ParseSourceType(string type, IList<string> lines)
        {
            var sources = new List<string>();
            var sourcesFound = false;
            for (var i = 0; i < lines.Count(); i++)
            {
                var line = lines[i];
                if (!sourcesFound)
                {
                    if (!line.StartsWith(type))
                        continue;

                    sourcesFound = true;
                    line = line.Replace(type, "");
                }

                if (line.Contains("="))
                    break;

                line = line.Replace("\t", "");

                var lineSources = line.Split(' ').Where(y => !y.Equals("\\") && !string.IsNullOrWhiteSpace(y) && !y.Contains("$")).Select(
                    x => Path.Combine(WorkDir, x)).Where(File.Exists);
                sources.AddRange(lineSources);
            }

            return sources;
        }
    }
}
