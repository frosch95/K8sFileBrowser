using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using K8sFileBrowser.Models;
using Serilog;

namespace K8sFileBrowser.Services;

public class KubernetesFileInformationResult
{
    private readonly string _parent;
    public IList<FileInformation> FileInformations { get; set; } = new List<FileInformation>();

    public KubernetesFileInformationResult(string parent)
    {
        _parent = parent;
    }

    public Task ParseFileInformationCallback(Stream stdIn, Stream stdOut, Stream stdErr)
    {
        using (var stdOutReader = new StreamReader(stdOut))
        {
            var output = stdOutReader.ReadToEnd();
            
            output.Split("\n").ToList().ForEach(line =>
            {
                if (line.Length <= 0) return;
                line = line.TrimEnd('\n');
                
                var fileInformation = line.Split("|").ToList();
                FileInformations.Add(new FileInformation
                {
                    Parent = _parent,
                    Type = GetFileType(fileInformation[0]),
                    Name = fileInformation[1],
                    Size = fileInformation[2],
                    Date = GetDate(fileInformation[3])
                });
            });
            Log.Information(output);
        }
        
        using (var stdErrReader = new StreamReader(stdErr))
        {
            var output = stdErrReader.ReadToEnd();
            Log.Warning(output);
        }
        return Task.CompletedTask;
    }
    
    private static FileType GetFileType(string type)
    {
        return type switch
        {
            "directory" => FileType.Directory,
            "regular file" => FileType.File,
            _ => FileType.Unknown
        };
    }
    
    private static DateTimeOffset GetDate(string date)
    {
        var unixTime = long.Parse(date);
        return DateTimeOffset.FromUnixTimeSeconds(unixTime);
    }
}