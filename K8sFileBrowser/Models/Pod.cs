using System.Collections.Generic;

namespace K8sFileBrowser.Models;

public class Pod
{
    public string Name { get; init; } = string.Empty;
    public IList<string> Containers { get; set; } = new List<string>();
    
    public override string ToString()
    {
        return Name;
    }
}