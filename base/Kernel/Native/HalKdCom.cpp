//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  halkd.cpp: runtime support for debugging
//
//  For more information see:
//      \nt\base\ntos\kd64
//      \nt\base\boot\kdcom
//      \nt\base\boot\kd1394
//      \nt\base\boot\kdusb2
//      \nt\sdktools\debuggers\ntsd64
//
#include "hal.h"
#include "halkd.h"

extern "C" void *  __cdecl memcpy(void *, const void *, size_t);
extern "C" void *  __cdecl memset(void *, int, size_t);

//
// Debugger Debugging
//
#define KDDBG if (0) kdprintf
#define KDDBG2 if (0) kdprintf

//
#define CP_GET_SUCCESS  0
#define CP_GET_NODATA   1
#define CP_GET_ERROR    2

////////////////////////////////////////////////////////// COM PORT Constants.
//
#define COM1_PORT           0x03f8
#define COM2_PORT           0x02f8
#define COM3_PORT           0x03e8
#define COM4_PORT           0x02e8

#define COM_DAT             0x00
#define COM_IEN             0x01    // interrupt enable register
#define COM_FCR             0x02    // FIFO Control Register
#define COM_LCR             0x03    // line control registers
#define COM_MCR             0x04    // modem control reg
#define COM_LSR             0x05    // line status register
#define COM_MSR             0x06    // modem status register
#define COM_SCR             0x07    // scratch register
#define COM_DLL             0x00    // divisor latch least sig
#define COM_DLM             0x01    // divisor latch most sig

const UINT16 BaudRate = 1;          // 115200 bps

#define COM_DATRDY          0x01
#define COM_OUTRDY          0x20

#define LC_DLAB             0x80

#define CLOCK_RATE          0x1C200 // USART clock rate

#define MC_DTRRTS           0x03    // Control bits to assert DTR and RTS
#define MS_DSRCTSCD         0xB0    // Status bits for DSR, CTS and CD
#define MS_CD               0x80

#define SERIAL_MCR_LOOP     0x10    // enables loopback testing mode
#define SERIAL_MCR_OUT1     0x04    // general purpose output.
#define SERIAL_MSR_CTS      0x10    // (complemented) state of clear to send (CTS).
#define SERIAL_MSR_DSR      0x20    // (complemented) state of data set ready (DSR).
#define SERIAL_MSR_RI       0x40    // (complemented) state of ring indicator (RI).
#define SERIAL_MSR_DCD      0x80    // (complemented) state of data carrier detect (DCD).

//
// Globals
//
static UINT16 KdBasePort = COM2_PORT;
static ULONG KdCompPacketIdExpected = 0;
static ULONG KdCompNextPacketIdToSend = 0;
static BOOL  KdStateChange64Sent = FALSE;

////////////////////////////////////////////////// Serial Port Input & Output.
//
static UINT8 KdReadInt8(UINT16 port)
{
    __asm {
        mov eax,0;
        mov dx,port;
        in al,dx;
    }
}

static void KdWriteInt8(UINT16 port, UINT8 value)
{
    __asm {
        mov dx,port;
        mov al,value;
        out dx,al;
    }
}

// http://byterunner.com/16550.html

