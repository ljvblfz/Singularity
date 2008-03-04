////////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   Monitoring.cpp
//
//  Note:
//

// This stuff in here provides a ring-buffer like way to organize monitoring
// buffer entries.  Interrupts are *never* disabled, all synchronization is
// done in a non-blocking fashion.
//
// You should *never* get incorrect values from the basic events, the worst
// thing that should happen is that a value is not available anymore, due to
// buffer wrap around.  Entries should never be corrupted.
//
// This buffer should be thread and MP safe.  Therefore only one buffer is
// currently used for the whole system.
//
// All events may have an additional pointer to a variably sized string in a
// second buffer.  Guarantees for data in this buffer are weaker.  You might
// not be able to retrieve data in it, if there was a buffer wrap-around or
// an very old mid-preempted entry-writing threads gets scheduled again and
// corrupts the area you just acquired.  That is, you might not be able to
// retrieve certain string, but older ones might still be accessible.
// In very rare cases you could also see corrupted data in the strings!
//
// These words of warning spoken: Such data loss should be negligible if you
// make the buffer structures large enough, and read often enough from them.

#include "hal.h"

//////////////////////////////////////////////////////////////// Image Loader.
//

#define min(a, b) ((a < b) ? a : b)

struct indexEntry {
    union {
        volatile UINT64 data;
        struct {
            volatile UINT32 lo;
            volatile UINT32 hi;
        } hi_lo;
        struct {
            // use similar types to really get a packed bitfield
            // (circumvent compiler limitation)
            volatile UINT64 index:16;   // index (lo. 16 bits)   \_ 'data' grows
            volatile UINT64 counter:48; // counter (hi. 48 bits) /  monotonically
        } val;
    };
};

struct buffer_t {
    indexEntry   head;        // head (most current) index entry
    indexEntry   tail;        // tail (oldest valid) index entry
    UINT32       flags;       // 0 == monitoring active, 1 == mon. inactive
    UINT32       entries;     // number of elements in the following arrays
    indexEntry * index;       // index array
    Struct_Microsoft_Singularity_Monitoring_LogEntry * logs; // entry array
    uint8      * txt;         // start of text area
    uint8      * txtLimit;    // end of text area
    // fixme: we need a version number here for ABA problem
    uint8      * txtCurrent;  // pointer to current position in text area
    UINT32       magic;       // just for padding to 48 byte size
};

static unsigned char cas64(void *dest, void *comp, void *exch)
{
   unsigned char retval;
   __asm {
       mov   esi, [comp];
       mov   eax, [esi + 0];
       mov   edx, [esi + 4];
       mov   esi, [exch];
       mov   ebx, [esi + 0];
       mov   ecx, [esi + 4];
       mov   esi, [dest];
       lock  cmpxchg8b [esi];
       sete  retval;
   }
   return retval;
}

static unsigned char cas(void *dest, void *comp, void *exch)
{
   unsigned char retval;
    __asm {
        mov  ecx, dest;
        mov  edx, exch;
        mov  eax, comp;
        lock cmpxchg [ecx], edx;
        sete retval;
    }
    return retval;
}

// debug version that puts a seqno in _eip.
// #define GET_EIP() \
// {                                \
//     uintptr old_seq;                     \
//     uintptr new_seq;                     \
//     UINT32 *valp = &(((buffer_t*)c_buffer)->magic);      \
//     do                               \
//     {                                \
//  old_seq = *valp;                    \
//  new_seq = old_seq + 1;                  \
//     } while (!cas(valp, (void*)old_seq, (void*)new_seq));    \
//     _eip = new_seq;                      \
// }


#define MONITORING_BUFFER_SIZE (6 * 1024 * 1024)
#define MONITORING_TEXT_SIZE   (2 * 1024 * 1024)

