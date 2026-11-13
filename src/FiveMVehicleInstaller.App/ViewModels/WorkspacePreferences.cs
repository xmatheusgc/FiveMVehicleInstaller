using System.IO;
using System.Text.Json;

namespace FiveMVehicleInstaller.App.ViewModels;

public enum FolderPurpose { Import, Export, Pack }
public sealed record WorkspacePreferences
{
    public string ImportDirectory { get; init; } = "";
    public string ExportDirectory { get; init; } = "";
    public string PackDirectory { get; init; } = "";
    public string[] RecentExportDirectories { get; init; } = [];
}
public interface IWorkspacePreferences
{
    WorkspacePreferences Load();
    void Save(WorkspacePreferences preferences);
}
public sealed class MemoryWorkspacePreferences : IWorkspacePreferences
{
    private WorkspacePreferences value = new();
    public WorkspacePreferences Load() => value;
    public void Save(WorkspacePreferences preferences) => value=preferences;
}
public sealed class JsonWorkspacePreferences(string? path=null) : IWorkspacePreferences
{
    private readonly string file=path??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"FiveMVehicleInstaller","workspace.json");
    public WorkspacePreferences Load()
    {
        try
        {
            if(!File.Exists(file)||new FileInfo(file).Length>1024*1024)return new();
            return JsonSerializer.Deserialize<WorkspacePreferences>(File.ReadAllText(file))??new();
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException){return new();}
    }
    public void Save(WorkspacePreferences preferences)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
        var temporary=file+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.WriteAllText(temporary,JsonSerializer.Serialize(preferences,new JsonSerializerOptions{WriteIndented=true}));File.Move(temporary,file,overwrite:true);}
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
}