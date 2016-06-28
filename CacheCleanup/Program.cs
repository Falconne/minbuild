using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NLog;

namespace CacheCleanup
{
    class Program
    {
        static void Main(string[] args)
        {
            var cacheRoot = @"c:\temp\minbuild";
            if (args.Length > 0)
                cacheRoot = args[0];

            logger.Info("Cleaning up cache root {0}", cacheRoot);
            if (!Directory.Exists(cacheRoot))
                return;

            foreach (var type in Directory.GetDirectories(cacheRoot))
            {
                foreach (var version in Directory.GetDirectories(type))
                {
                    logger.Info("Cleaning cache under: " + version);
                    CheckAndCleanVersionDir(version);
                }
            }
        }

        private static void CheckAndCleanVersionDir(string versionDir)
        {
            var now = DateTime.UtcNow;
            logger.Debug("Current time UTC is {0}", now);
            var allDirs = Directory.GetDirectories(versionDir);
            foreach (var directory in allDirs)
            {
                logger.Debug("Check {0}", directory);
                var dirTime = new DirectoryInfo(directory).LastWriteTimeUtc;
                logger.Debug("Directory write time is {0}", dirTime);
                var diff = now - dirTime;
                logger.Debug("Age in days: " + diff.Days);
                if (diff.Days > 1)
                {
                    logger.Info("Deleting old directory {0} ", directory);
                    try
                    {
                        Directory.Delete(directory, true);
                    }
                    catch (Exception e)
                    {
                        logger.Error("Cannot delete {0} due to {1}", directory, e.Message);
                    }
                }
                else
                {
                    logger.Debug("Directory is still current");
                }
            }
        }

        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
    }
}
