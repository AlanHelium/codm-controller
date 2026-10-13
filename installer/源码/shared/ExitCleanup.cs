using System.Diagnostics;

namespace Burrito.Installer;

public static class ExitCleanup
{
    // One shell owns the entire deletion operation. Arguments are passed separately;
    // only exact product temp/cache folders are eligible, with all reparse points rejected.
    public static void Start(string tempCopy,bool removeCaches)
    {
        string root=Path.TrimEndingDirectorySeparator(Path.GetFullPath(tempCopy));string parent=Path.TrimEndingDirectorySeparator(Path.GetTempPath());
        if(!SafeFiles.Within(root,parent)||!System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(root),@"^CODM-Uninstall-[a-f0-9]{32}$"))return;
        SafeFiles.NoLinks(root);
        string code="""
        & {
          $cleanupRoot=$env:CODM_CLEANUP_ROOT
          $ownerPid=$env:CODM_CLEANUP_PID
          $ownerTicks=$env:CODM_CLEANUP_TICKS
          $withCaches=$env:CODM_CLEANUP_CACHES
          $cleanupBase=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
          $cleanupTarget=[IO.Path]::GetFullPath($cleanupRoot).TrimEnd('\')
          if ([IO.Path]::GetDirectoryName($cleanupTarget) -ne $cleanupBase -or [IO.Path]::GetFileName($cleanupTarget) -notmatch '^CODM-Uninstall-[a-f0-9]{32}$') { exit 1 }
          try { $owner=Get-Process -Id $ownerPid -ErrorAction Stop; if ($owner.StartTime.ToUniversalTime().Ticks -eq [long]$ownerTicks) { $owner.WaitForExit() } } catch {}
          function Remove-OwnedFolder([string]$target) {
            if (-not (Test-Path -LiteralPath $target)) { return }
            $ancestor=[IO.Path]::GetFullPath($target)
            while ($ancestor) {
              if (Test-Path -LiteralPath $ancestor) {
                $ancestorItem=Get-Item -LiteralPath $ancestor -Force -ErrorAction Stop
                if (($ancestorItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Refusing linked ancestor' }
              }
              $ancestor=[IO.Path]::GetDirectoryName($ancestor)
            }
            $queue=[Collections.Generic.Queue[string]]::new(); $queue.Enqueue($target)
            while ($queue.Count -gt 0) {
              $current=$queue.Dequeue(); $item=Get-Item -LiteralPath $current -Force -ErrorAction Stop
              if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Refusing reparse point' }
              foreach ($child in Get-ChildItem -LiteralPath $current -Force -ErrorAction Stop) {
                if (($child.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Refusing linked child' }
                if ($child.PSIsContainer) { $queue.Enqueue($child.FullName) }
              }
            }
            Remove-Item -LiteralPath $target -Recurse -Force -ErrorAction Stop
          }
          if ($withCaches -eq '1') {
            foreach ($name in @('CODMControllerProbe','CODMControllerProbeSetup','CODMControllerProbeUninstaller')) {
              $cache=[IO.Path]::GetFullPath([IO.Path]::Combine($cleanupBase,'.net',$name))
              if ($cache.StartsWith([IO.Path]::Combine($cleanupBase,'.net')+'\',[StringComparison]::OrdinalIgnoreCase)) { try { Remove-OwnedFolder $cache } catch {} }
            }
            foreach ($helper in Get-ChildItem -LiteralPath $cleanupBase -Directory -Filter 'CODMIsolation-*' -ErrorAction SilentlyContinue) {
              if ($helper.Name -match '^CODMIsolation-[a-f0-9]{32}$' -and [IO.Path]::GetDirectoryName($helper.FullName) -eq $cleanupBase) { try { Remove-OwnedFolder $helper.FullName } catch {} }
            }
          }
          Remove-OwnedFolder $cleanupTarget
        }
        """;
        var start=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),@"WindowsPowerShell\v1.0\powershell.exe")){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden};
        foreach(string arg in new[]{"-NoProfile","-NonInteractive","-Command",code})start.ArgumentList.Add(arg);
        start.Environment["CODM_CLEANUP_ROOT"]=root;start.Environment["CODM_CLEANUP_PID"]=Environment.ProcessId.ToString();start.Environment["CODM_CLEANUP_TICKS"]=Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks.ToString();start.Environment["CODM_CLEANUP_CACHES"]=removeCaches?"1":"0";
        Process.Start(start)?.Dispose();
    }
}
