///////////////////////////////////////////////////////////////////////////////
//
//  profile.cpp - Extension to dump profiling information.
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//

///////////////////////////////////////////////////////////////////////////////
//
// Preprocessor material

// #define TEST 1
#ifdef TEST

#include <windows.h>
#include <assert.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#define ExtOut printf

#else

#include "singx86.h"
#define assert //

#endif

#define UNUSED(x) (x) = (x);

///////////////////////////////////////////////////////////////////////////////
//
// Forward declarations

extern "C" {
    int OriginCompare(const void* a, const void* b);
    int TargetCompare(const void* a, const void* b);
}

///////////////////////////////////////////////////////////////////////////////
//
// Global variables


///////////////////////////////////////////////////////////////////////////////
//
// Utility methods
#ifdef TEST

static const char* fake_methods[] = { "foo", "bar", "puck", "po", "dizzy", "bazz", "escher", "ringo", "borix" };
static const size_t n_fake_methods = sizeof(fake_methods) / sizeof(fake_methods[0]);

ULONG64 MethodIp(ULONG64 ip)
{
    return ip;
}

void GetMethodName(ULONG64 ip, PSTR buffer, ULONG bufferLength)
{
    strncpy(buffer, fake_methods[ip], bufferLength);
}

#else // TEST

ULONG64 RoundIp(ULONG64 ip)
{
    ULONG64 displacement;
    if (g_ExtSymbols->GetNameByOffset(ip, NULL, 0, NULL, &displacement) == S_OK) {
        return (ip - displacement);
    }
    return ip;
}

void GetMethodName(ULONG64 ip, PSTR buffer, ULONG bufferLength)
{
    ULONG64 displacement;
    ULONG   bufferUsed = 0;

    int status = g_ExtSymbols->GetNameByOffset(ip, buffer, bufferLength,
                                               &bufferUsed, &displacement);
    if (status != S_OK) {
        _snprintf(buffer, bufferLength, "%I64x", ip);
        return;
    }
    else if (displacement != 0) {
        _snprintf(buffer + bufferUsed - 1, bufferLength - bufferUsed, "+0x%I64x", displacement);
    }
}
#endif // TEST

///////////////////////////////////////////////////////////////////////////////
//
// Target Node

struct TargetNode
{
    ULONG64 jumpTarget;
    int     jumpCount;

    struct TargetNode* next;
    struct TargetNode* prev;

    TargetNode(ULONG64 theJumpTarget = 0)
        : jumpTarget(theJumpTarget), jumpCount(1)
    {
        next = this;
        prev = this;
    }

    inline ULONG64 JumpTarget() const   { return jumpTarget; }
    inline int JumpCount() const        { return jumpCount; }

    static int CompareJumps(const TargetNode* a, const TargetNode* b)
    {
        if (a->jumpCount > b->jumpCount) {
            return -1;
        } else if (a->jumpCount < b->jumpCount) {
            return +1;
        }
        return 0;
    }
};

///////////////////////////////////////////////////////////////////////////////
//
// Originating Node

struct OriginNode
{
    struct OriginNode* next;
    struct OriginNode* prev;

  private:
    TargetNode sentinel;
    ULONG64  jumpOrigin;
    int      jumpCount;
    int      nodeCount;

    inline void Append(TargetNode* node)
    {
        node->next = sentinel.next;
        node->next->prev = node;
        node->prev = &sentinel;
        sentinel.next = node;
        nodeCount++;
    }

  public:
    OriginNode(ULONG64 theJumpOrigin = 0) : sentinel(0)
    {
        jumpOrigin = theJumpOrigin;
        jumpCount  = 0;
        nodeCount  = 0;

        this->next = this;
        this->prev = this;
    }

    ~OriginNode()
    {
        TargetNode* node = sentinel.next;
        while (node != &sentinel) {
            TargetNode* next = node->next;
            delete node;
            node = next;
        }
    }

    void AddJump(ULONG64 theJumpTarget)
    {
        jumpCount++;

        TargetNode* node = sentinel.next;
        TargetNode* stop = &sentinel;

        while (node != stop) {
            if (node->jumpTarget == theJumpTarget) {
                node->jumpCount++;
                return;
            }
            node = node->next;
        }
        Append(new TargetNode(theJumpTarget));
    }

    inline int JumpCount() const
    {
        return jumpCount;
    }

