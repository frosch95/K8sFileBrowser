using System;

namespace K8sFileBrowser.Models;

public class FileInformation
{
    public string Parent { get; set; } = string.Empty;
    public FileType Type { get; set; } = FileType.File;
    public string DisplayName
    {
        get
        {
            if (".." == Name) return "..";
            return Parent.Length < 2 ? Name[Parent.Length..] : Name[(Parent.Length + 1)..];
        }
    }

    public string Name { get; set; } = string.Empty;
    public string Size { get; set; } = string.Empty;
    public DateTimeOffset? Date { get; set; }

    public string DateTimeOffsetString => Date?.ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty;

    public bool IsFile => Type == FileType.File;
    public bool IsDirectory => Type == FileType.Directory;
    public bool IsUnknown => Type == FileType.Unknown;
    public bool IsSymbolicLink => Type == FileType.SymbolicLink;
}

public enum FileType
{
    Directory,
    File,
    SymbolicLink,
    Unknown
}