void
Class_Microsoft_Singularity_Monitoring::
g_Initialize()
{
#if SINGULARITY_KERNEL
    // Init. all data only in the kernel
    //Propagate information to userland to init. a (simpler) copy object there

    // layout the header structure at the start of KERNEL_MONREC_BEGIN
    g_InitPages((UIntPtr)(MONITORING_BUFFER_SIZE));
    if (c_buffer == NULL) {
        printf("Monitoring: Error getting Memory for buffer!!!\n");
        // fixme: halt system here
        return;
    }
    else {
        printf("Monitoring: Got Memory for buffer: %p\n", c_buffer);
    }

    // text stuff
    ((buffer_t *)c_buffer)->txt =
        (uint8 *) g_InitText((UIntPtr)(MONITORING_TEXT_SIZE));
    if (((buffer_t *)c_buffer)->txt == NULL) {
        printf("Monitoring: Error getting Memory for text!!!\n");
        // fixme: halt system here
        return;
    }
    else {
        printf("Monitoring: Got Memory for text: %p\n",
               ((buffer_t *)c_buffer)->txt);
    }
    ((buffer_t *)c_buffer)->txtLimit = ((buffer_t *)c_buffer)->txt +
                                       MONITORING_TEXT_SIZE;
    ((buffer_t *)c_buffer)->txtCurrent = ((buffer_t *)c_buffer)->txt;

    ((buffer_t *)c_buffer)->head.data = 0;
    ((buffer_t *)c_buffer)->tail.data = 0;
    ((buffer_t *)c_buffer)->flags = 0;
    ((buffer_t *)c_buffer)->index = (indexEntry *)(((buffer_t *)c_buffer) + 1);
    // compute the number of available events
    // fixme: one should really have ssize_t here
    int memSize = MONITORING_BUFFER_SIZE - sizeof(buffer_t);
    if (memSize < sizeof(indexEntry) + sizeof(buffer_t)) {
        printf("Error: Not enough memory for monitoring events!\n");
        // fixme: stop machine here or something
        ((buffer_t *)c_buffer)->entries = 0;
    }
    else {
        ((buffer_t *)c_buffer)->entries =
            min(0xffff, memSize / (sizeof(indexEntry) + sizeof(buffer_t)));
        printf("Info: Monitoring will use %u entries at %p, memSize = %d.\n",
               ((buffer_t *)c_buffer)->entries, c_buffer, memSize);
    }
    ((buffer_t *)c_buffer)->logs =
        (Struct_Microsoft_Singularity_Monitoring_LogEntry *)
            (((buffer_t *)c_buffer)->index + ((buffer_t *)c_buffer)->entries);

    // init. elements
    for (UINT16 i = 0; i < ((buffer_t *)c_buffer)->entries; i++) {
        // clean up indices
        ((buffer_t *)c_buffer)->index[i].val.index = i;
        ((buffer_t *)c_buffer)->index[i].val.counter = i; // fixme: 0 or i
        // cleanup timestamps in real entries as this is used to detect a
        // valid entry
        ((buffer_t *)c_buffer)->logs[i].cycleCount = 0ULL;
    }
    // init. head and tail indices
    // fixme: get this right
    ((buffer_t *)c_buffer)->head.val.index = 0;
    ((buffer_t *)c_buffer)->head.val.counter = ((buffer_t *)c_buffer)->entries;
    ((buffer_t *)c_buffer)->tail.val.index = 0;
    ((buffer_t *)c_buffer)->tail.val.counter = 0;

    // last magic values, mostly used for alignment
    ((buffer_t *)c_buffer)->magic = 0x12345678;

#elif SINGULARITY_PROCESS
    // get the address from the kernel and init. our local object
    uint8 * _buffer;

    Struct_Microsoft_Singularity_V1_Services_ProcessService::
        g_GetMonitoringHeaders(&_buffer);

    c_buffer = _buffer;
#endif
}

// Get Pointer to (hopefully dequeued) LogEntry for index into the log array
// as returned by dequeue.
Struct_Microsoft_Singularity_Monitoring_LogEntry *
Class_Microsoft_Singularity_Monitoring::
g_IndexToPointer(UINT16 index)
{
    return &((buffer_t *)c_buffer)->logs[index];
}