bool KdpComInit(Struct_Microsoft_Singularity_BootInfo *bi)
// Initializes the communication port (baud rate, parity etc.)
{
    KdBasePort = bi->DebugBasePort;
    if (KdBasePort < 0x100) {
        KdBasePort = 0;
        return FALSE;
    }
    KdCompNextPacketIdToSend = INITIAL_PACKET_ID | SYNC_PACKET_ID;
    KdCompPacketIdExpected = INITIAL_PACKET_ID;

    // turn off interrupts
    KdWriteInt8(KdBasePort + COM_LCR, 0x00);
    KdWriteInt8(KdBasePort + COM_IEN, 0x00);

    // Turn on DTS/RTS
    KdWriteInt8(KdBasePort + COM_MCR, MC_DTRRTS); // Needed for VirtualPC PIPE/Serial

    // Turn on FIFO
    //KdWriteInt8(KdBasePort + COM_FCR, 1);

    // Set the baud rate
    KdWriteInt8(KdBasePort + COM_LCR, LC_DLAB);  // Divisor latch access bit
    KdWriteInt8(KdBasePort + COM_DLM, (UINT8)(BaudRate >> 8));
    KdWriteInt8(KdBasePort + COM_DLL, (UINT8)(BaudRate & 0xFF));

    // initialize the LCR
    KdWriteInt8(KdBasePort + COM_LCR, 0x03);
    // 8 data bits, 1 stop bit, no parity, no break

    // See if the 16450/16550 scratch register is available.
    // If not, we'll assume the serial port doesn't really exist.
    KdWriteInt8(KdBasePort + COM_SCR, 0xff);
    UINT8 a1 = KdReadInt8(KdBasePort + COM_SCR);
    KdWriteInt8(KdBasePort + COM_SCR, 0x00);
    UINT8 a2 = KdReadInt8(KdBasePort + COM_SCR);

    return (bool) ((a1 == (UINT8)0xff) && (a2 == (UINT8)0x00));
}

//
// Define wait timeout value.
//
#define TIMEOUT_COUNT 1024 * 10
// #define TIMEOUT_COUNT 1024 * 200
//#define TIMEOUT_COUNT 15

static KDP_STATUS CpGetByte(OUT PUCHAR Input, BOOL WaitForByte)
{
    UCHAR lsr;
    UCHAR value;
    ULONG limitcount = WaitForByte ? TIMEOUT_COUNT : 1;

    UCHAR msr;
    msr = KdReadInt8(KdBasePort + COM_MSR);
    KDDBG2("MSR %02x\n", msr);

    while (limitcount != 0) {
        limitcount--;

        lsr = KdReadInt8(KdBasePort + COM_LSR);
        KDDBG2("LSR %02x\n", lsr);
        if (lsr & COM_DATRDY) {
            value = KdReadInt8(KdBasePort + COM_DAT);
            *Input = value & 0xff;
            return KDP_PACKET_RECEIVED;
        }
    }
    return KDP_PACKET_TIMEOUT;
}

//  Fetch a byte from the debug port and return it.
//  N.B. It is assumed that necessary multiprocessor synchronization has been
//  performed before this routine is called.
//
static KDP_STATUS KdCompGetByte(OUT PUCHAR Input)
{
    KDP_STATUS stat;
    KDDBG2("KdCompGetByte\n");
    stat = CpGetByte(Input, TRUE);
    KDDBG2("KdCompGetByte status %d\n", stat);

    return stat;
}

//  Write a byte to the debug port.
//  N.B. It is assumed that necessary multiprocessor synchronization has been
//  performed before this routine is called.
//
static VOID KdCompPutByte(IN UCHAR Output)
{
    KDDBG2("KdCompPutByte %02x\n", Output);
    // wait for the com port to be ready
    while ((KdReadInt8( KdBasePort + COM_LSR ) & COM_OUTRDY) == 0);

    KDDBG2("KdCompPutByte ready\n");

    // write a single char
    KdWriteInt8(KdBasePort + COM_DAT, Output);
    KDDBG2("KdCompPutByte done\n");
}


//  Fetch a byte from the debug port and return it if one is available.
//  N.B. It is assumed that necessary multiprocessor synchronization has been
//  performed before this routine is called.
//
static KDP_STATUS KdCompPollByte(OUT PUCHAR Input)
{
    KDDBG2("KdCompPollByte\n");
    KDP_STATUS status = CpGetByte(Input, FALSE);
    KDDBG2("KdCompPollByte %d\n", status);
    return status;
}

