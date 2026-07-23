' OPTIMAX Silent Startup Script
Set WshShell = CreateObject("WScript.Shell")
scriptPath = WshShell.CurrentDirectory & "\Optimax.ps1"
If Not WScript.Arguments.Count = 0 Then
    scriptPath = WScript.Arguments(0)
End If
WshShell.Run "powershell.exe -ExecutionPolicy Bypass -WindowStyle Hidden -File """ & scriptPath & """ -Extreme", 0, False