// fixme: care for failing dequeuing due to missing elements
UINT16
Class_Microsoft_Singularity_Monitoring::
g_Dequeue()
{
    indexEntry tail;
    indexEntry new_tail;
    UINT16 index;

    // comments are from the paper where this buffer is described:
    // "A Generalized Approach to Runtime Monitoring for Real-Time Systems",
    // Torvald Riegel, 2005, Technische Universität Dresden, Germany
    //
    // 1. The tail position is read. The technique explained in the previous
    // subsection (reading the higher, the lower, and again the higher word) can
    // be reused if the index part is not monotonically increasing because the
    // element counter part is strictly monotonically increasing and located in
    // the upper 48 bits of 64 bit wide tail position. If the first and the
    // second read of the higher word returned different values, this step is
    // executed again.
    for (;;) {
        // read atomically
        tail.hi_lo.hi = ((buffer_t *)c_buffer)->tail.hi_lo.hi;
        tail.hi_lo.lo = ((buffer_t *)c_buffer)->tail.hi_lo.lo;
        if (tail.hi_lo.hi != ((buffer_t *)c_buffer)->tail.hi_lo.hi) {
            continue;  // try again
        }

        new_tail.data = tail.data;

        // 2. The element counter part and the index part of the local copy of
        // the read position are advanced.
        //
        new_tail.val.counter++;
        new_tail.val.index++;
        if (new_tail.val.index >= ((buffer_t *)c_buffer)->entries) {
            new_tail.val.index = 0;
        }

        index = tail.val.index;

        // 3. Using a compare-and-exchange instruction, it is tried to
        // exchange the tail position in the output buffer with the
        // modified local copy. If the exchange fails because the value
        // in the buffer has been modified, the algorithm is restarted at
        // step one. If the operation succeeds, the index that was read
        // in step one returned. It points to the dequeued and thus
        // allocated output element.
        //
        if (cas64(&(((buffer_t *)c_buffer)->tail),
                  (void *)&tail.data, (void *)&new_tail.data)) {
            return index;
        }
    }
}

void
Class_Microsoft_Singularity_Monitoring::
g_Enqueue(UINT16 newElement)
{
    indexEntry head;
    indexEntry new_head;
    indexEntry index;
    indexEntry new_index;
    UINT32 i;
    int enqueued;

    do {
        // step 1
        // read atomically
        head.hi_lo.hi = ((buffer_t *)c_buffer)->head.hi_lo.hi;
        head.hi_lo.lo = ((buffer_t *)c_buffer)->head.hi_lo.lo;
        if (head.hi_lo.hi != ((buffer_t *)c_buffer)->head.hi_lo.hi) {
            continue;  // try again
        }

        i = head.val.index;

        // step 2
        index.hi_lo.hi = ((buffer_t *)c_buffer)->index[i].hi_lo.hi;
        index.hi_lo.lo = ((buffer_t *)c_buffer)->index[i].hi_lo.lo;
        if ((index.hi_lo.hi != ((buffer_t *)c_buffer)->index[i].hi_lo.hi)
            || (index.val.counter > head.val.counter)) {
            continue;  // try again
        }

        if (index.val.counter != head.val.counter) {
            // step 3
            ((buffer_t *)c_buffer)->logs[newElement].cycleCount = RDTSC();
            // step 4
            new_index.val.counter = head.val.counter;
            new_index.val.index   = newElement;
            enqueued = cas64(&(((buffer_t *)c_buffer)->index[i]),
                             (void *)&index.data, (void *)&new_index.data);
        }
        // step 5
        new_head.val.counter = head.val.counter + 1;
        new_head.val.index   = head.val.index + 1;
        if (new_head.val.index >= ((buffer_t *)c_buffer)->entries) {
            new_head.val.index = 0;
        }

        cas64(&(((buffer_t *)c_buffer)->head), (void *)&head.data,
              (void *)&new_head.data);
    } while (!enqueued);
}