//  Wait for a packet header leader (receive it into PacketLeader ULONG).
//
static
KDP_STATUS
KdCompReceivePacketLeader(
    OUT PULONG PacketLeader,
    IN OUT PKD_CONTEXT KdContext
    )
{

    UCHAR Input;
    UCHAR PreviousByte = 0;
    ULONG PacketId = 0;
    ULONG Index;
    KDP_STATUS ReturnCode;
    BOOLEAN BreakinDetected = FALSE;

    KDDBG2("KdCompReceivePacketLeader\n");
    //
    // NOTE - With all the interrupts being off, it is very hard
    // to implement the actual timeout code. (Maybe, by reading the CMOS.)
    // Here we use a loop count to wait about 3 seconds.  The CpGetByte
    // will return with error code = KDP_PACKET_TIMEOUT if it cannot find data
    // byte within 1 second. Kernel debugger's timeout period is 5 seconds.
    //

    Index = 0;
    do {
        ReturnCode = KdCompGetByte(&Input);
        if (ReturnCode == KDP_PACKET_TIMEOUT) {
            if (BreakinDetected) {
                KdContext->KdpControlCPending = TRUE;
                return KDP_PACKET_RESEND;
            } else {
                KDDBG2("KdCompReceivePackerLeader returning KDP_PACKET_TIMEOUT\n");
                return KDP_PACKET_TIMEOUT;
            }
        } else if (ReturnCode == KDP_PACKET_RESEND) {
            Index = 0;
            continue;
        } else {                    // if (ReturnCode == KDP_PACKET_RECEIVED)
            if ( Input == PACKET_LEADER_BYTE ||
                 Input == CONTROL_PACKET_LEADER_BYTE ) {
                if ( Index == 0 ) {
                    PreviousByte = Input;
                    Index++;
                } else if (Input == PreviousByte ) {
                    Index++;
                } else {
                    PreviousByte = Input;
                    Index = 1;
                }
            } else {

                //
                // If we detect breakin character, we need to verify it
                // validity.  (It is possible that we missed a packet leader
                // and the breakin character is simply a data byte in the
                // packet.)
                // Since kernel debugger send out breakin character ONLY
                // when it is waiting for State Change packet.  The breakin
                // character should not be followed by any other character
                // except packet leader byte.
                //

                if ( Input == BREAKIN_PACKET_BYTE ) {
                    BreakinDetected = TRUE;
                }
                else {

                    //
                    // The following statement is ABSOLUTELY necessary.
                    //

                    BreakinDetected = FALSE;
                }
                Index = 0;
            }
        }
    } while ( Index < 4 );

    if (BreakinDetected) {
        KdContext->KdpControlCPending = TRUE;
    }

    //
    // return the packet leader and FALSE to indicate no resend is needed.
    //

    if ( Input == PACKET_LEADER_BYTE ) {
        *PacketLeader = PACKET_LEADER;
    }
    else {
        *PacketLeader = CONTROL_PACKET_LEADER;
    }
    KdDebuggerNotPresent = FALSE;
#if 0
    SharedUserData->KdDebuggerEnabled |= 0x00000002;
#endif

    return KDP_PACKET_RECEIVED;
}


static
VOID
KdpSendString(
    IN PCHAR Source,
    IN ULONG Length
    )
    //  Routine Description:
    //      This routine writes a string to the kernel debugger port.
    //
    //  Arguments:
    //      Source - Supplies a pointer to the output string.
    //      Length - Supplies the length of the string to be written.
    //
    //  Return Value:
    //      None.
{

    UCHAR Output;

    KDDBG2("KdpSendString len %d\n", Length);

    //
    // Write bytes to the kernel debugger port.
    //

    while (Length > 0) {
        Output = *Source++;
        KdCompPutByte(Output);
        Length -= 1;
    }
    KDDBG2("KdpSendString done\n");
    return;
}

