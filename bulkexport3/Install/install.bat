@echo off
setlocal
::install - makes network share setup for bulkexport service
::syntax: install.bat targetBaseFolder [options] 
::          targetBaseFolder - a public network share UNC or path to local directory
::Options:
::          /user=userName   - user name. Optional.
::          /password=pass   - user password. Should be used together with /user parameter.
::          /source=path     - dump files arrivals. Optional. If omitted try to find source automatically.
::          /target-subfolder=path      - zip files departures. Optional. If omitted try to find source automatically.
::          /max-files-in-zip=capacity  - capacity for zip archive. Optional. Default 1000
::          /clevel=0-9       - zip compression level. From 0 - None to 9 - best compression. Default 1.
::          /dry-run          - trying the parameters without causing any changes in the system.
::          /collect-recipes  - enable recipe directory watcher
::          /non-recursive    - disable recursive subdirectories watching for the source directory
::          /cleaning-interval=hours    - retention interval for bad files
set _CNT_=1
set _SEP_=/

:PARSE

if %_CNT_%==1 set _PARAM1_=%~1 & set _PARAM2_=%~2
if %_CNT_%==2 set _PARAM1_=%~2 & set _PARAM2_=%~3
if %_CNT_%==3 set _PARAM1_=%~3 & set _PARAM2_=%~4
if %_CNT_%==4 set _PARAM1_=%~4 & set _PARAM2_=%~5
if %_CNT_%==5 set _PARAM1_=%~5 & set _PARAM2_=%~6
if %_CNT_%==6 set _PARAM1_=%~6 & set _PARAM2_=%~7
if %_CNT_%==7 set _PARAM1_=%~7 & set _PARAM2_=%~8
if %_CNT_%==8 set _PARAM1_=%8 & set _PARAM2_=%9
if %_CNT_%==9 set _PARAM1_=%9 & set _PARAM2_=%~10

if "%_PARAM2_%"=="" set _PARAM2_=1

if "%_PARAM1_:~0,1%"=="%_SEP_%" (
  if "%_PARAM2_:~0,1%"=="%_SEP_%" (
    set %_PARAM1_:~1,-1%=1
    shift /%_CNT_%
  ) else (
    set %_PARAM1_:~1,-1%=%_PARAM2_%
    shift /%_CNT_%
    shift /%_CNT_%
  )
) else (
  set /a _CNT_+=1
)

if /i %_CNT_% LSS 9 goto :PARSE

set _PARAM1_=
set _PARAM2_=
set _CNT_=

rem getargs anystr1 anystr2 /test$1 /test$2=123 /test$3 str anystr3
rem set | find "test$"
rem echo %1 %2 %3 %4 %5 %6 %7 %8 %9 %source%

REM checking for empty parameters
IF [%1] == [] GOTO HELP
REM IF [%~2] == [] GOTO HELP
REM IF [%~3] == [] GOTO HELP
rem IF [%~4] == [] GOTO HELP

REM checking for service exists
REM SC HAS PROBLEMS in XP
REM SC QUERY bulkexport > NUL
REM SC query bulkexport | FIND "STATE" | FIND "RUNNING"
REM SC query bulkexport | FIND "STATE" | FIND "STOPPED"
NET START | FIND /i "bulkexport" > NUL
IF ERRORLEVEL 1 GOTO INSTALL 
GOTO END
:INSTALL

IF NOT DEFINED max-files-in-zip (SET zip=1000) ELSE (SET zip=%max-files-in-zip%)
     
REM read line and machine name
    for /f "delims=" %%a in ('dir /b /a-d /O:D C:\Sirio\Work\RecipeData\MachineConfigData') do set lastone=%%a
