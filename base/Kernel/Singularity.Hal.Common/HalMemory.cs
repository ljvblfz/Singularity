///////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   HalMemory.cs
//
//
//  Note:
//
//
//  Caution:
//

using Microsoft.Singularity.Hal.Acpi;


namespace Microsoft.Singularity.Hal
{
    using System;
    using System.Diagnostics;
    using Microsoft.Singularity.Io;

    [CLSCompliant(false)]
    internal class HalMemory : IHalMemory
    {
        // See ProcessorAffinity and MemoryAffinity
        // in the Interface (IHalMemory.cs)

        private static ProcessorAffinity[] processors;
        private static MemoryAffinity[] memories;

        private Srat srat;

        internal HalMemory(Srat srat)
        {
            this.srat = srat;
            if (srat == null) {
                processors = null;
                memories = null;
            }
            else {
                processors = new
                    ProcessorAffinity[srat.GetNumberOfProcessors()];
                memories = new
                    MemoryAffinity [srat.GetNumberOfMemories()];

                for (int i = 0; i < processors.Length; i++) {
                    processors[i].domain = srat.GetProcessorDomain(i);
                    processors[i].apicId = srat.GetProcessorApicId(i);
                    processors[i].flagIgnore = srat.GetProcessorFlagIgnore(i);
                }

                for (int i = 0; i < memories.Length; i++) {
                    memories[i].domain = srat.GetMemoryDomain(i);
                    memories[i].baseAddress = srat.GetMemoryBaseAddress(i);
                    memories[i].endAddress = srat.GetMemoryEndAddress(i);
                    memories[i].memorySize = srat.GetMemorySize(i);
                    memories[i].flagIgnore = srat.GetMemoryFlagIgnore(i);
                    memories[i].flagHotPluggable =
                        srat.GetMemoryFlagHotPluggable(i);
                    memories[i].flagNonVolatile =
                        srat.GetMemoryFlagNonVolatile(i);
                }
            }
        }

        public override ProcessorAffinity[] GetProcessorAffinity()
        {
            return processors;
        }

        public override MemoryAffinity[] GetMemoryAffinity()
        {
            return memories;
        }

        // public static void Initialize (Srat srat)
    }
}
