$ip = '127.0.0.1'; $port = 8020
$out = Join-Path $PSScriptRoot 'usb8020-test.h264'
$log = Join-Path $PSScriptRoot 'usb8020-test-log.txt'
$phoneLog = Join-Path $PSScriptRoot 'phone-log-usb8020.txt'
"Test started $(Get-Date)" | Out-File $log -Encoding utf8
try {
  $c = New-Object System.Net.Sockets.TcpClient
  $t = $c.ConnectAsync($ip, $port)
  if (-not $t.Wait(5000)) { throw "Could not connect within 5 s" }
  "Connected to ${ip}:$port" | Add-Content $log
  $s = $c.GetStream(); $s.ReadTimeout = 1000
  $fs = [IO.File]::Create($out); $buf = New-Object byte[] 65536
  $total = 0; $first = $null; $sw = [Diagnostics.Stopwatch]::StartNew()
  while ($sw.Elapsed.TotalSeconds -lt 15) {
    $n = -1
    try { $n = $s.Read($buf, 0, $buf.Length) } catch { $n = -1 }
    if ($n -eq 0) { "Phone closed the connection at $([math]::Round($sw.Elapsed.TotalSeconds,1)) s" | Add-Content $log; break }
    if ($n -gt 0) {
      if ($first -eq $null) { $first = $sw.Elapsed.TotalSeconds }
      $fs.Write($buf, 0, $n); $total += $n
    }
  }
  $fs.Close(); $c.Close()
  "Bytes received: $total; first data after $first s" | Add-Content $log
} catch { "ERROR: $_" | Add-Content $log }
try {
  (New-Object System.Net.WebClient).DownloadString("http://192.168.0.121:5001/") | Out-File $phoneLog -Encoding utf8
  "Phone log saved" | Add-Content $log
} catch { "Phone log fetch failed: $_" | Add-Content $log }
"Done $(Get-Date)" | Add-Content $log
