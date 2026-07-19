Option Explicit

Dim shell, fileSystem, basePath, command
Set shell = CreateObject("WScript.Shell")
Set fileSystem = CreateObject("Scripting.FileSystemObject")

basePath = fileSystem.GetParentFolderName(WScript.ScriptFullName)
command = Chr(34) & basePath & "\ensure-share.cmd" & Chr(34)
shell.Run command, 0, True