void
Class_Microsoft_Singularity_Monitoring::
g_Enqueue(UINT16 newElement, UINT64 * ptr)
{
    indexEntry head;
    indexEntry new_head;
    indexEntry index;
    indexEntry new_index;
    UINT32 i;
    int enqueued;

    do {
        // step 1
        // read atomically
        head.hi_lo.hi = ((buffer_t *)c_buffer)->head.hi_lo.hi;
        head.hi_lo.lo = ((buffer_t *)c_buffer)->head.hi_lo.lo;
        if (head.hi_lo.hi != ((buffer_t *)c_buffer)->head.hi_lo.hi) {
            continue;  // try again
        }

        i = head.val.index;

        // step 2
        index.hi_lo.hi = ((buffer_t *)c_buffer)->index[i].hi_lo.hi;
        index.hi_lo.lo = ((buffer_t *)c_buffer)->index[i].hi_lo.lo;
        if ((index.hi_lo.hi != ((buffer_t *)c_buffer)->index[i].hi_lo.hi)
            || (index.val.counter > head.val.counter)) {
            continue;  // try again
        }

        if (index.val.counter != head.val.counter) {
            // step 3
            *ptr = RDTSC();
            ((buffer_t *)c_buffer)->logs[newElement].cycleCount = *ptr;
            // step 4
            new_index.val.counter = head.val.counter;
            new_index.val.index   = newElement;
            enqueued = cas64(&(((buffer_t *)c_buffer)->index[i]),
                             (void *)&index.data, (void *)&new_index.data);
        }

        // step 5
        new_head.val.counter = head.val.counter + 1;
        new_head.val.index   = head.val.index + 1;
        if (new_head.val.index >= ((buffer_t *)c_buffer)->entries) {
            new_head.val.index = 0;
        }

        cas64(&(((buffer_t *)c_buffer)->head), (void *)&head.data,
              (void *)&new_head.data);
    } while (!enqueued);
}


void
Class_Microsoft_Singularity_Monitoring::
g_Finalize()
{
}

uint8 *
Class_Microsoft_Singularity_Monitoring::
g_CompareExchange(uint8 **dest, uint8 *exch, uint8 *comp)
{
    uint8 *val;
    __asm {
        mov ecx, dest;
        mov edx, exch;
        mov eax, comp;
        lock cmpxchg [ecx], edx;
        mov val, eax;
    }
    return val;
}


Struct_Microsoft_Singularity_Monitoring_LogEntry *
Class_Microsoft_Singularity_Monitoring::
g_CompareExchange(Struct_Microsoft_Singularity_Monitoring_LogEntry **dest,
                Struct_Microsoft_Singularity_Monitoring_LogEntry *exch,
                Struct_Microsoft_Singularity_Monitoring_LogEntry *comp)
{
    Struct_Microsoft_Singularity_Monitoring_LogEntry * val;
    __asm {
        mov ecx, dest;
        mov edx, exch;
        mov eax, comp;
        lock cmpxchg [ecx], edx;
        mov val, eax;
    }
    return val;
}

