$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$encoder=Join-Path $root 'src/TD_Wallpaper.App/DownloadTools/ffmpeg.exe'
$directory=Join-Path $env:TEMP ('TD_Wallpaper-hardware-fixtures-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $directory|Out-Null
$files=@()
foreach($resolution in @(@{Name='1080p';Width=1920;Height=1080},@{Name='4K';Width=3840;Height=2160})){
    foreach($codec in @('h264','hevc','vp9','av1')){
        $extension=if($codec -in 'vp9','av1'){'webm'}else{'mp4'}
        $path=Join-Path $directory "$codec-$($resolution.Name).$extension"
        $args=@('-hide_banner','-loglevel','error','-f','lavfi','-i',"testsrc2=size=$($resolution.Width)x$($resolution.Height):rate=60",'-t','8','-an','-pix_fmt','yuv420p','-threads','6')
        switch($codec){
          h264 {$args+=@('-c:v','libx264','-preset','ultrafast','-crf','30')}
          hevc {$args+=@('-c:v','libx265','-preset','ultrafast','-crf','32','-x265-params','pools=4:frame-threads=2:log-level=error','-tag:v','hvc1')}
          vp9 {$args+=@('-c:v','libvpx-vp9','-deadline','realtime','-cpu-used','8','-row-mt','1','-crf','40','-b:v','0')}
          av1 {$args+=@('-c:v','libsvtav1','-preset','13','-crf','40','-svtav1-params','lp=6')}
        }
        $args+=@('-y',$path)
        & $encoder @args 2> (Join-Path $directory "$codec-$($resolution.Name)-encode.txt")
        if($LASTEXITCODE -ne 0){throw "Encode failed: $path"}
        $files+=@{Name="$codec-$($resolution.Name)-$extension";Codec=$codec;Width=$resolution.Width;Height=$resolution.Height;FPS=60;Path=$path;ExpectedMode='hardware';SHA256=(Get-FileHash -LiteralPath $path).Hash}
        "READY fixture $codec $($resolution.Name) $extension"
        if($extension -eq 'webm'){
            $remux=Join-Path $directory "$codec-$($resolution.Name).mp4"
            & $encoder -hide_banner -loglevel error -i $path -map 0:v:0 -c copy -y $remux
            if($LASTEXITCODE -ne 0){throw 'MP4 remux failed'}
            $files+=@{Name="$codec-$($resolution.Name)-mp4";Codec=$codec;Width=$resolution.Width;Height=$resolution.Height;FPS=60;Path=$remux;ExpectedMode='hardware';SHA256=(Get-FileHash -LiteralPath $remux).Hash}
        }
    }
}
$path=Join-Path $directory 'h264-high10-1080p.mp4'
& $encoder -hide_banner -loglevel error -f lavfi -i 'testsrc2=size=1920x1080:rate=60' -t 8 -an -c:v libx264 -preset ultrafast -crf 30 -threads 6 -pix_fmt yuv420p10le -profile:v high10 -y $path
if($LASTEXITCODE -ne 0){throw 'High10 encode failed'}
$files+=@{Name='h264-high10-1080p-mp4';Codec='h264';Width=1920;Height=1080;FPS=60;Path=$path;ExpectedMode='software';SHA256=(Get-FileHash -LiteralPath $path).Hash}
[IO.File]::WriteAllText((Join-Path $root 'artifacts/stage3-hardware-fixtures.json'),($files|ConvertTo-Json -Depth 6),[Text.UTF8Encoding]::new($false))
"Fixture directory: $directory"
exit 0