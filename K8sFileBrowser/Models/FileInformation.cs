using System;

namespace K8sFileBrowser.Models;

public class FileInformation
{
    public string Parent { get; set; } = string.Empty;
    public FileType Type { get; set; } = FileType.File;
    public string DisplayName => Parent.Length < 2 ? Name[Parent.Length..] : Name[( Parent.Length + 1)..];
    public string Name { get; set; } = string.Empty;
    public string Size { get; set; } = string.Empty;
    public DateTimeOffset Date { get; set; } = DateTimeOffset.MinValue;

    public bool IsFile => Type == FileType.File;
    public bool IsDirectory => Type == FileType.Directory;
    public bool IsUnknown => Type == FileType.Unknown;
}

public enum FileType
{
    Directory,
    File,
    Unknown
}