void Class_Microsoft_Singularity_Monitoring::
g_Log(uint16 provider, uint16 type, uint16 version,
      uint32 a0, uint32 a1, uint32 a2, uint32 a3, uint32 a4)
{
    // fixme: should we check for c_buffer != 0 ???

    if (! g_isActive()) {
        return;
    }

    uintptr _eip;
    __asm {
        mov eax, [ebp+4];
        mov _eip, eax;
    }

    UINT16 index;
    Struct_Microsoft_Singularity_Monitoring_LogEntry * p;

    Struct_Microsoft_Singularity_X86_ThreadContext *threadContext =
        Class_Microsoft_Singularity_Processor::g_GetCurrentThreadContext();

    int cpuid = Class_Microsoft_Singularity_Processor::
                    g_GetCurrentProcessorContext()->cpuId;

    // get buffer
    index = g_Dequeue();
    p = g_IndexToPointer(index);

    // fill it
    p->eip       = _eip;
    p->provider  = provider;
    p->type      = type;
    p->text      = NULL;
    p->cpu       = cpuid;
    p->version   = version;
    p->arg0      = a0;
    p->arg1      = a1;
    p->arg2      = a2;
    p->arg3      = a3;
    p->arg4      = a4;
    p->processId = threadContext->processId;
#ifdef  SINGULARITY_KERNEL
    p->threadId  = threadContext->threadIndex;
#else
    p->threadId  = threadContext->kernelThreadIndex;
#endif

    // return buffer
    g_Enqueue(index);

//    UINT32 d_ret;
//    d_ret = g_ConsistencyCheck();
//    if (d_ret) {
//        Class_Microsoft_Singularity_DebugStub::g_Break();
//    }
}

void Class_Microsoft_Singularity_Monitoring::
g_Log(uint16 provider, uint16 type)
{
    // fixme: should we check for c_buffer != 0 ???  Right now we assume to
    //        only get called after init.

    if (! g_isActive()) {
        return;
    }

    uintptr _eip;
    __asm {
        mov eax, [ebp+4];
        mov _eip, eax;
    }

    UINT16 index;
    Struct_Microsoft_Singularity_Monitoring_LogEntry * p;

    Struct_Microsoft_Singularity_X86_ThreadContext *threadContext =
        Class_Microsoft_Singularity_Processor::g_GetCurrentThreadContext();

    int cpuid = Class_Microsoft_Singularity_Processor::
                    g_GetCurrentProcessorContext()->cpuId;

    // get buffer
    index = g_Dequeue();
    p = g_IndexToPointer(index);

    // fill it
    p->eip       = _eip;
    p->provider  = provider;
    p->type      = type;
    p->text      = NULL;
    p->cpu       = cpuid;
    p->version   = 0;
    p->arg0      = 0;
    p->arg1      = 0;
    p->arg2      = 0;
    p->arg3      = 0;
    p->arg4      = 0;
    p->processId = threadContext->processId;
#ifdef  SINGULARITY_KERNEL
    p->threadId  = threadContext->threadIndex;
#else
    p->threadId  = threadContext->kernelThreadIndex;
#endif

    // return buffer
    g_Enqueue(index);

//    UINT32 d_ret;
//    d_ret = g_ConsistencyCheck();
//    if (d_ret) {
//        Class_Microsoft_Singularity_DebugStub::g_Break();
//    }
}