IF NOT DEFINED target-subfolder (
    IF NOT DEFINED lastone (
        echo Folder C:\Sirio\Work\RecipeData\MachineConfigData empty or not exists. Can't go ahead
        GOTO FIN
    )
    IF [%lastone%] == [] (
        echo lastfile error    
        GOTO FIN
    )
    echo read file C:\Sirio\Work\RecipeData\MachineConfigData\%lastone%
)
    FOR /F %%i IN ('call xpath.bat  C:\Sirio\Work\RecipeData\MachineConfigData\%lastone% /Config/@StationName') DO set name=%%i
IF NOT DEFINED target-subfolder (
    IF NOT DEFINED name (
        echo No config files in C:\Sirio\Work\RecipeData\MachineConfigData. Can't go ahead
        GOTO FIN
    )
    SET "target-subfolder=%name:System\=\%"
    SET "fa=%~1"
)
IF DEFINED collect-recipes (
    IF NOT EXIST C:\Sirio\Work\RecipeData (
        echo Directory C:\Sirio\Work\RecipeData does not exists. Can't go ahead.
        echo Remove /collect-recipes parameter or check RecipeData directory.
        GOTO FIN
    )
)
REM set "ntsh=%fa%%target%" 
REM set default values for the dump folder
IF NOT DEFINED source  (
    IF EXIST "C:\Sirio\Work\SvDumpFiles" (
      SET sourceDir="C:\Sirio\Work\SvDumpFiles"
    ) ELSE (
        IF EXIST "C:\Documents and Settings\operator\Local Settings\Temp\SvDump" (
           SET sourceDir="C:\Documents and Settings\operator\Local Settings\Temp\SvDump"
        ) ELSE (
            echo dump directory not found. Enter path to dump folder using sourceDir parameter.
            GOTO FIN
        )
    )
) ELSE SET sourceDir="%source%" 

IF DEFINED cleaning-interval (
    SET cleaner=-i%cleaning-interval%
) ELSE SET cleaner=""

echo sourceDir=%sourceDir%
REM test acces to network share 
IF DEFINED user (
        echo %user%
        IF NOT DEFINED password (
            echo Use the /password parameter along with /user.
            GOTO FIN
        )
        net use U: %1 /user:%user% %password%
        IF ERRORLEVEL 1 (
            echo Can't mount %1 to U: drive. Check %1 UNC path exists and have appropriate permissions. 
            GOTO FIN
        )
    IF NOT EXIST U:%target-subfolder% (
        mkdir U:%target-subfolder%
        IF ERRORLEVEL 1 (
            net use U: /D
            echo Can't create U:%target-subfolder%. Check %1 for appropriate permissions. 
            GOTO FIN
        )
    )
    net use U: /D
)

