using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace MinBuild
{
    public abstract class CacheTaskParent : Task
    {
        [Required]
        public string ProjectName { get; set; }

        [Required]
        public string BranchVersion { get; set; }

        [Required]
        public string CacheRoot { get; set; }

        [Required]
        public bool ShowContentHashes { private get; set; }
        

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
                Log.LogWarning("Cache cannot be used with duplicate output files:");
                var duplicates = outputFiles.Where(file => !hashset.Add(Path.GetFileName(file))).ToList();
                duplicates.ForEach(x => Log.LogWarning("\t" + x));
                return true;
            }

            return false;
        }

        protected void LogProjectMessage(string message, 
            MessageImportance importance = MessageImportance.High)
        {
            Log.LogMessage(importance, string.Format("{0}: {1}", ProjectName, message));
        }

        protected abstract string GetCacheType();

        protected string BranchCacheLocation { get { return Path.Combine(CacheLocation, BranchVersion); } }

        protected string PrecomputedCacheLocation { get { return Path.Combine(CacheLocation, "Precomputed"); } }

        protected bool CacheBuildArtifacts(IList<string> outputFiles, string cacheHash)
        {
            if (ShouldSkipCache(outputFiles))
            {
                return true;
            }

            var cacheOutput = Path.Combine(BranchCacheLocation, cacheHash);
            LogProjectMessage("Caching artifacts to " + cacheOutput, MessageImportance.High);
            if (Directory.Exists(cacheOutput))
            {
                Log.LogWarning(ProjectName + ": Cache dir is being created by another thread");
                return true;
            }

            Directory.CreateDirectory(cacheOutput);
            foreach (var outputFile in outputFiles)
            {
                LogProjectMessage("\t" + outputFile, MessageImportance.High);
                var dst = Path.Combine(cacheOutput, Path.GetFileName(outputFile));
                if (File.Exists(dst))
                {
                    Log.LogWarning(ProjectName + ": Cache files are being created by another thread");
                    return true;
                }

                File.Copy(outputFile, dst);
            }

            var completeMarker = Path.Combine(cacheOutput, "complete");
            File.Create(completeMarker).Close();

            return true;
        }

        // Content hash is the hash of each input file's content, concatenanted then rehashed
        protected string GetHashForFiles(IList<string> files)
        {
            // TODO detect architecture
            var sb = new StringBuilder(48 * (files.Count() + 5));
            var priority = (ShowContentHashes) ? MessageImportance.High : MessageImportance.Low;
            foreach (var file in files)
            {
                LogProjectMessage("\t\t\tInput: " + file, priority);
                var fileHash = GetHashForFile(file);
                LogProjectMessage("\t\t\tHash: " + fileHash, priority);
                sb.Append(fileHash);
            }

            var hashString = GetHashForContent(sb.ToString());
            return hashString;
        }

        protected string GetHashForContent(byte[] bytes)
        {
            var cryptoProvider = new MD5CryptoServiceProvider();
            var hashString = BitConverter.ToString(cryptoProvider.ComputeHash(bytes));

            return hashString;
        }

        protected string GetHashForContent(string raw)
        {
            return GetHashForContent(Encoding.Default.GetBytes(raw));
        }

        protected string GetHashForFile(string file, bool allowPrecompute = true)
        {
            if (allowPrecompute)
            {
                // TODO be smarter (remember, msbuild tasks run 32bit)
                if (file.Contains(@"\Program Files"))
                {
                    return GetPrecomputedHashFor(file);
                }
            }

            var bytes = GetBytesWithRetry(file);
            return GetHashForContent(bytes);
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Microsoft.Interoperability", "CA1404:CallGetLastErrorImmediatelyAfterPInvoke")]
        protected static byte[] GetBytesWithRetry(string path)
        {
            var autoResetEvent = new AutoResetEvent(false);

            while (true)
            {
                try
                {
                    return File.ReadAllBytes(path);
                }
                catch (IOException)
                {
                    // Multithreaded builds will take exclusive locks on shared files
                    var win32Error = Marshal.GetLastWin32Error();

                    if (win32Error != ERROR_SHARING_VIOLATION && win32Error != ERROR_LOCK_VIOLATION) continue;

                    using (var fileSystemWatcher = new FileSystemWatcher(Path.GetDirectoryName(path),
                            "*" + Path.GetExtension(path))
                    {
                        EnableRaisingEvents = true
                    })
                    {
                        fileSystemWatcher.Changed +=
                            (o, e) =>
                            {
                                if (Path.GetFullPath(e.FullPath) == Path.GetFullPath(path))
                                {
                                    autoResetEvent.Set();
                                }
                            };

                        autoResetEvent.WaitOne();
                    }
                }
            }
        }

        // Cache and retrieve procomputed content hash for SDK input files
        protected string GetPrecomputedHashFor(string filepath)
        {
            //LogProjectMessage("Checking for precomputed hash for " + filepath);
            var fi = new FileInfo(filepath);
            var dirAsSubpath = fi.FullName.Replace(":", "");
            var cachePath = Path.Combine(PrecomputedCacheLocation, dirAsSubpath);

            if (Directory.Exists(cachePath))
            {
                var hashFile = Directory.EnumerateFiles(cachePath, "*.hash").FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(hashFile))
                {
                    var hfi = new FileInfo(hashFile);
                    if (hfi.LastWriteTime != fi.LastWriteTime)
                    {
                        LogProjectMessage("Precomputed hash out of date for " + filepath);
                    }
                    else
                    {
                        return Path.GetFileNameWithoutExtension(hashFile);
                    }
                }
            }
            else
            {
                Directory.CreateDirectory(cachePath);
            }

            var hash = GetHashForFile(filepath, false);
            var newHashFile = Path.Combine(cachePath, hash + ".hash");

            // As build is multithreaded, other threads could be writing this file out.
            // Ignore any write errors as it doesn't mater who updates the cache file.
            try
            {
                File.Create(newHashFile).Close();
            }
            catch (IOException e)
            {
                Log.LogWarning("Cannot create precomputed hash " + newHashFile);
                Log.LogWarning("Reason " + e.Message);
                return hash;
            }
            try
            {
                // Used to ensure we recompute if the source file is modified
                File.SetLastWriteTime(newHashFile, fi.LastWriteTime);
            }
            catch (IOException)
            {
                Log.LogWarning("Cannot set last modified time on " + newHashFile);
            }

            return hash;
        }

        protected string GetCacheDirForHash(string hash)
        {
            var cacheOutput = Path.Combine(BranchCacheLocation, hash);
            if (!Directory.Exists(cacheOutput))
            {
                LogProjectMessage("Artifacts not cached, recompiling...");
                return null;
            }

            var completeMarker = Path.Combine(cacheOutput, "complete");
            if (!File.Exists(completeMarker))
            {
                LogProjectMessage("Cache dir incomplete, recompiling...");
                return null;
            }

            LogProjectMessage("Retrieving cached artifacts from " + cacheOutput);
            // Touch to reset deletion timer
            File.SetLastWriteTimeUtc(completeMarker, DateTime.UtcNow);

            return cacheOutput;
        }

        private const long ERROR_SHARING_VIOLATION = 0x20;
        private const long ERROR_LOCK_VIOLATION = 0x21;

        private string CacheLocation
        {
            get { return Path.Combine(CacheRoot, GetCacheType()); }
        }

    }
}