    inline ULONG64 JumpOrigin() const { return jumpOrigin; }

    TargetNode** CreateSortTable()
    {
        TargetNode** sortTable = new TargetNode* [nodeCount];
        int insert = 0;

        TargetNode* stop = &sentinel;
        TargetNode* node = stop->next;
        while (node != stop) {
            sortTable[insert++] = node;
            node = node->next;
        }
        assert(insert == nodeCount);
        qsort(sortTable, nodeCount, sizeof(TargetNode*), TargetCompare);
        return sortTable;
    }

    void ReleaseSortTable(TargetNode** sortTable)
    {
        delete [] sortTable;
    }

    void DisplayTargets(const char* preamble)
    {
        char name[512];

        TargetNode** sortTable = CreateSortTable();

        for (int i = 0; i < nodeCount; i++) {
            TargetNode* node = sortTable[i];
            GetMethodName(node->JumpTarget(), name, sizeof(name) - 1);
            ExtOut("%s [%d] %s\n", preamble, node->JumpCount(), name);
        }
        ReleaseSortTable(sortTable);
    }

    static int CompareJumps(const OriginNode* a, const OriginNode* b)
    {
        if (a->jumpCount> b->jumpCount) {
            return -1;
        }
        else if (a->jumpCount < b->jumpCount) {
            return +1;
        }
        return 0;
    }
};

///////////////////////////////////////////////////////////////////////////////
//
// Jump Table to store all IP jumps

class JumpTable
{
    static const int hashBins = 32;

    OriginNode hashTable[hashBins];
    int totalJumps;
    int nodeCount;

    inline int Hash(ULONG64 theJumpOrigin)
    {
        return (int) ((theJumpOrigin ^
                       (theJumpOrigin >> 12) ^
                       (theJumpOrigin >> 24)
                      ) % hashBins);
    }

  public:

    JumpTable()
        : nodeCount(0), totalJumps(0)
    {
    }

    ~JumpTable()
    {
        for (int i = 0; i < hashBins; i++) {
            OriginNode* sentinel = &hashTable[i];
            OriginNode* node = sentinel->next;
            while (node != sentinel) {
                OriginNode* next = node->next;
                delete node;
                node = next;
            }
        }
    }

    void AddJump(ULONG64 theJumpOrigin, ULONG64 theJumpTarget)
    {
        int bin = Hash(theJumpOrigin);

        OriginNode* sentinel = &hashTable[bin];
        OriginNode* node = sentinel->next;
        while (node != sentinel) {
            if (node->JumpOrigin() == theJumpOrigin) {
                node->AddJump(theJumpTarget);
                totalJumps++;
                return;
            }
            node = node->next;
        }

        node = new OriginNode(theJumpOrigin);
        nodeCount++;

        node->AddJump(theJumpTarget);
        totalJumps++;

        node->next = sentinel->next;
        node->prev = sentinel;
        node->next->prev = node;
        sentinel->next = node;
    }

    OriginNode** CreateSortTable()
    {
        // Build table to sort
        OriginNode** sortTable = new OriginNode* [nodeCount];
        int insert = 0;

        for (int i = 0; i < hashBins; i++)
        {
            OriginNode* sentinel = &hashTable[i];
            OriginNode* node = sentinel->next;
            while (node != sentinel) {
                assert(node->JumpOrigin() < 0xffffffffULL);
                sortTable[insert++] = node;
                node = node->next;
            }
        }
        qsort(sortTable, nodeCount, sizeof(OriginNode*), OriginCompare);

        for (int i = 1; i < nodeCount; i++) {
            assert(sortTable[i-1]->JumpCount() >= sortTable[i]->JumpCount());
        }

        return sortTable;
    }

    void ReleaseSortTable(OriginNode** sortTable)
    {
        delete [] sortTable;
    }

    void Display(const char* parentPreamble, const char* childPreamble)
    {
        OriginNode** sortTable = CreateSortTable();
        char name[512];

        int lastJumps = 0x7fffffff;
        for (int i = 0; i < nodeCount; i++) {
            OriginNode* node = sortTable[i];
            int nodeJumps = node->JumpCount();
            assert(nodeJumps <= lastJumps);
            lastJumps = nodeJumps;

            GetMethodName(node->JumpOrigin(), name, sizeof(name) - 1);

            ExtOut("%d. %s %s [%d calls (%.2f%%)]\n",
                   i, parentPreamble, name, nodeJumps,
                   100.0 * (float)nodeJumps / (float) totalJumps);
            node->DisplayTargets(childPreamble);
        }

        ReleaseSortTable(sortTable);
    }
};