IF EXIST %sourceDir% (

IF DEFINED dry-run GOTO DRY
REM save user and password
    IF DEFINED user (
         echo Writing registry keys for net mapping...
         echo using %1 as net share UNC
         reg add HKLM\SYSTEM\CurrentControlSet\Services\bulkexport\Parameters /v Mapping /t REG_SZ /d "%~1" /f
         reg add HKLM\SYSTEM\CurrentControlSet\Services\bulkexport\Parameters /v User /t REG_SZ /d %user% /f
         reg add HKLM\SYSTEM\CurrentControlSet\Services\bulkexport\Parameters /v Password /t REG_SZ /d %password% /f
    )
    echo Writing registry key for compression level...
    IF DEFINED clevel (
        IF 1%clevel% GEQ 110 (
            echo clevel is incorrect
            GOTO HELP
        )
        reg add HKLM\SYSTEM\CurrentControlSet\Services\bulkexport\Parameters /v CompressionLevel /t REG_DWORD /d %clevel% /f
    )
    IF DEFINED non-recursive (
        echo Set non recursive mode
        reg add HKLM\SYSTEM\CurrentControlSet\Services\bulkexport\Parameters /v RecursiveWatching /t REG_SZ /d False /f
        IF ERRORLEVEL 1 (            
            echo Run install.bat as an administrator 
            GOTO FIN
        )
    ) ELSE (
        echo Set recursive mode
        reg add HKLM\SYSTEM\CurrentControlSet\Services\bulkexport\Parameters /v RecursiveWatching /t REG_SZ /d True /f
        IF ERRORLEVEL 1 (            
            echo Run install.bat as an administrator 
            GOTO FIN
        )
    )

     echo Install bulkexport service...
     if DEFINED user (
         echo bulkexport install %sourceDir% U:%target-subfolder% %zip% -s %cleaner%

         IF NOT DEFINED collect-recipes (
           bulkexport install %sourceDir% U:%target-subfolder% %zip% -s %cleaner%
         ) ELSE (
           bulkexport install %sourceDir% U:%target-subfolder% %zip% -s -c %cleaner%
         )
     ) ELSE (
         IF NOT DEFINED collect-recipes (
           echo bulkexport install %sourceDir% %1\%target-subfolder% %zip% -s %cleaner%
           bulkexport install %sourceDir% %1\%target-subfolder% %zip% -s %cleaner%
         ) ELSE (
           echo bulkexport install %sourceDir% %1\%target-subfolder% %zip% -s -c %cleaner%
           bulkexport install %sourceDir% %1\%target-subfolder% %zip% -s -c %cleaner%
         )
     )
) ELSE (
  echo Target directory %sourceDir% does not exist
)
GOTO FIN
:DRY
     echo Running Dry-run player... No actions take place in this mode
REM save user and password
    IF DEFINED user (
         echo Writing registry keys for net mapping...
         echo using %1 as net share UNC
         echo reg add HKLM\SYSTEM\CurrentControlSet\Services\bulkexport\Parameters /v Mapping /t REG_SZ /d "%~1" /f
         echo reg add HKLM\SYSTEM\CurrentControlSet\Services\bulkexport\Parameters /v User /t REG_SZ /d %user% /f
         echo reg add HKLM\SYSTEM\CurrentControlSet\Services\bulkexport\Parameters /v Password /t REG_SZ /d %password% /f
    )

     IF DEFINED clevel (
        IF %clevel% GEQ 10 (
            echo clevel is incorrect
            GOTO HELP
        )
        echo reg add HKLM\SYSTEM\CurrentControlSet\Services\bulkexport\Parameters /v CompressionLevel /t REG_DWORD /d %clevel% /f
     ) ELSE (
        echo reg delete HKLM\SYSTEM\CurrentControlSet\Services\bulkexport\Parameters /v CompressionLevel
     )
     echo Install bulkexport service...
     IF DEFINED user (
         echo bulkexport install %sourceDir% U:%target-subfolder% %zip% -s
     ) ELSE (
         echo bulkexport install %sourceDir% %1\%target-subfolder% %zip% -s
     )
GOTO FIN
:END
echo bulkexport service already running. Please, run uninstall.bat first to remove it.
GOTO FIN

:HELP
echo install - makes network share setup for bulkexport service
echo syntax: install.bat targetBaseFolder [options]
echo    targetBaseFolder - a public network share UNC or path to local directory. Examples: C:\output, \\net\share.
echo possible options are:
echo    /user=userName - user name.
echo    /password=pass - user password. Should be used together with /user parameter.
echo    /source=path   - dump files arrivals. If this argument is not set tries to find source automatically.
echo    /target-subfolder=path  - zip files departures. Format: \line\station-name 
echo                              or "\line\station name" if path contains spaces. If omitted try to find source 
echo                              automatically.
echo    /max-files-in-zip=capacity - capacity for zip archive. Default 1000
echo    /clevel=0-9                - zip compression level. From 0 - None to 9 - best compression. Default 1 - best speed.
echo    /dry-run                   - trying the parameters without causing any changes in the system.
echo    /collect-recipes           - enable recipe directory watcher.
echo    /non-recursive             - disable subdirectories watching for the source directory
echo    /cleaning-interval=hours   - retention interval for bad files

:FIN