void Class_Microsoft_Singularity_Monitoring::
g_Log(uint16 provider, uint16 type, Class_System_String *s)
{
    // fixme: should we check for c_buffer != 0 ???  Right now we assume to
    //        only get called after init.

    if (! g_isActive()) {
        return;
    }

    uintptr _eip;
    __asm {
        mov eax, [ebp+4];
        mov _eip, eax;
    }

    UINT16 index;
    Struct_Microsoft_Singularity_Monitoring_LogEntry * p;

    Struct_Microsoft_Singularity_X86_ThreadContext *threadContext =
        Class_Microsoft_Singularity_Processor::g_GetCurrentThreadContext();

    int cpuid = Class_Microsoft_Singularity_Processor::
                    g_GetCurrentProcessorContext()->cpuId;

    // get buffer
    index = g_Dequeue();
    p = g_IndexToPointer(index);

    // fill it
    p->eip       = _eip;
    p->provider  = provider;
    p->type      = type;
    p->cpu       = cpuid;
    p->version   = 0;
    p->arg0      = 0;
    p->arg1      = 0;
    p->arg2      = 0;
    p->arg3      = 0;
    p->arg4      = 0;
    p->processId = threadContext->processId;
#ifdef  SINGULARITY_KERNEL
    p->threadId  = threadContext->threadIndex;
#else
    p->threadId  = threadContext->kernelThreadIndex;
#endif
    // care for the string now ...

    bool enabled = Class_Microsoft_Singularity_Processor::g_DisableInterrupts();
    // reserve space for one 64 bit counter, 32 bit for the string length and
    // the string itself
    uint8 * newTxt;
    uint8 * oldTxt;

//    Class_Microsoft_Singularity_DebugStub::g_Break();

    // update txtCurrent, align to 8 byte
    do {
        oldTxt = ((buffer_t *)c_buffer)->txtCurrent;
        // well, this is ugly but /W3 seems to prevent bitmasking (uint8 *) ...
        newTxt = (uint8 *)((unsigned)
                     ((oldTxt + sizeof(UINT64) + sizeof(UINT32) +
                      s->m_stringLength + 7)) & ((unsigned)(-1) - 7));
        if (newTxt >= ((buffer_t *)c_buffer)->txtLimit) {
            newTxt = (uint8 *)((unsigned)
                         (((buffer_t *)c_buffer)->txt + sizeof(UINT64) +
                          sizeof(UINT32) + s->m_stringLength + 7) &
                          ((unsigned)(-1) - 7));
        }
    } while (! cas(&((buffer_t *)c_buffer)->txtCurrent, oldTxt, newTxt));

    *(oldTxt + sizeof(UINT64)) = s->m_stringLength;          // write length
    g_AddText(oldTxt + sizeof(UINT64) + sizeof(UINT32), s);  // write string
    p->text = oldTxt;                                 // string ptr -> event

    // return buffer
    // also sets counter in string, *before* releasing event
    g_Enqueue(index, (UINT64 *)oldTxt);
    Class_Microsoft_Singularity_Processor::g_RestoreInterrupts(enabled);

//    UINT32 d_ret;
//    d_ret = g_ConsistencyCheck();
//    if (d_ret) {
//        Class_Microsoft_Singularity_DebugStub::g_Break();
//    }
}

int Class_Microsoft_Singularity_Monitoring::
g_FillLogEntry(Struct_Microsoft_Singularity_Monitoring_LogEntry * log,
               UINT64 * min_counter)
{
    // fixme: should we check for c_buffer != 0 ???, probably not necessary,
    //        Initialize() should have been called long before first request
    //        here, right?

    indexEntry tail;
    indexEntry head;
    indexEntry ie;
    UINT16 index;

//    UINT32 d_ret;
//    d_ret = g_ConsistencyCheck();
//    if (d_ret) {
//        Class_Microsoft_Singularity_DebugStub::g_Break();
//    }

    // get tail pointer first
    do {
        tail.hi_lo.hi = ((buffer_t *)c_buffer)->tail.hi_lo.hi;
        tail.hi_lo.lo = ((buffer_t *)c_buffer)->tail.hi_lo.lo;
    } while (tail.hi_lo.hi != ((buffer_t *)c_buffer)->tail.hi_lo.hi);

    for (;;) {

        // check if user wants valid entry, no? give him the oldest valid one
        if (*min_counter < tail.val.counter) {
            *min_counter = tail.val.counter;
        }

        // check for too fast request polling
        do {
            head.hi_lo.hi = ((buffer_t *)c_buffer)->head.hi_lo.hi;
            head.hi_lo.lo = ((buffer_t *)c_buffer)->head.hi_lo.lo;
        } while (head.hi_lo.hi != ((buffer_t *)c_buffer)->head.hi_lo.hi);
        // consumer requests counter in the future
        if (*min_counter >= head.val.counter) {
            return -1;
        }

        // now get the index entry atomically
        index = (UINT16)(*min_counter % ((buffer_t *)c_buffer)->entries);
        do {
            ie.hi_lo.hi = ((buffer_t *)c_buffer)->index[index].hi_lo.hi;
            ie.hi_lo.lo = ((buffer_t *)c_buffer)->index[index].hi_lo.lo;
        } while (ie.hi_lo.hi != ((buffer_t *)c_buffer)->index[index].hi_lo.hi);

        // if index is newer than expected, try again
        if (ie.val.counter != *min_counter) {
            continue;
        }

        // now copy the whole thing
        *log = ((buffer_t *)c_buffer)->logs[ie.val.index];

        // get tail pointer again
        do {
            tail.hi_lo.hi = ((buffer_t *)c_buffer)->tail.hi_lo.hi;
            tail.hi_lo.lo = ((buffer_t *)c_buffer)->tail.hi_lo.lo;
        } while (tail.hi_lo.hi != ((buffer_t *)c_buffer)->tail.hi_lo.hi);

        // check if we were overtaken
        if (tail.val.counter <= *min_counter) {
            break;  // finally, we got it ...
        }
    }

//    d_ret = g_ConsistencyCheck();
//    if (d_ret) {
//        Class_Microsoft_Singularity_DebugStub::g_Break();
//    }

    return 0;
}