int OriginCompare(const void* pa, const void* pb)
{
    return OriginNode::CompareJumps(*((const OriginNode**)pa),
                                    *((const OriginNode**)pb));
}

int TargetCompare(const void* pa, const void* pb)
{
    return TargetNode::CompareJumps(*(const TargetNode**)pa,
                                    *(const TargetNode**)pb);
}

#ifdef TEST

int main()
{
    JumpTable jt;
    for (int calls = 0; calls < 150; calls++) {
        int n = sizeof(fake_methods) / sizeof(fake_methods[0]);
        int s = rand() % n_fake_methods;
        int t = rand() % n_fake_methods;
        jt.AddJump((ULONG64)s, (ULONG64)t);
    }
    jt.Display("  ", "   >>");

    return 0;
}

#else

struct SampleDescriptor
{
    ULONG64 eip;
    int     count;
};

extern "C" {
    static int eip_sort(const void* a, const void* b)
    {
        SampleDescriptor* sa = (SampleDescriptor*)a;
        SampleDescriptor* sb = (SampleDescriptor*)b;

        if (sa->eip > sb->eip)
            return +1;
        if (sa->eip < sb->eip)
            return -1;
        return 0;
    }

    static int count_sort(const void* a, const void* b)
    {
        SampleDescriptor* sa = (SampleDescriptor*)a;
        SampleDescriptor* sb = (SampleDescriptor*)b;

        if (sa->count < sb->count)
            return +1;
        if (sa->count > sb->count)
            return -1;
        return 0;
    }
};

static HRESULT Usage()
{
    ExtOut("Usage:\n"
           "    !sample {options}\n"
           "    !sample clear\n"
           "Options:\n"
           "    -c       : Print caller-callee information\n"
           "    -f       : Print frequency distribution of methods\n"
           "    -k       : Print callee-caller information\n"
           "    -l       : Analyze call graph leafs only\n"
           "    -n count : Print count most recent sample stack traces\n"
           "    -t       : Print sample stack traces\n"
           "    -x       : Use exact IP values rather than rounding to method start\n"
          );

    return S_FALSE;
}

static void
ClearLog()
{
    ULONG64 address;
    int newPcLength = 0;
    if (g_ExtSymbols->GetOffsetByName("nt!Processor::pcLength", &address) == S_OK) {
        g_ExtData->WriteVirtual(address, &newPcLength, 4, NULL);
        ExtOut("Log cleared.\n");
        return;
    }
    ExtOut("Failed: Could not locate log length variable.\n");
}

static ULONG
ScanBack(ULONG64 arrayBase,
         ULONG   arrayElements,
         ULONG   pointerSize,
         ULONG   pcHead,
         ULONG   pcLength,
         ULONG   traces)
{
    // head points to start of next record, end of last record is zero
    // so skip back 2 to get to the ending eip
    ULONG pos = (pcHead + arrayElements - 2) % arrayElements;
    ULONG wrap = pos;

    while (traces != 0) {
        ULONG64 element;
        if (g_ExtData->ReadPointersVirtual(1, arrayBase + pos * pointerSize,
                                           &element) != S_OK) {
            ExtErr("Scan failed to read value.");
            return pcLength;
        }

        if (element == 0) {
            traces--;
        }

        pos = (pos + arrayElements - 1) % arrayElements;
        if (pos == wrap)
            return pcLength;
    }
    return (wrap + arrayElements - pos) % arrayElements;
}

