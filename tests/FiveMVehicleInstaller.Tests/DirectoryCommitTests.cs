using System.Runtime.InteropServices;
using FiveMVehicleInstaller.Core;
using FiveMVehicleInstaller.Infrastructure;
using Microsoft.Win32.SafeHandles;

namespace FiveMVehicleInstaller.Tests;

public sealed class DirectoryCommitTests
{
    [Fact]
    public async Task FinalRenameSurvivesTemporaryDirectoryLockWithoutRewritingOrLeavingStaging()
    {
        using var w=new TestWorkspace(); var input=PackageFixture.WriteFolder(w,"input");
        var converter=new VehicleConverter(temporaryParent:w.PathFor("temp"));
        using var session=await converter.AnalyzeAsync([input]); var package=Assert.Single(session.Packages);
        SafeFileHandle? handle=null; Task? release=null;
        try
        {
            var result=await converter.ExportAsync(package,new("car",w.PathFor("out"),new Dictionary<string,string>()),new ImmediateProgress(p=>
            {
                if(handle is not null || p.Total==0 || p.Completed!=p.Total-1) return;
                var staging=Assert.Single(Directory.GetDirectories(w.PathFor("out"),".fv-install-*"));
                handle=CreateFile(staging,0x80000000,3,IntPtr.Zero,3,0x02000000,IntPtr.Zero);
                Assert.False(handle.IsInvalid,$"CreateFile: {Marshal.GetLastWin32Error()}");
                release=Task.Run(async()=> {await Task.Delay(200);handle.Dispose();});
            }));
            Assert.NotNull(release); await release;
            Assert.Single(Directory.GetDirectories(w.PathFor("out")));
            Assert.True(File.Exists(Path.Combine(result.ResourceDirectory,"fxmanifest.lua")));
            Assert.Equal(await File.ReadAllBytesAsync(Path.Combine(input,"data/vehicles.meta")),
                await File.ReadAllBytesAsync(Path.Combine(result.ResourceDirectory,"data/vehicles.meta")));
        }
        finally {handle?.Dispose();if(release is not null)await release;}
    }
    private sealed class ImmediateProgress(Action<ConversionProgress> callback):IProgress<ConversionProgress>
    {public void Report(ConversionProgress value)=>callback(value);}
    [DllImport("kernel32.dll",EntryPoint="CreateFileW",CharSet=CharSet.Unicode,SetLastError=true)]
    private static extern SafeFileHandle CreateFile(string name,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
}