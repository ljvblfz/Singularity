##############################################################################
#
#   Microsoft Research Singularity
#
#   Copyright (c) Microsoft Corporation.  All rights reserved.
#
#   File:   Windows\nmake_dirs\Makefile
#
##############################################################################

OBJROOT=..\obj
!INCLUDE "$(SINGULARITY_ROOT)/Makefile.inc"

CSCFLAGS = /nologo $(CSFLAGS) /warn:2 /unsafe

##############################################################################

all: $(OBJDIR) $(OBJDIR)\nmake_dirs.exe

$(OBJDIR):
    -mkdir $(OBJDIR)

copy: $(OBJDIR) $(OBJDIR)\nmake_dirs.exe
    $(COPY) $(OBJDIR)\nmake_dirs.exe ..\..\build
    $(COPY) $(OBJDIR)\nmake_dirs.pdb ..\..\build


install: $(OBJDIR) $(OBJDIR)\nmake_dirs.exe
    $(SDEDIT) ..\..\build\nmake_dirs.*
    $(COPY) $(OBJDIR)\nmake_dirs.exe ..\..\build
    $(COPY) $(OBJDIR)\nmake_dirs.pdb ..\..\build


##############################################################################

clean:
    @-del /q $(OBJDIR)\nmake_dirs.* *~ 2>nul
    @-rmdir $(OBJDIR) 2>nul
    @-rmdir $(OBJROOT) 2>nul

##############################################################################

REFERENCES = \
    /reference:System.Xml.dll \
    /reference:System.dll \


SOURCE_FILES = \
    NMakeDirs.cs \
        

##############################################################################

$(OBJDIR)\nmake_dirs.exe: $(SOURCE_FILES) Makefile
    $(CSC) $(CSCFLAGS) $(REFERENCES) /out:$(OBJDIR)\nmake_dirs.exe $(SOURCE_FILES)

##############################################################################
