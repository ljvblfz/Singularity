;; trusted assembly language definitions

externdef _$$?callSiteTableCount:NEAR
externdef _$$?returnAddressToCallSiteSetNumbers:NEAR
externdef _$$?callSiteSetCount:NEAR
externdef _$$?codeBaseStartTable:NEAR
externdef _$$?activationDescriptorTable:NEAR
externdef _$$?callSiteSetNumberToIndex:NEAR
externdef _$$?sectionCount:NEAR
externdef _$$?dataSectionEnd:NEAR
externdef _$$?staticDataPointerBitMap:NEAR
externdef _$$?dataSectionBase:NEAR
externdef ?_vtable@String@System@@2UVTable@2@A:NEAR
externdef ?g_Main@Kernel@1@SIXXZ:NEAR

PUBLIC _$$StackCheck

?STRING_VTABLE EQU ?_vtable@String@System@@2UVTable@2@A

_?Throw EQU __throwDispatcher
?FatalHandler EQU _?FatalHandler
?ErrorHandler EQU _?ErrorHandler
?FaultHandler EQU _?FaultHandler
?InterruptHandler EQU _?InterruptHandler
?KernelEntryPoint EQU ?g_Main@Kernel@1@SIXXZ
_?AllocObject EQU ?g_AllocateObject@GC@System@@SIPAUObject@2@PAUVTable@2@@Z
_?AllocVector EQU ?g_AllocateVector@GC@System@@SIPAUArray@2@PAUVTable@2@H@Z
_?GarbageCollect EQU ?g_GarbageCollect@NucleusCalls@Kernel@@SIXXZ
_?GetStackState EQU ?g_GetStackState@NucleusCalls@Kernel@@SIII@Z
_?ResetStack EQU ?g_ResetStack@NucleusCalls@Kernel@@SIXI@Z
_?YieldTo EQU ?g_YieldTo@NucleusCalls@Kernel@@SIXI@Z
_?VgaTextWrite EQU ?g_VgaTextWrite@NucleusCalls@Kernel@@SIXII@Z
_?TryReadKeyboard EQU ?g_TryReadKeyboard@NucleusCalls@Kernel@@SIIXZ
_?SendEoi EQU ?g_SendEoi@NucleusCalls@Kernel@@SIXXZ
_?StartTimer EQU ?g_StartTimer@NucleusCalls@Kernel@@SIXI@Z
_?CycleCounter EQU ?g_Rdtsc@NucleusCalls@Kernel@@SI_JXZ
_?DebugPrintHex EQU ?g_DebugPrintHex@NucleusCalls@Kernel@@SIXII@Z