int Class_Microsoft_Singularity_Monitoring::
g_FillTextEntry(uint8 * src, UINT64 counter, uint8 * dst, int max_size)
{
    uint8 * old_src = src;

    if ((src < ((buffer_t *)c_buffer)->txt) ||
        (src + sizeof(UINT64) + sizeof(UINT32) >
            ((buffer_t *)c_buffer)->txtLimit)) {
        return -2;  // memory bounds error
    }

    // 1. get size
    // fixme: make another bounds check here
    int len = *(UINT32 *)(src + sizeof(UINT64));

    // fixme: create a memory barrier here

    // 2. check TSC, -> bail out if wrong
    volatile UINT64 _counter = *(UINT64 *)(src);
    if (_counter != counter) {
        return -1;  // corrupt data
    }

    src += sizeof(UINT64) + sizeof(UINT32);

    // 3. copy string
    int i;
    for (i = 0; i < max_size && i < len; i++) {
        *dst++ = *src++;
    }

    // 4. check TSC again, -> bail out if wrong
    _counter = *(UINT64 *)(old_src);
    if (_counter != counter) {
        return -3;  // corrupt data
    }
    return i;
}

// Copies a given string to a memory area by stripping every second byte,
// UTF16 -> ASCII conversion, assuming there where no control characters in the
// UTF16 string ...
uint8 * Class_Microsoft_Singularity_Monitoring::
g_AddText(uint8 *dst, Class_System_String *arg)
{
    bartok_char *src = &arg->m_firstChar;
    bartok_char *end = src + arg->m_stringLength;

    while (src < end) {
        *dst++ = (uint8)*src++;
    }
//    *dst++ = '\0';

    return dst;
}

bool Class_Microsoft_Singularity_Monitoring::
g_isActive()
{
    return ((buffer_t *)c_buffer)->flags == 0;
}

void Class_Microsoft_Singularity_Monitoring::
g_setActive(bool active)
{
    if (active) {
        ((buffer_t *)c_buffer)->flags = 0;
    }
    else {
        ((buffer_t *)c_buffer)->flags = 1;
    }
}


void Class_Microsoft_Singularity_Monitoring::
g_DebugTest(UINT64 * h_ts, UINT64 * t_ts, UINT64 *min, UINT64 * max)
{
    // output head and tail counters
#if 0
#if SINGULARITY_KERNEL
    printf("Head (%llu, %llu), Tail (%llu, %llu)\n",
           ((buffer_t *)c_buffer)->head.val.index, ((buffer_t *)c_buffer)->head.val.counter,
           ((buffer_t *)c_buffer)->tail.val.index, ((buffer_t *)c_buffer)->tail.val.counter);
#else
    Class_Microsoft_Singularity_DebugStub::Print("Head (" + ((buffer_t *)c_buffer)->head.val.index +
                           ", " + ((buffer_t *)c_buffer)->head.val.counter +
                           "), Tail (" + ((buffer_t *)c_buffer)->tail.val.index +
                           ", " + ((buffer_t *)c_buffer)->tail.val.counter) + ")\n");

