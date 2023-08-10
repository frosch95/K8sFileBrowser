using Nuke.Common;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.DotNet;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

class Build : NukeBuild
{
    AbsolutePath SourceDirectory => RootDirectory / "K8sFileBrowser";
    AbsolutePath OutputDirectory => RootDirectory / "output";
    AbsolutePath WinOutputDirectory => OutputDirectory / "win";
    AbsolutePath LinuxOutputDirectory => OutputDirectory / "linux";
    
    AbsolutePath ProjectFile => SourceDirectory / "K8sFileBrowser.csproj";
    
    public static int Main () => Execute<Build>(x => x.Publish);

    [Parameter("Configuration to build - Default is 'Debug' (local) or 'Release' (server)")]
    readonly Configuration Configuration = IsLocalBuild ? Configuration.Debug : Configuration.Release;
    
    [Parameter] readonly string Version = "1.0.0";

    Target Clean => _ => _
        .Before(Restore)
        .Executes(() =>
        {
            DotNetClean(s => s
                .SetOutput(OutputDirectory));
        });

    Target Restore => _ => _
        .Executes(() =>
        {
            DotNet($"restore {ProjectFile}");
            //DotNetTasks.DotNetRestore(new DotNetRestoreSettings());
        });

    
    Target PublishWin => _ => _
        .DependsOn(Clean)
        .Executes(() =>
        {
            DotNetPublish(s => s
                .SetProject(ProjectFile)
                .SetConfiguration(Configuration)
                .SetOutput(WinOutputDirectory)
                .SetSelfContained(true)
                .SetFramework("net7.0")
                .SetRuntime("win-x64")
                .SetPublishSingleFile(true)
                .SetPublishReadyToRun(true)
                .SetAuthors("Andreas Billmann")
                .SetCopyright("Copyright (c) 2023")
                .SetVersion(Version)
                .SetProcessArgumentConfigurator(_ => _
                    .Add("-p:IncludeNativeLibrariesForSelfExtract=true"))
                .EnableNoRestore());
        });
    
    Target PublishLinux => _ => _
        .DependsOn(Clean)
        .Executes(() =>
        {
            DotNetPublish(s => s
                .SetProject(ProjectFile)
                .SetConfiguration(Configuration)
                .SetOutput(LinuxOutputDirectory)
                .SetSelfContained(true)
                .SetFramework("net7.0")
                .SetRuntime("linux-x64")
                .SetPublishSingleFile(true)
                .SetPublishReadyToRun(true)
                .SetAuthors("Andreas Billmann")
                .SetCopyright("Copyright (c) 2023")
                .SetVersion(Version)
                .SetProcessArgumentConfigurator(_ => _
                    .Add("-p:IncludeNativeLibrariesForSelfExtract=true"))
                .EnableNoRestore());
        });
    
    Target Publish => _ => _
        .DependsOn(PublishWin, PublishLinux)
        .Executes(() =>
        {
        });

}
