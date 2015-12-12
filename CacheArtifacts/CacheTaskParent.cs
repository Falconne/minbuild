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
        protected string GetContentHash(IEnumerable<string> files)
        {
            var sb = new StringBuilder();
            foreach (var file in files)
            {
                LogProjectMessage("\t\t\tInput: " + file, MessageImportance.Low);
                var bytes = GetBytesWithRetry(file);
                var fileHash = GetHashFor(bytes);
                sb.Append(fileHash);
            }

            var allHashesBytes = Encoding.Default.GetBytes(sb.ToString());
            var hashString = GetHashFor(allHashesBytes);
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

        private string GetHashFor(byte[] bytes)
        {
            var cryptoProvider = new MD5CryptoServiceProvider();
            var hashString = BitConverter.ToString(cryptoProvider.ComputeHash(bytes));
            LogProjectMessage("\t\t\tComputed Hash: " + hashString, MessageImportance.Low);

            return hashString;
        }

        private const long ERROR_SHARING_VIOLATION = 0x20;
        private const long ERROR_LOCK_VIOLATION = 0x21;
    }
}