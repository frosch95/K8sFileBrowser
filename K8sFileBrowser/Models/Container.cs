namespace K8sFileBrowser.Models;

public class Container
{
    public string Name { get; set; } = string.Empty;
    
    public override string ToString()
    {
        return Name;
    }
    
    public override bool Equals(object? obj)
    {
        if (obj is Container container)
        {
            return Name == container.Name;
        }

        return false;
    }
    
    public override int GetHashCode() => Name.GetHashCode();
}