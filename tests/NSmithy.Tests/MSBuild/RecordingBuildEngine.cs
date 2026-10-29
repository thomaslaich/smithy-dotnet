using System.Collections;
using Microsoft.Build.Framework;

namespace NSmithy.Tests.MSBuild;

internal sealed class RecordingBuildEngine : IBuildEngine
{
    public List<BuildErrorEventArgs> Errors { get; } = [];
    public bool ContinueOnError => false;
    public int LineNumberOfTaskNode => 0;
    public int ColumnNumberOfTaskNode => 0;
    public string ProjectFileOfTaskNode => "";

    public bool BuildProjectFile(
        string projectFileName,
        string[] targetNames,
        IDictionary globalProperties,
        IDictionary targetOutputs
    ) => false;

    public void LogCustomEvent(CustomBuildEventArgs e) { }

    public void LogErrorEvent(BuildErrorEventArgs e) => Errors.Add(e);

    public void LogMessageEvent(BuildMessageEventArgs e) { }

    public void LogWarningEvent(BuildWarningEventArgs e) { }
}
