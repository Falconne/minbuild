﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using log4net.Repository.Hierarchy;

namespace CacheCleanup
{
    class Program
    {
        static void Main(string[] args)
        {
            var cacheRoot = @"c:\temp\minbuild";
            if (args.Length > 0)
                cacheRoot = args[0];

            logger.Info("Using cache root " + cacheRoot);
            CleanCSharpCache(Path.Combine(cacheRoot, "csharp"));
        }

        private static void CleanCSharpCache(string cacheLocation)
        {
            var now = DateTime.UtcNow;
            logger.Info("Current time UTC is " + now);
            foreach (var directory in Directory.GetDirectories(cacheLocation))
            {
                var di = new DirectoryInfo(directory);
                if (di.Name == "Precomputed")
                    continue;
                logger.Info("Check " + directory);
                var completeMarker = Path.Combine(directory, "complete");
                if (!File.Exists(completeMarker))
                {
                    // FIXME check if directory is orphaned
                    logger.Info("Completion marker not found, skipping");
                    continue;
                }
                var dirTime = di.LastWriteTimeUtc;
                logger.Info("Directory write time is " + dirTime);
                var diff = now - dirTime;
                logger.Info("Age in days: " + diff.Days);
                if (diff.Days > 4)
                {
                    logger.Info("Direactory is outdated");
                }
                else
                {
                    logger.Info("Directory is still current");
                }
            }
        }


        static readonly protected log4net.ILog logger =
           log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
    }
}
