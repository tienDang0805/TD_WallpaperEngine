param([string]$Marker,[string]$Output,[string]$StopFile)
$ErrorActionPreference='Stop'
$utf8=[Text.UTF8Encoding]::new($false)
while(!(Test-Path -LiteralPath $StopFile)){
    try{
        if(!(Test-Path -LiteralPath $Marker)){Start-Sleep -Milliseconds 250;continue}
        $phase=Get-Content -LiteralPath $Marker -Raw|ConvertFrom-Json
        $taskPlayerId=[int]$phase.Pid
        if($taskPlayerId -le 0){Start-Sleep -Milliseconds 250;continue}
        $prefix="pid_${taskPlayerId}_"
        $memory=@(Get-CimInstance Win32_PerfFormattedData_GPUPerformanceCounters_GPUProcessMemory -Filter "Name LIKE 'pid_${taskPlayerId}_%'" | Where-Object {$_.Name.StartsWith($prefix)} | ForEach-Object {@{Name=$_.Name;DedicatedMiB=$_.DedicatedUsage/1MB;SharedMiB=$_.SharedUsage/1MB}})
        $engines=@(Get-CimInstance Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine -Filter "Name LIKE 'pid_${taskPlayerId}_%'" | Where-Object {$_.Name.StartsWith($prefix)} | ForEach-Object {@{Name=$_.Name;UtilizationPercent=$_.UtilizationPercentage}})
        $after=Get-Content -LiteralPath $Marker -Raw|ConvertFrom-Json
        if($after.Pid -eq $taskPlayerId -and $after.Fixture -eq $phase.Fixture -and $after.Limit -eq $phase.Limit){
            $row=@{Utc=[DateTime]::UtcNow.ToString('O');Pid=$taskPlayerId;Fixture=$phase.Fixture;Limit=$phase.Limit;Memory=$memory;Engines=$engines;Error=$null}
            [IO.File]::AppendAllText($Output,($row|ConvertTo-Json -Depth 6 -Compress)+[Environment]::NewLine,$utf8)
        }
    }catch{
        [IO.File]::AppendAllText($Output,(@{Utc=[DateTime]::UtcNow.ToString('O');Error=$_.Exception.Message}|ConvertTo-Json -Compress)+[Environment]::NewLine,$utf8)
    }
    Start-Sleep -Milliseconds 500
}