static
KDP_STATUS
KdpReceiveString(
    OUT PCHAR Destination,
    IN ULONG Length
    )
    //  Routine Description:
    //      This routine reads a string from the kernel debugger port.
    //
    //  Arguments:
    //      Destination - Supplies a pointer to the input string.
    //      Length - Supplies the length of the string to be read.
{

    UCHAR Input;
    KDP_STATUS ReturnCode;
    KDDBG2("KdpReceiveString len %d\n", Length);

    //
    // Read bytes until either an error is encountered or the entire string
    // has been read.
    //
    while (Length > 0) {
        KdpSpin();

        ReturnCode = KdCompGetByte(&Input);
        if (ReturnCode != KDP_PACKET_RECEIVED) {
            KDDBG("KdpReceiveString return %d\n", ReturnCode);
            return ReturnCode;
        }
        else {
            *Destination++ = Input;
            Length -= 1;
        }
    }
    KDDBG2("KdpReceiveString return %d\n", KDP_PACKET_RECEIVED);
    return KDP_PACKET_RECEIVED;
}

static
VOID
KdpSendControlPacket(
    IN USHORT PacketType,
    IN ULONG PacketId OPTIONAL
    )
    //  Routine Description:
    //      This routine sends a control packet to the host machine that is running the
    //      kernel debugger and waits for an ACK.
    //
    //  Arguments:
    //      PacketType - Supplies the type of packet to send.
    //      PacketId - Supplies packet id, optionally.
    //
    //  Return Value:
    //      None.
{

    KD_PACKET PacketHeader;

    //
    // Initialize and send the packet header.
    //

    PacketHeader.PacketLeader = CONTROL_PACKET_LEADER;
    if (PacketId != 0) {
        PacketHeader.PacketId = PacketId;
    }
    PacketHeader.ByteCount = 0;
    PacketHeader.Checksum = 0;
    PacketHeader.PacketType = PacketType;
    KdpSendString((PCHAR)&PacketHeader, sizeof(KD_PACKET));

    return;
}