#endif
#endif
    // output head and tail elements
    UINT64 ih, it;
    UINT64 ih2, it2;

    ih = (((buffer_t *)c_buffer)->head.val.counter - 1) % ((buffer_t *)c_buffer)->entries;
    it = (((buffer_t *)c_buffer)->tail.val.counter)     % ((buffer_t *)c_buffer)->entries;

    ih2 = ((buffer_t *)c_buffer)->index[ih].val.index;
    it2 = ((buffer_t *)c_buffer)->index[it].val.index;

#if 0
#if SINGULARITY_KERNEL
    printf("TS: Head-1 (%llu), Tail (%llu)\n",
           ((buffer_t *)c_buffer)->logs[ih2].cycleCount,
           ((buffer_t *)c_buffer)->logs[it2].cycleCount);
#else
    Class_Microsoft_Singularity_DebugStub::Print("TS: Head-1 (" + ((buffer_t *)c_buffer)->logs[ih2].cycleCount +
                    "), Tail (" + ((buffer_t *)c_buffer)->logs[it2].cycleCount + ")\n");
#endif
#endif

    *h_ts = ((buffer_t *)c_buffer)->logs[ih2].cycleCount;
    *t_ts = ((buffer_t *)c_buffer)->logs[it2].cycleCount;

    // show min and max ts + its index
    UINT32 i, j;
    UINT64 min_ts = 100000000000ULL, max_ts = 0, ts;
    for (i = ((buffer_t *)c_buffer)->tail.val.counter;
         i < ((buffer_t *)c_buffer)->head.val.counter;) {
        j = i % ((buffer_t *)c_buffer)->entries;
        ts = ((buffer_t *)c_buffer)->logs[((buffer_t *)c_buffer)->index[j].val.index].cycleCount;

        if (ts < min_ts) {
            min_ts = ts;
        }
        if (ts > max_ts) {
            max_ts = ts;
        }

        // advance i
        i++;
        if (i >= ((buffer_t *)c_buffer)->entries) {
            i = 0;
        }
    }
#if 0
#if SINGULARITY_KERNEL
    printf("TS: Min (%llu), Max (%llu)\n", min_ts, max_ts);
#else
    Class_Microsoft_Singularity_DebugStub::Print("TS: Min (" + min_ts + "), Max (" +max_ts  + ")\n");
#endif
#endif
    *min = min_ts;
    *max = max_ts;

}

uint32 Class_Microsoft_Singularity_Monitoring::
g_ConsistencyCheck()
{
    bool id;

    id = Class_Microsoft_Singularity_Processor::g_DisableInterrupts();

    // run through all indices and check the TSs of the log entries for
	// ascending order
    uint64 i;
    uint32 j;
    uint32 k = 0;
    uint64 old_ts = 0;
    uint64 ts;
    for (i = ((buffer_t *)c_buffer)->tail.val.counter;
         i < ((buffer_t *)c_buffer)->head.val.counter;
         i++, k++) {
        j = (UINT32)(i % ((buffer_t *)c_buffer)->entries);
        ts = ((buffer_t *)c_buffer)->logs[((buffer_t *)c_buffer)->index[j].val.index].cycleCount;

        if (ts < old_ts && old_ts != 0 && ts != 0) {
            ((buffer_t *)c_buffer)->magic = (UINT32)i;
            Class_Microsoft_Singularity_DebugStub::g_Break();
            Class_Microsoft_Singularity_Processor::g_RestoreInterrupts(id);
            return (UINT32)i;
        }
        old_ts = ts;
        if (k > 100000) {
            Class_Microsoft_Singularity_Processor::g_RestoreInterrupts(id);
            Class_Microsoft_Singularity_DebugStub::g_Break();
            return 0;
        }
    }

    Class_Microsoft_Singularity_Processor::g_RestoreInterrupts(id);

    return 0;  // everything is fine!
}

//
///////////////////////////////////////////////////////////////// End of File.
