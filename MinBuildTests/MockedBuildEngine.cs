using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Build.Framework;

namespace MinBuildTests
{
    class MockedBuildEngine : IBuildEngine
    {
        public void LogErrorEvent(BuildErrorEventArgs e)
        {
        }

        public void LogWarningEvent(BuildWarningEventArgs e)
        {
        }

        public void LogMessageEvent(BuildMessageEventArgs e)
        {
        }

        public void LogCustomEvent(CustomBuildEventArgs e)
        {
        }

        public bool BuildProjectFile(string projectFileName, 
            string[] targetNames, IDictionary globalProperties, IDictionary targetOutputs)
        {
            return false;
        }

        public bool ContinueOnError { get; }
        public int LineNumberOfTaskNode { get; }
        public int ColumnNumberOfTaskNode { get; }
        public string ProjectFileOfTaskNode { get; }
    }
}