//////////////////////////////////////////////////////////////////////////////
//
void
KdpComSendPacket(
                 IN ULONG PacketType,
                 IN PSTRING MessageHeader,
                 IN PSTRING MessageData OPTIONAL,
                 IN OUT PKD_CONTEXT KdContext
                )
    //  Routine Description:
    //      This routine sends a packet to the host machine that is running the
    //      kernel debugger and waits for an ACK.
    //
    //  Arguments:
    //      PacketType - Supplies the type of packet to send.
    //      MessageHeader - Supplies a pointer to a string descriptor that describes
    //          the message information.
    //      MessageData - Supplies a pointer to a string descriptor that describes
    //          the optional message data.
    //      KdContext - Supplies a pointer to the kernel debugger context.
    //
    //  Return Value:
    //      None.
{

    KD_PACKET PacketHeader;
    ULONG MessageDataLength;
    KDP_STATUS ReturnCode;
    KDDBG2("KdpComSendPacket %d\n", PacketType);

    if (MessageData != NULL) {
        MessageDataLength = MessageData->Length;
        PacketHeader.Checksum = KdpComputeChecksum(MessageData->Buffer,
                                                   MessageData->Length);
    }
    else {
        MessageDataLength = 0;
        PacketHeader.Checksum = 0;
    }
    PacketHeader.Checksum += KdpComputeChecksum(MessageHeader->Buffer,
                                                MessageHeader->Length);

    //
    // Initialize and send the packet header.
    //

    PacketHeader.PacketLeader = PACKET_LEADER;
    PacketHeader.ByteCount = (USHORT)(MessageHeader->Length + MessageDataLength);
    PacketHeader.PacketType = (USHORT)PacketType;

    KdCompNumberRetries = KdCompRetryCount;

    //
    // We sync on first STATE_CHANGE64 message like NT.  If this
    // is the first such message, drain receive pipe as nothing
    // said before this instant is interesting (and any buffered
    // packets may interact badly with SendWaitContinue).
    //
    if (PacketType == PACKET_TYPE_KD_STATE_CHANGE64 && !KdStateChange64Sent) {
        //
        UCHAR uDummy;
        DWORD dwDrained = 0;
        KdCompNextPacketIdToSend |= SYNC_PACKET_ID;
        KdStateChange64Sent = TRUE;

        while (KdCompGetByte(&uDummy) == KDP_PACKET_RECEIVED)
            dwDrained++;
    }

    do {
        KDDBG2("LOOP %d/%d\n", KdCompNumberRetries, KdCompRetryCount);
        if (KdCompNumberRetries == 0) {
            KDDBG("KdCompNumberRetries == 0\n");
            //
            // If the packet is not for reporting exception, we give up
            // and declare debugger not present.
            //
            if (PacketType == PACKET_TYPE_KD_STATE_CHANGE64) {
                PDBGKD_ANY_WAIT_STATE_CHANGE StateChange
                    = (PDBGKD_ANY_WAIT_STATE_CHANGE)MessageHeader->Buffer;
                if (StateChange->NewState == DbgKdLoadSymbolsStateChange) {
                    KdDebuggerNotPresent = TRUE;
                    //SharedUserData->KdDebuggerEnabled &= ~0x00000002;
                    KdCompNextPacketIdToSend = INITIAL_PACKET_ID | SYNC_PACKET_ID;
                    KdCompPacketIdExpected = INITIAL_PACKET_ID;
                    return;
                }
            }
            else if (PacketType == PACKET_TYPE_KD_DEBUG_IO) {
                PDBGKD_DEBUG_IO DebugIo
                    = (PDBGKD_DEBUG_IO)MessageHeader->Buffer;
                if (DebugIo->ApiNumber == DbgKdPrintStringApi) {
                    KdDebuggerNotPresent = TRUE;
                    //SharedUserData->KdDebuggerEnabled &= ~0x00000002;
                    KdCompNextPacketIdToSend = INITIAL_PACKET_ID | SYNC_PACKET_ID;
                    KdCompPacketIdExpected = INITIAL_PACKET_ID;
                    return;
                }
            }
#if 0
            else if (PacketType == PACKET_TYPE_KD_FILE_IO) {
                PDBGKD_FILE_IO FileIo;

                FileIo = (PDBGKD_FILE_IO)MessageHeader->Buffer;
                if (FileIo->ApiNumber == DbgKdCreateFileApi) {
                    KdDebuggerNotPresent = TRUE;
                    //SharedUserData->KdDebuggerEnabled &= ~0x00000002;
                    KdCompNextPacketIdToSend = INITIAL_PACKET_ID | SYNC_PACKET_ID;
                    KdCompPacketIdExpected = INITIAL_PACKET_ID;
                    return;
                }
            }
#endif
        }
        //
        // Setting PacketId has to be in the do loop in case Packet Id was
        // reset.
        //

        PacketHeader.PacketId = KdCompNextPacketIdToSend;
        KdpSendString((PCHAR)&PacketHeader, sizeof(KD_PACKET));

        //
        // Output message header.
        //

        KdpSendString(MessageHeader->Buffer, MessageHeader->Length);

        //
        // Output message data.
        //

        if ( MessageDataLength ) {
            KdpSendString(MessageData->Buffer, MessageData->Length);
        }

        //
        // Output a packet trailing byte
        //

        KdCompPutByte(PACKET_TRAILING_BYTE);

        //
        // Wait for the Ack Packet
        //

        ReturnCode = KdpComReceivePacket(
                                         PACKET_TYPE_KD_ACKNOWLEDGE,
                                         NULL,
                                         NULL,
                                         NULL,
                                         KdContext
                                        );
        if (ReturnCode == KDP_PACKET_TIMEOUT) {
            KDDBG2("TIMEOUT\n");
            KdCompNumberRetries--;
        }
        KdpSpin();
    } while (ReturnCode != KDP_PACKET_RECEIVED);

    KDDBG2("KD: PACKET_RECEIVED\n");
    //
    // Reset Sync bit in packet id.  The packet we sent may have Sync bit set
    //

    KdCompNextPacketIdToSend &= ~SYNC_PACKET_ID;

    //
    // Since we are able to talk to debugger, the retrycount is set to
    // maximum value.
    //

    KdCompRetryCount = KdContext->KdpDefaultRetries;

    KDDBG2("KdpComSendPacket %d done\n", PacketType);
}

