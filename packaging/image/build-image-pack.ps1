param([string]$ToolchainDirectory='C:/Strawberry/c/bin',[string]$CMake='cmake')
$ErrorActionPreference='Stop'
$imageRoot=[IO.Path]::GetFullPath($PSScriptRoot)
$repoRoot=[IO.Path]::GetFullPath((Join-Path $imageRoot '../..'))
$buildRoot=Join-Path $repoRoot '.local/image-pack'
$installRoot=Join-Path $buildRoot 'install'
$gcc=Join-Path $ToolchainDirectory 'gcc.exe';$gxx=Join-Path $ToolchainDirectory 'g++.exe'
if((& $gxx --version | Select-Object -First 1) -notmatch '13\.2\.0'){throw 'This pinned pack was built with MinGW-w64 GCC 13.2.0. Use that toolchain to reproduce it.'}
New-Item -ItemType Directory -Force $buildRoot,$installRoot | Out-Null
function Invoke-Checked([string]$Program,[string[]]$Arguments){& $Program @Arguments;if($LASTEXITCODE -ne 0){throw "$Program failed with exit code $LASTEXITCODE"}}
foreach($source in @('libde265-1.1.3','kvazaar-2.3.2','libheif-1.23.6')){Invoke-Checked 'tar' @('-xf',(Join-Path $imageRoot "source/$source.tar.gz"),'-C',$buildRoot)}
$common=@('-G','Ninja',"-DCMAKE_C_COMPILER=$gcc","-DCMAKE_CXX_COMPILER=$gxx",'-DCMAKE_BUILD_TYPE=Release',"-DCMAKE_INSTALL_PREFIX=$installRoot",'-DBUILD_SHARED_LIBS=ON')
Invoke-Checked $CMake (@('-S',(Join-Path $buildRoot 'libde265-1.1.3'),'-B',(Join-Path $buildRoot 'build-de265'))+$common+@('-DENABLE_SDL=OFF','-DENABLE_DECODER=OFF','-DENABLE_ENCODER=OFF','-DENABLE_AVX512=OFF'))
Invoke-Checked $CMake @('--build',(Join-Path $buildRoot 'build-de265'),'-j','4');Invoke-Checked $CMake @('--install',(Join-Path $buildRoot 'build-de265'))
Invoke-Checked $CMake (@('-S',(Join-Path $buildRoot 'kvazaar-2.3.2'),'-B',(Join-Path $buildRoot 'build-kvazaar'))+$common+@('-DBUILD_TESTS=OFF','-DBUILD_KVAZAAR_BINARY=OFF','-DUSE_CRYPTO=OFF'))
Invoke-Checked $CMake @('--build',(Join-Path $buildRoot 'build-kvazaar'),'-j','4');Invoke-Checked $CMake @('--install',(Join-Path $buildRoot 'build-kvazaar'))
Invoke-Checked $CMake (@('-S',(Join-Path $buildRoot 'libheif-1.23.6'),'-B',(Join-Path $buildRoot 'build-heif'))+$common+@("-DCMAKE_PREFIX_PATH=$installRoot",'-DENABLE_PLUGIN_LOADING=OFF','-DWITH_LIBDE265=ON','-DWITH_KVAZAAR=ON','-DWITH_X265=OFF','-DWITH_X264=OFF','-DWITH_OpenH264_DECODER=OFF','-DWITH_AOM_DECODER=OFF','-DWITH_AOM_ENCODER=OFF','-DWITH_GDK_PIXBUF=OFF','-DWITH_EXAMPLES=OFF','-DBUILD_TESTING=OFF','-DWITH_LIBSHARPYUV=OFF','-DWITH_DAV1D=OFF','-DWITH_RAV1E=OFF','-DWITH_SvtEnc=OFF'))
Invoke-Checked $CMake @('--build',(Join-Path $buildRoot 'build-heif'),'-j','4');Invoke-Checked $CMake @('--install',(Join-Path $buildRoot 'build-heif'))
$output=Join-Path $imageRoot 'win-x64/heic';New-Item -ItemType Directory -Force $output | Out-Null
Invoke-Checked $gxx @('-std=c++20','-O2','-municode',(Join-Path $imageRoot 'heic-helper.cpp'),"-I$installRoot/include","-L$installRoot/lib",'-lheif','-o',(Join-Path $output 'citrus-heic.exe'))
Copy-Item -Path (Join-Path $installRoot 'bin/*.dll') -Destination $output
foreach($runtime in @('libgcc_s_seh-1.dll','libstdc++-6.dll','libwinpthread-1.dll')){Copy-Item -LiteralPath (Join-Path $ToolchainDirectory $runtime) -Destination $output}
$exif=Join-Path $buildRoot 'exiftool-repack';Expand-Archive -LiteralPath (Join-Path $imageRoot 'source/exiftool-13.59_64.zip') -DestinationPath $exif -Force
$exifOutput=Join-Path $imageRoot 'win-x64/exiftool';New-Item -ItemType Directory -Force $exifOutput | Out-Null
Copy-Item -LiteralPath (Join-Path $exif 'ExifTool.exe') -Destination (Join-Path $exifOutput 'exiftool.exe')
Copy-Item -LiteralPath (Join-Path $exif 'exiftool_files') -Destination $exifOutput -Recurse -Force
Invoke-Checked (Join-Path $output 'citrus-heic.exe') @('--version')
