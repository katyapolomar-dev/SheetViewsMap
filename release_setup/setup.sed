[Version]
Class=IEXPRESS
SEDVersion=3.0

[Options]
PackageTitle=Sheet Views Map v1.0.0
ShowInstallProgramWindow=1
InstallPrompt=Это установщик плагина Sheet Views Map для Revit 2021/2023. Продолжить?
DisplayLicense=0
FinishMessage=Установка завершена! Перезапустите Revit для загрузки плагина.

SourceFilesCount=6

[SourceFiles]
SourceFiles0=D:\katyacoder\SheetViewsMap\release_setup\install.cmd
SourceFiles1=D:\katyacoder\SheetViewsMap\release_setup\addins\SheetViewsMap.addin.template
SourceFiles2=D:\katyacoder\SheetViewsMap\release_setup\out\2021\SheetViewsMap2021.dll
SourceFiles3=D:\katyacoder\SheetViewsMap\release_setup\out\2021\SheetViewsMap2021.pdb
SourceFiles4=D:\katyacoder\SheetViewsMap\release_setup\out\2023\SheetViewsMap2023.dll
SourceFiles5=D:\katyacoder\SheetViewsMap\release_setup\out\2023\SheetViewsMap2023.pdb

[TargetName]
TargetName=D:\katyacoder\SheetViewsMap\release_setup\SheetViewsMap_Setup_v1.0.0.exe

[ExtractOptions]
MakeCab=1

[SetupProgramCmdLine]
SetupProgramCmdLine=install.cmd
