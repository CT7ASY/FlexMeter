param([switch]$AtualizarPrograma,[string]$InnoCompiler)
$ErrorActionPreference='Stop'
$projectRoot=$PSScriptRoot
$buildDir=Join-Path $projectRoot 'build'
New-Item -ItemType Directory -Path $buildDir,(Join-Path $projectRoot 'dist') -Force | Out-Null
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if(-not(Test-Path -LiteralPath $compiler)){$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'}
$program=Join-Path $buildDir 'FlexMeter.exe'
& $compiler /nologo /target:winexe "/win32icon:$(Join-Path $projectRoot 'FlexMeter.ico')" "/win32manifest:$(Join-Path $projectRoot 'FlexMeter.manifest')" "/out:$program" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll (Join-Path $projectRoot 'FlexMeter.cs') (Join-Path $projectRoot 'FlexMeter.Features.cs')
if($LASTEXITCODE -ne 0){throw 'Falha na compilação do programa.'}
Copy-Item -LiteralPath (Join-Path $projectRoot 'FlexMeter.exe.config') -Destination $buildDir -Force
$installerBuilt=$false
if($InnoCompiler -and -not(Test-Path -LiteralPath $InnoCompiler)){throw 'Compilador Inno Setup não encontrado no caminho indicado.'}
if(-not $InnoCompiler){
 foreach($candidate in @((Join-Path $projectRoot 'tools\inno-compiler\ISCC.exe'),(Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),(Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'))){if(Test-Path -LiteralPath $candidate){$InnoCompiler=$candidate;break}}
}
if($InnoCompiler){
 & $InnoCompiler /Q (Join-Path $projectRoot 'FlexMeter-Setup.iss')
 if($LASTEXITCODE -ne 0){throw 'Falha na compilação do instalador.'}
 $installerBuilt=$true
}else{Write-Warning 'Programa compilado. Para criar o instalador, instale Inno Setup 6 ou indique -InnoCompiler com o caminho de ISCC.exe.'}
if($AtualizarPrograma){
 $target=Join-Path $projectRoot 'FlexMeter-v0.2.exe'
 $running=Get-Process -Name 'FlexMeter-v0.2' -ErrorAction SilentlyContinue | Where-Object {$_.Path -eq $target}
 if($running){throw 'Fecha o Flex Meter e repete com -AtualizarPrograma. O instalador já foi criado em dist.'}
 Copy-Item -LiteralPath $program -Destination $target -Force
}
if($installerBuilt){Write-Output 'Compilação concluída: programa em build; instalador em dist.'}else{Write-Output 'Compilação concluída: programa em build.'}