//////////////////////////////////////////////////////////////////////////////
//
KDP_STATUS
KdpComReceivePacket(
                    IN ULONG PacketType,
                    OUT PSTRING MessageHeader,
                    OUT PSTRING MessageData,
                    OUT PULONG DataLength,
                    IN OUT PKD_CONTEXT KdContext
                   )
    //  Routine Description:
    //      This routine receives a packet from the host machine that is running
    //      the kernel debugger UI.  This routine is ALWAYS called after packet being
    //      sent by caller.  It first waits for ACK packet for the packet sent and
    //      then waits for the packet desired.
    //
    //      N.B. If caller is KdPrintString, the parameter PacketType is
    //         PACKET_TYPE_KD_ACKNOWLEDGE.  In this case, this routine will return
    //         right after the ack packet is received.
    //
    //  Arguments:
    //      PacketType - Supplies the type of packet that is excepted.
    //      MessageHeader - Supplies a pointer to a string descriptor for the input
    //          message.
    //      MessageData - Supplies a pointer to a string descriptor for the input data.
    //      DataLength - Supplies pointer to ULONG to receive length of recv. data.
    //      KdContext - Supplies a pointer to the kernel debugger context.
    //
    //  Return Value:
    //      KDP_PACKET_RESEND - if resend is required.
    //      KDP_PAKCET_TIMEOUT - if timeout.
    //      KDP_PACKET_RECEIVED - if packet received.
{

    UCHAR Input;
    ULONG MessageLength;
    KD_PACKET PacketHeader;
    KDP_STATUS ReturnCode;
    ULONG Checksum;

    KDDBG2("KdpComReceivePacket %d\n", PacketType);

  WaitForPacketLeader:

    KdpSpin();

    //
    // Read Packet Leader
    //
    ReturnCode = KdCompReceivePacketLeader(&PacketHeader.PacketLeader, KdContext);
    KDDBG2("KdCompReceivePacketLeader returned %d\n", ReturnCode);

    //
    // If we can successfully read packet leader, it has high possibility that
    // kernel debugger is alive.  So reset count.
    //
    if (ReturnCode != KDP_PACKET_TIMEOUT) {
        KdCompNumberRetries = KdCompRetryCount;
    }
    if (ReturnCode != KDP_PACKET_RECEIVED) {
        return ReturnCode;
    }

    //
    // Read packet type.
    //
    ReturnCode = KdpReceiveString((PCHAR)&PacketHeader.PacketType,
                                  sizeof(PacketHeader.PacketType));
    if (ReturnCode == KDP_PACKET_TIMEOUT) {
        return KDP_PACKET_TIMEOUT;
    }
    else if (ReturnCode == KDP_PACKET_RESEND) {
        if (PacketHeader.PacketLeader == CONTROL_PACKET_LEADER) {
            //
            // If read error and it is for a control packet, simply
            // pretend that we have not seen this packet.  Hopefully
            // we will receive the packet we desire which automatically acks
            // the packet we just sent.
            //
            goto WaitForPacketLeader;
        }
        else {
            //
            // if read error while reading data packet, we have to ask
            // kernel debugger to resend us the packet.
            //
            goto SendResendPacket;
        }
    }

    //
    // if the packet we received is a resend request, we return true and
    // let caller resend the packet.
    //
    if ( PacketHeader.PacketLeader == CONTROL_PACKET_LEADER &&
         PacketHeader.PacketType == PACKET_TYPE_KD_RESEND ) {
        return KDP_PACKET_RESEND;
    }

    //
    // Read data length.
    //
    ReturnCode = KdpReceiveString((PCHAR)&PacketHeader.ByteCount,
                                  sizeof(PacketHeader.ByteCount));
    if (ReturnCode == KDP_PACKET_TIMEOUT) {
        return KDP_PACKET_TIMEOUT;
    }
    else if (ReturnCode == KDP_PACKET_RESEND) {
        if (PacketHeader.PacketLeader == CONTROL_PACKET_LEADER) {
            goto WaitForPacketLeader;
        }
        else {
            goto SendResendPacket;
        }
    }

    //
    // Read Packet Id.
    //
    ReturnCode = KdpReceiveString((PCHAR)&PacketHeader.PacketId,
                                  sizeof(PacketHeader.PacketId));

    if (ReturnCode == KDP_PACKET_TIMEOUT) {
        return KDP_PACKET_TIMEOUT;
    }
    else if (ReturnCode == KDP_PACKET_RESEND) {
        if (PacketHeader.PacketLeader == CONTROL_PACKET_LEADER) {
            goto WaitForPacketLeader;
        }
        else {
            goto SendResendPacket;
        }
    }

    //
    // Read packet checksum.
    //
    ReturnCode = KdpReceiveString((PCHAR)&PacketHeader.Checksum,
                                  sizeof(PacketHeader.Checksum));
    if (ReturnCode == KDP_PACKET_TIMEOUT) {
        return KDP_PACKET_TIMEOUT;
    }
    else if (ReturnCode == KDP_PACKET_RESEND) {
        if (PacketHeader.PacketLeader == CONTROL_PACKET_LEADER) {
            goto WaitForPacketLeader;
        }
        else {
            goto SendResendPacket;
        }
    }

    //
    // A complete packet header is received.  Check its validity and
    // perform appropriate action depending on packet type.
    //
    if (PacketHeader.PacketLeader == CONTROL_PACKET_LEADER ) {
        if (PacketHeader.PacketType == PACKET_TYPE_KD_ACKNOWLEDGE ) {
            //
            // If we received an expected ACK packet and we are not
            // waiting for any new packet, update outgoing packet id
            // and return.  If we are NOT waiting for ACK packet
            // we will keep on waiting.  If the ACK packet
            // is not for the packet we send, ignore it and keep on waiting.
            //
            if (PacketHeader.PacketId !=
                (KdCompNextPacketIdToSend & ~SYNC_PACKET_ID))  {
                goto WaitForPacketLeader;
            }
            else if (PacketType == PACKET_TYPE_KD_ACKNOWLEDGE) {
                KdCompNextPacketIdToSend ^= 1;
                return KDP_PACKET_RECEIVED;
            } else {
                goto WaitForPacketLeader;
            }
        }
        else if (PacketHeader.PacketType == PACKET_TYPE_KD_RESET) {
            //
            // if we received Reset packet, reset the packet control variables
            // and resend earlier packet.
            //
            KdCompNextPacketIdToSend = INITIAL_PACKET_ID;
            KdCompPacketIdExpected = INITIAL_PACKET_ID;
            KdpSendControlPacket(PACKET_TYPE_KD_RESET, 0L);
            return KDP_PACKET_RESEND;
        }
        else if (PacketHeader.PacketType == PACKET_TYPE_KD_RESEND) {
            return KDP_PACKET_RESEND;
        }
        else {
            //
            // Invalid packet header, ignore it.
            //
            goto WaitForPacketLeader;
        }

        //
        // The packet header is for data packet (not control packet).
        //

    }
    else if (PacketType == PACKET_TYPE_KD_ACKNOWLEDGE) {
        //
        // if we are waiting for ACK packet ONLY
        // and we receive a data packet header, check if the packet id
        // is what we expected.  If yes, assume the acknowledge is lost (but
        // sent), ask sender to resend and return with PACKET_RECEIVED.
        //
        if (PacketHeader.PacketId == KdCompPacketIdExpected) {
            KdpSendControlPacket(PACKET_TYPE_KD_RESEND, 0L);
            KdCompNextPacketIdToSend ^= 1;
            return KDP_PACKET_RECEIVED;
        }
        else {
            KdpSendControlPacket(PACKET_TYPE_KD_ACKNOWLEDGE,
                                 PacketHeader.PacketId);
            goto WaitForPacketLeader;
        }
    }

    //
    // we are waiting for data packet and we received the packet header
    // for data packet. Perform the following checks to make sure
    // it is the packet we are waiting for.
    //

    //
    // Check ByteCount received is valid
    //
    MessageLength = MessageHeader->MaximumLength;
    if ((PacketHeader.ByteCount > (USHORT)PACKET_MAX_SIZE) ||
        (PacketHeader.ByteCount < (USHORT)MessageLength)) {
        goto SendResendPacket;
    }
    *DataLength = PacketHeader.ByteCount - MessageLength;

    //
    // Read the message header.
    //
    ReturnCode = KdpReceiveString(MessageHeader->Buffer, MessageLength);
    if (ReturnCode != KDP_PACKET_RECEIVED) {
        goto SendResendPacket;
    }
    MessageHeader->Length = (USHORT)MessageLength;

    //
    // Read the message data.
    //
    ReturnCode = KdpReceiveString(MessageData->Buffer, *DataLength);
    if (ReturnCode != KDP_PACKET_RECEIVED) {
        goto SendResendPacket;
    }
    MessageData->Length = (USHORT)*DataLength;

    //
    // Read packet trailing byte
    //
    ReturnCode = KdCompGetByte(&Input);
    if (ReturnCode != KDP_PACKET_RECEIVED || Input != PACKET_TRAILING_BYTE) {
        goto SendResendPacket;
    }

    //
    // Check PacketType is what we are waiting for.
    //
    if (PacketType != PacketHeader.PacketType) {
        KdpSendControlPacket(PACKET_TYPE_KD_ACKNOWLEDGE,
                             PacketHeader.PacketId);
        goto WaitForPacketLeader;
    }

    //
    // Check PacketId is valid.
    //
    if (PacketHeader.PacketId == INITIAL_PACKET_ID ||
        PacketHeader.PacketId == (INITIAL_PACKET_ID ^ 1)) {
        if (PacketHeader.PacketId != KdCompPacketIdExpected) {
            KdpSendControlPacket(PACKET_TYPE_KD_ACKNOWLEDGE,
                                 PacketHeader.PacketId);
            goto WaitForPacketLeader;
        }
    }
    else {
        goto SendResendPacket;
    }

    //
    // Check checksum is valid.
    //
    Checksum = KdpComputeChecksum(MessageHeader->Buffer,
                                  MessageHeader->Length);
    Checksum += KdpComputeChecksum(MessageData->Buffer,
                                   MessageData->Length);
    if (Checksum != PacketHeader.Checksum) {
        goto SendResendPacket;
    }

    //
    // Send Acknowledge byte and the Id of the packet received.
    // Then, update the ExpectId for next incoming packet.
    //
    KdpSendControlPacket(PACKET_TYPE_KD_ACKNOWLEDGE,
                         PacketHeader.PacketId);

    //
    // We have successfully received the packet so update the
    // packet control variables and return success.
    //
    KdCompPacketIdExpected ^= 1;
    KDDBG2("KdpComReceivePacket - got one!\n");
    return KDP_PACKET_RECEIVED;

  SendResendPacket:
    KdpSendControlPacket(PACKET_TYPE_KD_RESEND, 0L);
    goto WaitForPacketLeader;
}

// Returns TRUE if a breakin packet is pending.
// A packet is present if: There is a valid character which matches BREAK_CHAR.
bool KdpComPollBreakIn()
{
    KDDBG2("KdpComPollBreakIn\n");
    UCHAR Input;
    ULONG Status = KdCompPollByte(&Input);
    KDDBG2("KdCompPollByte STATUS %d Input %02x\n", Status, Input);
    if ((Status == KDP_PACKET_RECEIVED) && (Input == BREAKIN_PACKET_BYTE)) {
        KDDBG("KDP_PACKET_RECEIVED\n");
        KdDebuggerNotPresent = FALSE;
        return true;
    }
    return false;
}
//
///////////////////////////////////////////////////////////////// End of File.
