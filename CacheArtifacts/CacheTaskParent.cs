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
        public string Outputs { get; set; }

        protected static IEnumerable<string> ParseFileList(string raw)
        {
            var nonVersionInfoFiles =
                raw.Split(';').Where(x => 
                    !string.IsNullOrWhiteSpace(x));

            var uniqueFiles = nonVersionInfoFiles.Select(y => y.Trim()).OrderBy(z => z).Distinct();
            return uniqueFiles;
        }

        // Content hash is the hash of each input file's content, concatenanted then rehashed
        protected string GetContentHash(IList<string> files)
        {
            // TODO detect architecture
            var sb = new StringBuilder(48 * (files.Count() + 5));
            foreach (var file in files)
            {
                LogProjectMessage("\t\t\tInput: " + file, MessageImportance.Low);
                var fileHash = GetHashForFile(file);
                sb.Append(fileHash);
            }

            var hashString = GetHashForContent(sb.ToString());
            return hashString;
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

        private string GetHashForContent(byte[] bytes)
        {
            var cryptoProvider = new MD5CryptoServiceProvider();
            var hashString = BitConverter.ToString(cryptoProvider.ComputeHash(bytes));
            LogProjectMessage("\t\t\tComputed Hash: " + hashString, MessageImportance.Low);

            return hashString;
        }

        private string GetHashForContent(string raw)
        {
            return GetHashForContent(Encoding.Default.GetBytes(raw));
        }

        private string GetHashForFile(string file, bool allowPrecompute = true)
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

        protected const string CacheLocation = @"c:\temp\minbuild";

        private static byte[] GetBytesWithRetry(string path)
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
        private string GetPrecomputedHashFor(string filepath)
        {
            //LogProjectMessage("Checking for precomputed hash for " + filepath);
            var precomputedHashLocation = Path.Combine(CacheLocation, "Precomputed");
            var fi = new FileInfo(filepath);
            var dirAsSubpath = fi.FullName.Replace(":", "");
            var cachePath = Path.Combine(precomputedHashLocation, dirAsSubpath);

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
            File.Create(newHashFile).Close();
            // Used to ensure we recompute if the source file is modified
            File.SetLastWriteTime(newHashFile, fi.LastWriteTime);
            LogProjectMessage(string.Format("Created precomputed hash {0} for {1}, stored in {2}", hash, filepath, newHashFile));

            return hash;
        }

        private const long ERROR_SHARING_VIOLATION = 0x20;
        private const long ERROR_LOCK_VIOLATION = 0x21;
    }
}