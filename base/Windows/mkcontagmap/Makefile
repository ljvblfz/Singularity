##############################################################################
#
#   Microsoft Research Singularity
#
#   Copyright (c) Microsoft Corporation.  All rights reserved.
#
#   File:   Windows\mkcontagmap\Makefile
#
##############################################################################

OBJROOT=..\obj
!INCLUDE "$(SINGULARITY_ROOT)/Makefile.inc"

CSCFLAGS = /nologo $(CSFLAGS) /warn:2 

##############################################################################

all: $(OBJDIR) $(OBJDIR)\mkcontagmap.exe

$(OBJDIR):
    -mkdir $(OBJDIR)

install: $(OBJDIR) $(OBJDIR)\mkcontagmap.exe
    $(SDEDIT) ..\..\build\mkcontagmap.*
    $(COPY) $(OBJDIR)\mkcontagmap.exe ..\..\build
    $(COPY) $(OBJDIR)\mkcontagmap.pdb ..\..\build


##############################################################################

clean:
    @-del /q $(OBJDIR)\mkcontagmap.* 2>nul
    @-del /q *~ 2>nul
    @-rmdir $(OBJDIR) 2>nul
    @-rmdir $(OBJROOT) 2>nul

##############################################################################

REFERENCES = \
    /reference:System.dll \

SOURCE_FILES = \
    mkcontagmap.cs \

##############################################################################

$(OBJDIR)\mkcontagmap.exe: $(SOURCE_FILES)
    $(CSC) $(CSCFLAGS) $(REFERENCES) /out:$(OBJDIR)\mkcontagmap.exe $(SOURCE_FILES)

##############################################################################
