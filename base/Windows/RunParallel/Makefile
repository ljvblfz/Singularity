##############################################################################
#
#   Microsoft Research Singularity
#
#   Copyright (c) Microsoft Corporation.  All rights reserved.
#
#   File:   Windows\RunParallel\Makefile
#
##############################################################################

OBJROOT=..\obj
!INCLUDE "$(SINGULARITY_ROOT)/Makefile.inc"

CSCFLAGS = /nologo $(CSFLAGS) /warn:2 /unsafe

##############################################################################

all: $(OBJDIR) $(OBJDIR)\RunParallel.exe

$(OBJDIR):
    -mkdir $(OBJDIR)

copy: $(OBJDIR) $(OBJDIR)\RunParallel.exe
    $(COPY) $(OBJDIR)\RunParallel.exe ..\..\build
    $(COPY) $(OBJDIR)\RunParallel.pdb ..\..\build


install: $(OBJDIR) $(OBJDIR)\RunParallel.exe
    $(SDEDIT) ..\..\build\RunParallel.*
    $(COPY) $(OBJDIR)\RunParallel.exe ..\..\build
    $(COPY) $(OBJDIR)\RunParallel.pdb ..\..\build


##############################################################################

clean:
    @-del /q $(OBJDIR)\RunParallel.* *~ 2>nul
    @-rmdir $(OBJDIR) 2>nul
    @-rmdir $(OBJROOT) 2>nul

##############################################################################

REFERENCES = \
    /reference:System.Xml.dll \
    /reference:System.dll \


SOURCE_FILES = \
    Program.cs \
    ProgressForm.cs \
    SyncQueue.cs \
    Task.cs \
    WorkerThread.cs \
    TypeSafeWrappers.cs \
    Util.cs \
    ..\WindowsInteropLib\Kernel32.cs \
        

##############################################################################

$(OBJDIR)\RunParallel.exe: $(SOURCE_FILES) Makefile
    $(CSC) $(CSCFLAGS) $(REFERENCES) /out:$(OBJDIR)\RunParallel.exe $(SOURCE_FILES)

##############################################################################
