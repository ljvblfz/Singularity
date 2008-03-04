##############################################################################
#
#   Microsoft Research Singularity
#
#   Copyright (c) Microsoft Corporation.  All rights reserved.
#
#   File:   Windows\Spg\Makefile
#
##############################################################################

OBJROOT=..\obj
!INCLUDE "$(SINGULARITY_ROOT)/Makefile.inc"

CSCFLAGS = /nologo $(CSFLAGS) /warn:2 /warnaserror+

SGC = sgc

SGCFLAGS = $(SGFLAGS) /nologo $(CSFLAGS) /warn:2 /warnaserror+ \


##############################################################################

all: $(OBJDIR) $(OBJDIR)\spg.exe

$(OBJDIR):
    -mkdir $(OBJDIR)

install: $(OBJDIR) $(OBJDIR)\spg.exe
    $(SDEDIT) ..\..\build\spg.exe
    $(COPY) $(OBJDIR)\spg.exe ..\..\build
    $(SDEDIT) ..\..\build\spg.pdb
    $(COPY) $(OBJDIR)\spg.pdb ..\..\build


SOURCE_FILES = \
    Generator.cs \
    HashSet.cs \
    ISet.cs \
    Parser.cs \


$(OBJDIR)\spg.exe: $(SOURCE_FILES)
    $(CSC) $(CSCFLAGS) /r:System.dll /out:$(OBJDIR)\spg.exe $**

##############################################################################

clean:
    @-del /q $(OBJDIR)\spg.* *~ 2>nul
    @-rmdir $(OBJDIR) 2>nul
    @-rmdir $(OBJROOT) 2>nul
