. .\env.ps1

$ROOT="."

. $ROOT\def.ps1

$NMAKE = "$(ls $BUILD\nmake.exe)"

runShell "cd tools\boogieasm; & `"$NMAKE`" /nologo"
runShell "cd tools\beat; & `"$NMAKE`" /nologo"
runShell "cd src\Trusted\Spec; .\build.ps1"
runShell "cd src\Checked\Nucleus; .\build.ps1"
runShell "cd src\Trusted\BootLoader\SingLdrPc; & `"$NMAKE`" /nologo"
runShell "cd src\Trusted\BootLoader\BootSectors; & `"$NMAKE`" /nologo"

$doTal = $true
$TAL_BARTOK = "..\tal\Bartok\bartok.exe"
$TAL_CHECKER = "..\tal\checker.exe"
$SPEC_INCLUDE_DIR = "src\Trusted\Spec"

if (-not (test-path $TAL_BARTOK)) { $doTal = $false }

$doGenAsm = $true

if ($doTal) {
  $doGenAsm = $false
  $SPEC_INCLUDE_DIR = "src\Trusted\Spec\Tal"
}

$ILASM = "$BUILD\ilasm\ilasm.exe"
$CSC = "$BUILD\csc.exe"
$BARTOK = ".\build\x86_x86\Bartok\DEBUG\CLR\bartok.exe"
$MANAGED_DIR = "obj\Checked\Kernel"
$MANAGED_CORLIB_DLL = "$MANAGED_DIR\NullCorLib.dll"
$MANAGED_KERNEL_EXE = "$MANAGED_DIR\Kernel.exe"
ensureDirForFile($MANAGED_CORLIB_DLL)
ensureDirForFile($MANAGED_KERNEL_EXE)
run $ILASM /dll src\Trusted\Spec\NullCorLib.il /out:$MANAGED_CORLIB_DLL
run $CSC /debug /optimize /out:$MANAGED_KERNEL_EXE src\Checked\Kernel\Kernel.cs
if ($doTal) {
  run $TAL_BARTOK /Tal=true /CompileOnly=true /GenObjFile=true  /NullRuntime=true /VerifiedRuntime=true /StackOverflowChecks=true /ABCD=false /IrInitTypeInliner=false /NoCalleeSaveRegs=true /ThrowOnInternalError=true /nullgc /centralpt /WholeProgram=true /out:$MANAGED_DIR\Kernel.obj $MANAGED_KERNEL_EXE $MANAGED_CORLIB_DLL
  run $TAL_CHECKER $MANAGED_DIR\Kernel.obj
}
elseif ($doGenAsm) {
  run     $BARTOK           /CompileOnly=true /GenObjFile=false /NullRuntime=true /VerifiedRuntime=true /StackOverflowChecks=true /ABCD=false /IrInitTypeInliner=false /NoCalleeSaveRegs=true /ThrowOnInternalError=true /nullgc /centralpt /WholeProgram=true /outdir: $MANAGED_DIR $MANAGED_KERNEL_EXE $MANAGED_CORLIB_DLL
}
else {
  run     $BARTOK           /CompileOnly=true /GenObjFile=true  /NullRuntime=true /VerifiedRuntime=true /StackOverflowChecks=true /ABCD=false /IrInitTypeInliner=false /NoCalleeSaveRegs=true /ThrowOnInternalError=true /nullgc /centralpt /WholeProgram=true /outdir: $MANAGED_DIR $MANAGED_KERNEL_EXE $MANAGED_CORLIB_DLL
}

$AS = "$BUILD\x86_x86\ml.exe"
$LINK = "$BUILD\x86_x86\link.exe"
$NUCLEUS_MS = "obj\iso_ms\safeos\nucleus.exe"
$NUCLEUS_CP = "obj\iso_cp\safeos\nucleus.exe"
ensureDirForFile($NUCLEUS_MS)
ensureDirForFile($NUCLEUS_CP)
if ($doGenAsm) {
  run $AS /c /Fo$MANAGED_DIR\Kernel.000000.obj $MANAGED_DIR\Kernel.000000.asm
  run $AS /c /Fo$MANAGED_DIR\Kernel.000001.obj $MANAGED_DIR\Kernel.000001.asm
  run $AS /c /Fo$MANAGED_DIR\Kernel.000002.obj $MANAGED_DIR\Kernel.000002.asm
  $KERNEL_OBJS = list $MANAGED_DIR\Kernel.000000.obj $MANAGED_DIR\Kernel.000001.obj $MANAGED_DIR\Kernel.000002.obj
  run $AS /c /Fo$MANAGED_DIR\labels.obj src\Trusted\Spec\labels.asm
}
else {
  $KERNEL_OBJS = list $MANAGED_DIR\Kernel.obj
  run $AS /c /Fo$MANAGED_DIR\labels.obj src\Trusted\Spec\labels-coff.asm
}
run $AS /c "/I$SPEC_INCLUDE_DIR" /Foobj\Checked\Nucleus\nucleus_ms.obj obj\Checked\Nucleus\nucleus_ms.asm
run $AS /c "/I$SPEC_INCLUDE_DIR" /Foobj\Checked\Nucleus\nucleus_cp.obj obj\Checked\Nucleus\nucleus_cp.asm
run $LINK $KERNEL_OBJS obj\Checked\Nucleus\nucleus_ms.obj $MANAGED_DIR\labels.obj "/out:$NUCLEUS_MS" "/entry:?NucleusEntryPoint" /subsystem:native /nodefaultlib /base:0x300000 /LARGEADDRESSAWARE /driver /fixed
run $LINK $KERNEL_OBJS obj\Checked\Nucleus\nucleus_cp.obj $MANAGED_DIR\labels.obj "/out:$NUCLEUS_CP" "/entry:?NucleusEntryPoint" /subsystem:native /nodefaultlib /base:0x300000 /LARGEADDRESSAWARE /driver /fixed

ensureDirForFile("obj\iso_ms\safeos\boot.ini")
ensureDirForFile("obj\iso_cp\safeos\boot.ini")
cp obj\Trusted\BootLoader\loader obj\iso_ms\
cp obj\Trusted\BootLoader\loader obj\iso_cp\
"Size=$((ls $NUCLEUS_MS).length)   Path=/safeos/nucleus.exe`n" | out-file -encoding ascii obj\iso_ms\safeos\boot.ini
"Size=$((ls $NUCLEUS_CP).length)   Path=/safeos/nucleus.exe`n" | out-file -encoding ascii obj\iso_cp\safeos\boot.ini
_cdimage -j1 -lSafeOS -bootSector obj\Trusted\BootLoader\etfs.bin -inDir obj\iso_ms -out bin\safeos_ms.iso
_cdimage -j1 -lSafeOS -bootSector obj\Trusted\BootLoader\etfs.bin -inDir obj\iso_cp -out bin\safeos_cp.iso
