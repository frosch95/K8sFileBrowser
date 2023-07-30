namespace K8sFileBrowser.Models;

public class Namespace
{
    public string Name { get; set; } = string.Empty;
    
    public override string ToString()
    {
        return Name;
    }
}