##############################################################################
#
#   Microsoft Research Singularity
#
#   Copyright (c) Microsoft Corporation.  All rights reserved.
#
#   File:   Windows\mkcore\Makefile
#
##############################################################################

OBJROOT=..\obj
!INCLUDE "$(SINGULARITY_ROOT)/Makefile.inc"

CFLAGS = $(CFLAGS) \
    /I..\inc /I..\..\boot\include \
    /DWIN32 /DNT /Fd$(OBJDIR)\mkcore.pdb \

HOST_LINKFLAGS = $(HOST_LINKFLAGS) \
    /nologo /nod /libpath:..\lib /fixed:no /subsystem:console

LIBS = \
    kernel32.lib    \
    libcmt.lib        \

##############################################################################

.SUFFIXES: .cpp .obj

{.}.cpp{$(OBJDIR)}.obj:
    cl /c $(CFLAGS) /Fo$@ $<

##############################################################################

all: $(OBJDIR) $(OBJDIR)\mkcore.exe

$(OBJDIR):
    -mkdir $(OBJDIR)

install: $(OBJDIR) $(OBJDIR)\mkcore.exe
    $(SDEDIT) ..\..\build\mkcore.exe
    $(SDEDIT) ..\..\build\mkcore.pdb
    $(COPY) $(OBJDIR)\mkcore.exe ..\..\build
    $(COPY) $(OBJDIR)\mkcore.pdb ..\..\build

##############################################################################

clean:
    @-del /q $(OBJDIR)\mkcore.* *.exe *.dmp *~ 2>nul
    @-rmdir $(OBJDIR) 2>nul
    @-rmdir $(OBJROOT) 2>nul

##############################################################################

test: $(OBJDIR) $(OBJDIR)\mkcore.exe
    $(COPY) ..\..\Kernel\$(OBJDIR)\system.exe system.exe
    $(OBJDIR)\mkcore.exe /v /o:system.dmp system.exe
    $(OBJDIR)\mkcore.exe /d:system.dmp

testx: $(OBJDIR) $(OBJDIR)\mkcore.exe
    $(COPY) $(OBJDIR)\mkcore.exe 1.exe
    rebase -f -b 0x100000 1.exe
    $(COPY) $(OBJDIR)\mkcore.exe 2.exe
    rebase -f -b 0x200000 2.exe
    $(COPY) $(OBJDIR)\mkcore.exe 3.exe
    rebase -f -b 0x300000 3.exe
    $(COPY) $(OBJDIR)\mkcore.exe 4.exe
    rebase -f -b 0x400000 4.exe
    $(COPY) $(OBJDIR)\mkcore.exe 5.exe
    rebase -f -b 0x500000 5.exe
    $(COPY) $(OBJDIR)\mkcore.exe 6.exe
    rebase -f -b 0x600000 6.exe
    $(COPY) $(OBJDIR)\mkcore.exe 7.exe
    rebase -f -b 0x700000 7.exe
    $(OBJDIR)\mkcore.exe /v /o:test.dmp 4.exe 1.exe 2.exe 3.exe 5.exe 6.exe 7.exe
    $(OBJDIR)\mkcore.exe /d:test.dmp

$(OBJDIR)\mkcore.exe : $(OBJDIR)\mkcore.obj
    @echo Linking $@
    link $(HOST_LINKFLAGS) /out:$@ $** $(LIBS)

$(OBJDIR)\mkcore.obj : mkcore.cpp

################################################################# End of File.
