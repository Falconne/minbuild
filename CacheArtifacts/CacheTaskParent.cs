using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace MinBuild
{
    public abstract class CacheTaskParent : Task
    {
        [Required]
        public string Inputs { get; set; }


        [Required]
        public string Outputs { get; set; }

        protected IList<string> ParseFileList(string raw)
        {
            return raw.Split(';').Where(x => !string.IsNullOrWhiteSpace(x) && !x.Contains("AssemblyInfo.cs")).Select(
                y => y.Trim()).OrderBy(z => z).Distinct().ToList();
        }

        protected string GetFilenameHash(IEnumerable<string> files)
        {
            var composite = new StringBuilder();
            foreach (var file in files)
            {
                composite.Append(file);
                using (var md5Hash = MD5.Create())
                {
                    var hash = md5Hash.ComputeHash(Encoding.UTF8.GetBytes(composite.ToString()));
                    var sb = new StringBuilder();
                    foreach (var t in hash)
                    {
                        sb.Append(t.ToString("X2"));
                    }

                    var hashString = sb.ToString();
                    Log.LogMessage(MessageImportance.High, "Filename hash is " + hashString);
                    return hashString;
                }
            }

            return null;
        }

        protected string GetContentHash(IEnumerable<string> files)
        {
            foreach (var file in files)
            {
                using (var fs = new FileStream(file, FileMode.Open))
                using (var bs = new BufferedStream(fs))
                {
                    using (var cryptoProvider = new SHA1CryptoServiceProvider())
                    {
                        var hashString = BitConverter
                                .ToString(cryptoProvider.ComputeHash(bs));
                        hashString = hashString.Replace("-", "");
                        Log.LogMessage(MessageImportance.High, "Content hash is " + hashString);
                        return hashString;
                    }
                }
            }

            return null;
        }

    }
}