EXT_DECL(sample) // Defines: PDEBUG_CLIENT Client, PCSTR args
{
    JumpTable foreTable;
    JumpTable backTable;
    SampleDescriptor* statsValues = NULL;

    EXT_ENTER();    // Defines: HRESULT status = S_OK;

    ULONG pointerSize = (g_ExtControl->IsPointer64Bit() == S_OK) ? 8 : 4;
    ULONG64 address = 0;
    ULONG64 samples = 0;
    ULONG type = 0;
    ULONG subtype = 0;
    ULONG64 module = 0;

    bool doExactIps     = false;
    bool doCallerCallee = false;
    bool doCalleeCaller = false;
    bool doLeavesOnly   = false;
    bool doTraces       = false;
    bool doFrequencies  = false;
    int  mostRecent = 0;

    if (_strcmpi(args, "clear") == 0) {
        ClearLog();
        goto Exit;
    }

    while (*args != '\0') {
        while (*args == ' ' || * args == '\t') {
            args++;
        }

        if (*args != '-' && *args != '/') {
            Usage();
            EXT_CHECK(~S_OK);
        }
        args++;
        switch (tolower(*args++)) {
          case 'c':
            doCallerCallee = true;
            break;
          case 'f':
            doFrequencies = true;
            break;
          case 'k':
            doCalleeCaller = true;
            break;
          case 'l':
            doLeavesOnly = true;
            break;
          case 'n':
            while ((*args == ' ' || *args == '\t') && *args != '\0') {
                args++;
            }
            mostRecent = atoi(args);
            while (*args != ' ' && *args != '\t' && *args != '\0') {
                args++;
            }
            break;
          case 't':
            doTraces = true;
            break;
          case 'x':
            doExactIps = true;
            break;
          default:
            Usage();
            EXT_CHECK(~S_OK);
        }
    }

    if (doCallerCallee == false &&
        doCalleeCaller == false &&
        doTraces       == false &&
        doFrequencies  == false) {
        Usage();
        EXT_CHECK(~S_OK);
    }

    EXT_CHECK(g_ExtSymbols->GetOffsetByName("nt!Processor::pcSamples", &address));
    EXT_CHECK(g_ExtSymbols->GetOffsetTypeId(address, &type, &module));
    EXT_CHECK(g_ExtData->ReadPointersVirtual(1, address, &samples));

    int pcHead;
    EXT_CHECK(g_ExtSymbols->GetOffsetByName("nt!Processor::pcHead", &address));
    EXT_CHECK(g_ExtData->ReadVirtual(address, &pcHead, 4, NULL));

    int pcLength;
    EXT_CHECK(g_ExtSymbols->GetOffsetByName("nt!Processor::pcLength", &address));
    EXT_CHECK(g_ExtData->ReadVirtual(address, &pcLength, 4, NULL));

    CHAR name[512];
    EXT_CHECK(g_ExtSymbols->GetTypeName(module, type, name, arrayof(name), NULL));
    ExtVerb("  pcSamples type: %s\n", name);

    int len = strlen(name);
    if (len > 3 &&
        name[len-3] == '[' &&
        name[len-2] == ']' &&
        name[len-1] == '*') {
        name[len-3] = '\0';

        EXT_CHECK(g_ExtSymbols->GetTypeId(module, name, &subtype));
        EXT_CHECK(g_ExtSymbols->GetTypeName(module, subtype, name, arrayof(name), NULL));
        ExtVerb("  pcSamples type: %s\n", name);
    }

    ULONG lengthOffset = 0;
    EXT_CHECK(g_ExtSymbols->GetFieldOffset(module, type, "overall_length", &lengthOffset));

    ULONG valuesOffset = 0;
    EXT_CHECK(g_ExtSymbols->GetFieldOffset(module, type, "values", &valuesOffset));

    ULONG maxLength = 0;
    EXT_CHECK(g_ExtData->ReadVirtual(samples + lengthOffset,
                                     &maxLength, sizeof(maxLength), NULL));

    if (mostRecent != 0) {
        pcLength = ScanBack(samples + valuesOffset, maxLength, pointerSize, pcHead, pcLength, mostRecent);
    }

    if (doTraces)
        ExtOut("<Sample Round> <DeltaTicks> <interrupt> <eip ...>\n");

    int pos = (pcHead + maxLength - pcLength) % maxLength;

    // Allocate statistics array
    statsValues = new SampleDescriptor [pcLength];
    int statsCount = 0;

    ExtOut("Starting analysis of sample log of %d entries.\n", pcLength);
    if (doLeavesOnly)
        ExtOut("Analyzing leaf nodes only.\n");

    if (doTraces) {
        ExtOut("==================================================================\n");
        ExtOut("Raw call stacks");
    }

    while (pcLength > 0)
    {
        // Get Sample number
        ULONG64 number;
        EXT_CHECK(g_ExtData->ReadPointersVirtual(1, samples + valuesOffset + pos * pointerSize, &number));
        pos = (pos + 1) % maxLength;

        // Get ticks since last sample
        ULONG64 deltaTicks;
        EXT_CHECK(g_ExtData->ReadPointersVirtual(1, samples + valuesOffset + pos * pointerSize, &deltaTicks));
        pos = (pos + 1) % maxLength;

        // Get interrupt
        ULONG64 interrupt;
        EXT_CHECK(g_ExtData->ReadPointersVirtual(1, samples + valuesOffset + pos * pointerSize, &interrupt));
        pos = (pos + 1) % maxLength;

        pcLength -= 3;

        if (doTraces) {
            ExtOut(" %10d %10d %2x",
                   (int)number, (int)deltaTicks, (int)interrupt);
        }

        // Get instruction pointers
        int doneFirst = 0;
        ULONG64 lastIp = 0;
        bool leafJumpDone = false;
        bool leafIpDone  = false;
        do {
            ULONG64 eip = 0;
            EXT_CHECK(g_ExtData->ReadPointersVirtual(1, samples + valuesOffset + pos * pointerSize, &eip));

            if (!doExactIps) {
                eip = RoundIp(eip);
            }

            if ((leafIpDone == false || doLeavesOnly == false) && eip != 0) {
                statsValues[statsCount].eip   = eip;
                statsValues[statsCount].count = 0;
                statsCount++;
                leafIpDone = true;
            }

            if (lastIp != 0 && eip != 0 &&
                (leafJumpDone == false || doLeavesOnly == false)) {
                foreTable.AddJump(lastIp, eip);
                backTable.AddJump(eip, lastIp);
                leafJumpDone = true;
            }

            lastIp = eip;

            pos = (pos + 1) % maxLength;
            pcLength -= 1;

            if (eip == 0) {
                break;
            }

            doneFirst |= 1;

            if (doTraces == false)
                continue;

            if (g_ExtSymbols->GetNameByOffset(eip, (PSTR)&name, sizeof(name), NULL, NULL) == S_OK) {
                if (doneFirst)
                    ExtOut(" <- %s", name);
                else
                    ExtOut(" %s", name);
            }
            else {
                if (doneFirst)
                    ExtOut(" <- %p", eip);
                else
                    ExtOut(" %p", eip);
            }
        } while (pcLength > 0);

        if (doTraces)
            ExtOut("\n");
    }

    if (doFrequencies) {
        ExtOut("==================================================================\n");
        ExtOut("Raw instruction hits\n");

        if (statsCount > 0) {
            // Sort instruction pointers so we can count frequency of each unique
            // value.
            qsort(statsValues, statsCount, sizeof(SampleDescriptor), eip_sort);

            int outIndex = 0;
            ULONG64 lastIp = statsValues[0].eip;
            for (int i = 0; i < statsCount; i++) {
                if (statsValues[i].eip == lastIp) {
                    statsValues[outIndex].count++;
                }
                else {
                    outIndex++;
                    statsValues[outIndex].eip = statsValues[i].eip;
                    statsValues[outIndex].count = 1;
                    lastIp = statsValues[i].eip;
                }
            }

            // Sort on frequency and display
            qsort(statsValues, outIndex, sizeof(SampleDescriptor), count_sort);
            ExtOut("Top of the pops has %d entries:\n", outIndex);

            for (int i = 0; i < outIndex; i++) {
                if (g_ExtSymbols->GetNameByOffset(statsValues[i].eip,
                                                  (PSTR)&name,
                                                  sizeof(name),
                                                  NULL, NULL) == S_OK) {
                    ExtOut("% 7d %s\n", statsValues[i].count, name);
                }
                else {
                    ExtOut("% 7d %p\n", statsValues[i].count, statsValues[i].eip);
                }
            }
        }
    }

    if (doCallerCallee) {
        ExtOut("==================================================================\n");
        ExtOut("Calls by target method to other methods:\n");
        backTable.Display("Calls made by", "       ");
    }

    if (doCalleeCaller) {
        ExtOut("==================================================================\n");
        ExtOut("Calls to target method by other methods.\n");
        foreTable.Display("Calls made to",   "       ");
    }

    // EXT_LEAVE equivalent
  Exit:
    delete [] statsValues;
    ExtRelease();
    return status;
}
#endif // TEST
