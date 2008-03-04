##############################################################################
#
#   Microsoft Research Singularity
#
#   Copyright (c) Microsoft Corporation.  All rights reserved.
#
#   File:   Windows\singx86\Makefile
#
#   Note:
#
#   *** When adding extensions, create the .cpp file and an export
#       entry in singx86.def. ***
#
##############################################################################

OBJROOT=..\obj
!INCLUDE "$(SINGULARITY_ROOT)/Makefile.inc"

CFLAGS=$(CFLAGS) /DWIN32 /DNT /Oi- /I..\inc /I..\..\boot\include \
    /Fd$(OBJDIR)\singx86.pdb \

HOST_LINKFLAGS= $(HOST_LINKFLAGS) /nod /libpath:..\lib /fixed:no /subsystem:console

LIBS=\
    kernel32.lib    \
    libcmt.lib      \
    dbgeng.lib      \

OBJS=\
    $(OBJDIR)\singx86.res \
    \
    $(OBJDIR)\apic.obj \
    $(OBJDIR)\bytev.obj \
    $(OBJDIR)\charv.obj \
    $(OBJDIR)\dump.obj \
    $(OBJDIR)\help.obj \
    $(OBJDIR)\ioapic.obj \
    $(OBJDIR)\log.obj \
    $(OBJDIR)\object.obj \
    $(OBJDIR)\procs.obj \
    $(OBJDIR)\sample.obj \
    $(OBJDIR)\singx86.obj \
    $(OBJDIR)\sips.obj \
    $(OBJDIR)\stack.obj \
    $(OBJDIR)\structs.obj \
    $(OBJDIR)\thread.obj \
    $(OBJDIR)\threads.obj \

##############################################################################

.SUFFIXES: .cpp .obj

{.}.cpp{$(OBJDIR)}.obj:
    cl /c $(CFLAGS) /Fo$@ $<

##############################################################################

all: $(OBJDIR) $(OBJDIR)\singx86.dll

$(OBJDIR):
    -mkdir $(OBJDIR)

install: $(OBJDIR) $(OBJDIR)\singx86.dll
    $(SDEDIT) ..\..\build\singx86.*
    $(COPY) $(OBJDIR)\singx86.dll ..\..\build
    $(COPY) $(OBJDIR)\singx86.pdb ..\..\build

##############################################################################

clean:
    @-del /q $(OBJDIR)\singx86.* *~ 2>nul
    @-del /q $(OBJS) 2>nul
    @-rmdir $(OBJDIR) 2>nul
    @-rmdir $(OBJROOT) 2>nul

##############################################################################

$(OBJDIR)\singx86.dll : $(OBJS) singx86.def
    @echo Linking $@
    link /dll /out:$(OBJDIR)\singx86.dll /pdb:$(OBJDIR)\singx86.pdb /def:singx86.def \
        $(HOST_LINKFLAGS) \
        $(OBJS) $(LIBS)

$(OBJDIR)\singx86.res : singx86.rc
    rc /fo$(OBJDIR)\singx86.res singx86.rc

################################################################# End of File.
