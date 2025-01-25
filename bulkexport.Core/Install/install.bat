@echo off
setlocal
::install - makes network share setup for bulkexport service
::syntax: install.bat [options] 
::Options:
::          /source=path     - dump files arrivals. Optional. If omitted try to find source automatically.
::          /dry-run          - trying the parameters without causing any changes in the system.
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
rem echo %0 %1 %2 %3 %4 %5 %6 %7 %8 %9 %source%

REM checking for empty parameters
IF [%0] == [] GOTO HELP
REM IF [%~2] == [] GOTO HELP
REM IF [%~3] == [] GOTO HELP
rem IF [%~4] == [] GOTO HELP

REM checking for service exists
SC QUERY bulkexport > NUL 
IF ERRORLEVEL 1060 GOTO INSTALL 
GOTO END
:INSTALL

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
IF DEFINED non-recursive (
    echo Set non recursive mode
    SET recurSive="False"
) ELSE (
    echo Set recursive mode
    SET recurSive="True"
)
IF EXIST %sourceDir% (
IF DEFINED dry-run GOTO DRY
    rem echo Set source directory
    rem reg add HKLM\SYSTEM\CurrentControlSet\Services\bulkexport\Parameters /v Source /t REG_SZ /d "%sourceDir%" /f
    echo {"Source": %sourceDir%,"RecursiveWatching": %recurSive%} > c:\cybord_config\bulkexport-settings.json

    echo Install bulkexport service...
    echo sc.exe create bulkexport DisplayName= "Bulkexport GRPC service" binpath= "%cd%\bulkexport.exe"
    sc.exe create bulkexport DisplayName= "Bulkexport GRPC service" binpath= "%cd%\bulkexport.exe"
) ELSE (
  echo Target directory %sourceDir% does not exist
)
GOTO FIN
:DRY
     echo Running Dry-run player... No actions take place in this mode
     echo Install bulkexport service...
     echo sc.exe create "Bulkexport GRPC service" binpath= ".\bulkexport.exe"
GOTO FIN
:END
echo bulkexport service already running. Please, run uninstall.bat first to remove it.
GOTO FIN

:HELP
echo install - makes network share setup for bulkexport service
echo syntax: install.bat [options]
echo possible options are:
echo    /source=path   - dump files arrivals. If this argument is not set tries to find source automatically.
echo    /dry-run                   - trying the parameters without causing any changes in the system.
echo    /non-recursive             - disable subdirectories watching for the source directory
echo    /cleaning-interval=hours   - retention interval for bad files

:FIN