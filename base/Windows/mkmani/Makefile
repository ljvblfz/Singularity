##############################################################################
#
#   Microsoft Research Singularity
#
#   Copyright (c) Microsoft Corporation.  All rights reserved.
#
#   File:   Windows\mkmani\Makefile
#
##############################################################################

OBJROOT=..\obj
!INCLUDE "$(SINGULARITY_ROOT)/Makefile.inc"

CSCFLAGS = /nologo $(CSFLAGS) /warn:2 

##############################################################################

all: $(OBJDIR) $(OBJDIR)\mkmani.exe

$(OBJDIR):
    -mkdir $(OBJDIR)

install: $(OBJDIR) $(OBJDIR)\mkmani.exe
    $(SDEDIT) ..\..\build\mkmani.*
    $(COPY) $(OBJDIR)\mkmani.exe ..\..\build
    $(COPY) $(OBJDIR)\mkmani.pdb ..\..\build


##############################################################################

clean:
    @-del /q $(OBJDIR)\mkmani.* 2>nul
    @-del /q *~ 2>nul
    @-del /q shell.manifest sb16.manifest 2>nul
    @-rmdir $(OBJDIR) 2>nul
    @-rmdir $(OBJROOT) 2>nul

##############################################################################

REFERENCES = \
    /reference:System.Xml.dll \
    /reference:System.dll \
    /reference:$(BUILDIR)\MsilReader.dll \

SOURCE_FILES = \
    ManifestBuilder.cs \
    TokenDefinition.cs \
    mkmani.cs \

##############################################################################

$(OBJDIR)\mkmani.exe: $(SOURCE_FILES)
    $(CSC) $(CSCFLAGS) $(REFERENCES) /out:$(OBJDIR)\mkmani.exe $(SOURCE_FILES)

##############################################################################

SHELL_ASSEMBLIES=\
    $(APPSDIR)\Shell.exe \
    $(APPSDIR)\Directory.Contracts.dll \
    $(APPSDIR)\Io.Contracts.dll \
    $(APPSDIR)\FileSystem.Contracts.dll \
    $(LIBSDIR)\FileSystem.Utils.dll \
    $(APPSDIR)\Corlibsg.dll \
    $(ILLSDIR)\Singularity.V1.ill \
    $(APPSDIR)\Corlib.dll \
    $(APPSDIR)\System.Compiler.Runtime.dll \
    $(APPSDIR)\Microsoft.SingSharp.Runtime.dll \
    $(APPSDIR)\ILHelpers.dll \

SB16_ASSEMBLIES=\
    $(DRIVDIR)\Sb16.exe \
    $(APPSDIR)\Directory.Contracts.dll \
    $(APPSDIR)\Io.Contracts.dll \
    $(APPSDIR)\Corlib.dll \
    $(APPSDIR)\Corlibsg.dll \
    $(ILLSDIR)\singularity.V1.ill \
    $(APPSDIR)\System.Compiler.Runtime.dll \
    $(APPSDIR)\Microsoft.SingSharp.Runtime.dll \
    $(APPSDIR)\ILHelpers.dll \

test: $(OBJDIR)\mkmani.exe $(SHELL_ASSEMBLIES)
    $(OBJDIR)\mkmani.exe \
        /out:Shell.manifest \
        /app:Shell \
        /x86:Shell.x86 \
        /cache:$(BASEDIR) \
        $(SHELL_ASSEMBLIES)
    $(OBJDIR)\mkmani.exe \
        /out:Sb16.manifest \
        /app:Sb16 \
        /x86:Sb16.x86 \
        /cache:$(BASEDIR) \
        /linker:/debug \
        /linker:/merge:ugly=data \
        /codegen:/DisableX64LirOpts=true \
        /codegen:/EnableIceCap=false \
        $(SB16_ASSEMBLIES)
    @echo.
    type Shell.manifest
    @echo.
    type Sb16.manifest

##############################################################################
