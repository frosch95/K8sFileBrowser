
using System.IO;
using System.IO.Compression;
using Nuke.Common;
using Nuke.Common.IO;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.DotNet;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

class Build : NukeBuild
{
    [Parameter("Configuration to build - Default is 'Debug' (local) or 'Release' (server)")]
    readonly Configuration Configuration = IsLocalBuild ? Configuration.Debug : Configuration.Release;

    [Parameter] readonly string Version = "1.0.0";

    AbsolutePath SourceDirectory => RootDirectory / "K8sFileBrowser";
    AbsolutePath OutputDirectory => RootDirectory / "output";
    AbsolutePath WinOutputDirectory => OutputDirectory / "win";
    AbsolutePath LinuxOutputDirectory => OutputDirectory / "linux";

    AbsolutePath WinZip => OutputDirectory / $"K8sFileBrowser_{Version}.zip";
    AbsolutePath LinuxGz => OutputDirectory / $"K8sFileBrowser_{Version}.tgz";

    AbsolutePath ProjectFile => SourceDirectory / "K8sFileBrowser.csproj";

    readonly string ExcludedExtensions = "pdb";

    public static int Main () => Execute<Build>(x => x.Publish);



    Target Clean => _ => _
        .Before(Restore)
        .Executes(() =>
        {
            OutputDirectory.DeleteDirectory();
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
                    .Add("-p:IncludeNativeLibrariesForSelfExtract=true")));

            WinOutputDirectory.ZipTo(
                WinZip,
                filter: x => !x.HasExtension(ExcludedExtensions),
                compressionLevel: CompressionLevel.SmallestSize,
                fileMode: FileMode.CreateNew);
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
                    .Add("-p:IncludeNativeLibrariesForSelfExtract=true")));

            LinuxOutputDirectory.TarGZipTo(
                LinuxGz,
                filter: x => !x.HasExtension(ExcludedExtensions),
                fileMode: FileMode.CreateNew);
        });

    Target Publish => _ => _
        .DependsOn(PublishWin, PublishLinux)
        .Executes(() =>
        {
        });

}