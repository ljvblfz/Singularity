//////////////////////////////////////////////////////////////////////////////
//
// Copyright (c) Microsoft Corporation.  All rights reserved.
//
// debug.h: runtime support for debugging
//

#include "debug.h"

extern "C" void  IoSpaceWrite8(uint16 port, uint8 value);
extern "C" uint8 IoSpaceRead8(uint16 port);

#define FALSE 0
#define TRUE   1

#define IN
#define OUT

// Define the maximum number of retries for packet sends.
//
#define MAXIMUM_RETRIES 20

// Define packet waiting status codes.
//
#define BD_PACKET_RECEIVED 0
#define BD_PACKET_TIMEOUT 1
#define BD_PACKET_RESEND 2

// COM PORT Constants
//
#define COM1_PORT   0x03f8
#define COM2_PORT   0x02f8
#define COM3_PORT   0x03e8
#define COM4_PORT   0x02e8

#define COM_DAT     0x00
#define COM_IEN     0x01            // interrupt enable register
#define COM_FCR     0x02            // FIFO Control Register
#define COM_LCR     0x03            // line control registers
#define COM_MCR     0x04            // modem control reg
#define COM_LSR     0x05            // line status register
#define COM_MSR     0x06            // modem status register
#define COM_SCR     0x07            // scratch register
#define COM_DLL     0x00            // divisor latch least sig
#define COM_DLM     0x01            // divisor latch most sig

#define COM_DATRDY  0x01
#define COM_OUTRDY  0x20

#define LC_DLAB     0x80

#define CLOCK_RATE  0x1C200         // USART clock rate

#define MC_DTRRTS   0x03            // Control bits to assert DTR and RTS
#define MS_DSRCTSCD 0xB0            // Status bits for DSR, CTS and CD
#define MS_CD       0x80

//
// Communication functions (comio.c)
//

static
UINT32
BdComputeChecksum(
    IN PUINT8 Buffer,
    IN UINT32 Length
    );

static
UINT16
BdReceivePacketLeader(
    IN UINT32 PacketType,
    OUT PUINT32 PacketLeader
    );

static
void
BdSendControlPacket(
    IN UINT16 PacketType,
    IN UINT32 PacketId
    );

static
UINT32
BdReceivePacket(
    IN UINT32 ExpectedPacketType,
    OUT PSTRING MessageHeader,
    OUT PSTRING MessageData,
    OUT PUINT32 DataLength
    );

static
void
BdSendPacket(
    IN UINT32 PacketType,
    IN PSTRING MessageHeader,
    IN PSTRING MessageData
    );

static
UINT32
BdReceiveString(
    OUT PUINT8 Destination,
    IN UINT32 Length
    );

static
void
BdSendString(
    IN PUINT8 Source,
    IN UINT32 Length
    );

static
void
BdSendControlPacket(
    IN UINT16 PacketType,
    IN UINT32 PacketId
    );

// Debugger enabled and present.
//
static BOOL BdDebuggerNotPresent = FALSE;
static UINT16 BdBasePort = COM2_PORT;

// Next packet id to send and next packet id to expect.
//
static UINT32 BdPacketIdExpected;
static UINT32 BdNextPacketIdToSend;

// Number of retries and the retry count.
//
static UINT32 BdNumberRetries = MAXIMUM_RETRIES;
static UINT32 BdRetryCount = MAXIMUM_RETRIES;

////////////////////////////////////////////////////////// Serial Port Input & Output.
//

static UINT8 BdReadInt8(UINT16 port)
{
    UINT8 value;

    __asm {
        mov dx,port;
        in al,dx;
        mov value, al
    }

    return value;
}

static void BdWriteInt8(UINT16 port, UINT8 value)
{
    __asm {
        mov dx,port;
        mov al,value;
        out dx,al;
    }
}

static void ResetTimer0()
{
    // Put into periodic mode with maximum period.
    IoSpaceWrite8(0x43, 0x33);
    IoSpaceWrite8(0x40, 0);
    IoSpaceWrite8(0x40, 0);
}

static uint16 GetTimer0()
{
    // Latch timer values
    IoSpaceWrite8(0x43, 0x00);
    // Combine low and high byte values
    return IoSpaceRead8(0x40) + (((UINT16)IoSpaceRead8(0x40)) << 8);
}

static bool BdComInit(UINT16 debugBasePort)
// Initializes the communication port (baud rate, parity etc.)
{
    ResetTimer0();

    BdBasePort = debugBasePort;

    UINT16    BaudRate = 1;      // 115200 bps

    // turn off interrupts
    BdWriteInt8(BdBasePort + COM_LCR, 0x00);
    BdWriteInt8(BdBasePort + COM_IEN, 0x00);

    // Turn on DTS/RTS
    BdWriteInt8(BdBasePort + COM_MCR, MC_DTRRTS); // Needed for VirtualPC PIPE/Serial

    // Turn on FIFO
    BdWriteInt8(BdBasePort + COM_FCR, 1);

    // Set the baud rate
    BdWriteInt8(BdBasePort + COM_LCR, LC_DLAB);  // Divisor latch access bit
    BdWriteInt8(BdBasePort + COM_DLM, (UINT8)(BaudRate >> 8));
    BdWriteInt8(BdBasePort + COM_DLL, (UINT8)(BaudRate & 0xFF));

    // initialize the LCR
    BdWriteInt8(BdBasePort + COM_LCR, 0x03);
    // 8 data bits, 1 stop bit, no parity, no break

    // See if the 16450/16550 scratch register is available.
    // If not, we'll assume the serial port doesn't really exist.
    BdWriteInt8(BdBasePort + COM_SCR, 0xff);
    UINT8 a1 = BdReadInt8(BdBasePort + COM_SCR);
    BdWriteInt8(BdBasePort + COM_SCR, 0x00);
    UINT8 a2 = BdReadInt8(BdBasePort + COM_SCR);

    return (bool) ((a1 == (UINT8)0xff) && (a2 == (UINT8)0x00));
}

static void Stall(UINT16 DelayMicros)
{
/*++

Routine Description:

    Stall processor for approximately the number microseconds.  This method
    uses the i8254 timer, which runs at around 1.13MHz.  For the purposes
    of this method this is close enough to 1MHz.

Arguments:

    DelayMicros - Number of microseconds to stall.

Return Value:

    None
*/
    UINT16 t0 = GetTimer0();
    UINT16 delta;
    while ((delta = t0 - GetTimer0()) < DelayMicros) ;
}

static
UINT32
BdPortGetByte(
    OUT PUINT8 Input
    )
/*++

Routine Description:

    Fetch a byte from the debug port and return it.

Arguments:

    Input - Returns the data byte.

Return Value:

    CP_GET_SUCCESS is returned if a byte is successfully read from the
        kernel debugger line.
    CP_GET_ERROR is returned if error encountered during reading.
    CP_GET_NODATA is returned if timeout.

--*/
{
    //
    // Define wait timeout value.
    //

#define TIMEOUT_COUNT 2000 /* 1024 * 30 */

    UINT8 lsr;
    UINT8 value;
    UINT32 limitcount = TIMEOUT_COUNT;

    UINT8 msr;
    msr = BdReadInt8(BdBasePort + COM_MSR);

    while (limitcount != 0) {
        limitcount--;

        lsr = BdReadInt8(BdBasePort + COM_LSR);
        if (lsr & COM_DATRDY) {
            value = BdReadInt8(BdBasePort + COM_DAT);
            *Input = (UINT8)(value & 0xff);
            return CP_GET_SUCCESS;
        }
        Stall(50);
    }
    return CP_GET_NODATA;
}


static
void
BdPortPutByte(
    IN UINT8 Output
    )
{
    // wait for the com port to be ready
    while ((BdReadInt8( BdBasePort + COM_LSR ) & COM_OUTRDY) == 0) { }

    // write a single char
    BdWriteInt8(BdBasePort + COM_DAT, Output);
}

//////////////////////////////////////////////////////////////////////////////

static
UINT32
BdComputeChecksum(
    IN PUINT8 Buffer,
    IN UINT32 Length
    )

/*++

Routine Description:

    This routine computes the checksum of the specified buffer.

Arguments:

    Buffer - Supplies a pointer to the buffer.

    Length - Supplies the length of the buffer.

Return Value:

    A UINT32 is return as the checksum for the input string.

--*/

{

    UINT32 Checksum = 0;

    while (Length > 0) {
        Checksum = Checksum + (UINT32)*Buffer++;
        Length--;
    }

    return Checksum;
}

static
UINT16
BdReceivePacketLeader(
                      IN UINT32 /* PacketType */,
                      OUT PUINT32 PacketLeader
                     )

/*++

Routine Description:

    This routine waits for a packet header leader.

Arguments:

    PacketType - supplies the type of packet we are expecting.

    PacketLeader - supplies a pointer to a ulong variable to receive
                   packet leader bytes.

Return Value:

    BD_PACKET_RESEND - if resend is required.
    BD_PAKCET_TIMEOUT - if timeout.
    BD_PACKET_RECEIVED - if packet received.

--*/

{

    UINT8 Input, PreviousByte = 0;
    UINT32 PacketId = 0;
    UINT32 Index;
    UINT32 ReturnCode;

    // NOTE - With all the interrupts being off, it is very hard
    // to implement the actual timeout code. (Maybe, by reading the CMOS.)
    // Here we use a loop count to wait about 3 seconds.  The CpGetByte
    // will return with error code = CP_GET_NODATA if it cannot find data
    // byte within 1 second. Kernel debugger's timeout period is 5 seconds.
    //
    Index = 0;
    do {
        ReturnCode = BdPortGetByte(&Input);
        if (ReturnCode == CP_GET_NODATA) {
            return BD_PACKET_TIMEOUT;
        } else if (ReturnCode == CP_GET_ERROR) {
            Index = 0;
            continue;

        } else {                    // if (ReturnCode == CP_GET_SUCCESS)
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

                Index = 0;
            }
        }
    } while ( Index < 4 );

    // return the packet leader and FALSE to indicate no resend is needed.
    //
    if ( Input == PACKET_LEADER_BYTE ) {
        *PacketLeader = PACKET_LEADER;

    } else {
        *PacketLeader = CONTROL_PACKET_LEADER;
    }

    BdDebuggerNotPresent = FALSE;
    return BD_PACKET_RECEIVED;
}

static
void
BdSendControlPacket(
    IN UINT16 PacketType,
    IN UINT32 PacketId
    )

/*++

Routine Description:

    This routine sends a control packet to the host machine that is running the
    kernel debugger and waits for an ACK.

Arguments:

    PacketType - Supplies the type of packet to send.

    PacketId - Supplies packet id, optionally.

Return Value:

    None.

--*/

{

    KD_PACKET PacketHeader;

    // Initialize and send the packet header.
    //
    PacketHeader.PacketLeader = CONTROL_PACKET_LEADER;
    if (PacketId != 0) {
        PacketHeader.PacketId = PacketId;
    }

    PacketHeader.ByteCount = 0;
    PacketHeader.Checksum = 0;
    PacketHeader.PacketType = PacketType;
    BdSendString((PUINT8)&PacketHeader, sizeof(KD_PACKET));
    return;
}

static
UINT32
BdReceivePacket(
    IN UINT32 PacketType,
    OUT PSTRING MessageHeader,
    OUT PSTRING MessageData,
    OUT PUINT32 DataLength
    )

/*++

Routine Description:

    This routine receives a packet from the host machine that is running
    the kernel debugger UI.  This routine is ALWAYS called after packet being
    sent by caller.  It first waits for ACK packet for the packet sent and
    then waits for the packet desired.

    N.B. If caller is BdrintString, the parameter PacketType is
       PACKET_TYPE_KD_ACKNOWLEDGE.  In this case, this routine will return
       right after the ack packet is received.

Arguments:

    PacketType - Supplies the type of packet that is excepted.

    MessageHeader - Supplies a pointer to a string descriptor for the input
        message.

    MessageData - Supplies a pointer to a string descriptor for the input data.

    DataLength - Supplies pointer to UINT32 to receive length of recv. data.

Return Value:

    BD_PACKET_RESEND - if resend is required.
    BD_PAKCET_TIMEOUT - if timeout.
    BD_PACKET_RECEIVED - if packet received.

--*/

{

    UINT8 Input;
    UINT32 MessageLength;
    KD_PACKET PacketHeader;
    UINT32 ReturnCode;
    UINT32 Checksum;

WaitForPacketLeader:

    //
    // Read Packet Leader
    //

    ReturnCode = BdReceivePacketLeader(PacketType, &PacketHeader.PacketLeader);

    //
    // If we can successfully read packet leader, it has high possibility that
    // kernel debugger is alive.  So reset count.
    //

    if (ReturnCode != BD_PACKET_TIMEOUT) {
        BdNumberRetries = BdRetryCount;
    }
    if (ReturnCode != BD_PACKET_RECEIVED) {
        return ReturnCode;
    }

    //
    // Read packet type.
    //

    ReturnCode = BdReceiveString((PUINT8)&PacketHeader.PacketType,
                                 sizeof(PacketHeader.PacketType));

    if (ReturnCode == CP_GET_NODATA) {
        return BD_PACKET_TIMEOUT;

    } else if (ReturnCode == CP_GET_ERROR) {
        if (PacketHeader.PacketLeader == CONTROL_PACKET_LEADER) {

            //
            // If read error and it is for a control packet, simply
            // pretend that we have not seen this packet.  Hopefully
            // we will receive the packet we desire which automatically acks
            // the packet we just sent.
            //

            goto WaitForPacketLeader;

        } else {

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
        return BD_PACKET_RESEND;
    }

    //
    // Read data length.
    //

    ReturnCode = BdReceiveString((PUINT8)&PacketHeader.ByteCount,
                                 sizeof(PacketHeader.ByteCount));

    if (ReturnCode == CP_GET_NODATA) {
        return BD_PACKET_TIMEOUT;
    } else if (ReturnCode == CP_GET_ERROR) {
        if (PacketHeader.PacketLeader == CONTROL_PACKET_LEADER) {
            goto WaitForPacketLeader;
        } else {
            goto SendResendPacket;
        }
    }

    //
    // Read Packet Id.
    //

    ReturnCode = BdReceiveString((PUINT8)&PacketHeader.PacketId,
                                 sizeof(PacketHeader.PacketId));

    if (ReturnCode == CP_GET_NODATA) {
        return BD_PACKET_TIMEOUT;
    } else if (ReturnCode == CP_GET_ERROR) {
        if (PacketHeader.PacketLeader == CONTROL_PACKET_LEADER) {
            goto WaitForPacketLeader;
        } else {
            goto SendResendPacket;
        }
    }

    //
    // Read packet checksum.
    //

    ReturnCode = BdReceiveString((PUINT8)&PacketHeader.Checksum,
                                 sizeof(PacketHeader.Checksum));

    if (ReturnCode == CP_GET_NODATA) {
        return BD_PACKET_TIMEOUT;

    } else if (ReturnCode == CP_GET_ERROR) {
        if (PacketHeader.PacketLeader == CONTROL_PACKET_LEADER) {
            goto WaitForPacketLeader;
        } else {
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
                (BdNextPacketIdToSend & ~SYNC_PACKET_ID))  {
                goto WaitForPacketLeader;

            } else if (PacketType == PACKET_TYPE_KD_ACKNOWLEDGE) {
                BdNextPacketIdToSend ^= 1;
                return BD_PACKET_RECEIVED;

            } else {
                goto WaitForPacketLeader;
            }

        } else if (PacketHeader.PacketType == PACKET_TYPE_KD_RESET) {

            //
            // if we received Reset packet, reset the packet control variables
            // and resend earlier packet.
            //

            BdNextPacketIdToSend = INITIAL_PACKET_ID;
            BdPacketIdExpected = INITIAL_PACKET_ID;
            BdSendControlPacket(PACKET_TYPE_KD_RESET, 0L);
            return BD_PACKET_RESEND;

        } else if (PacketHeader.PacketType == PACKET_TYPE_KD_RESEND) {
            return BD_PACKET_RESEND;

        } else {

            //
            // Invalid packet header, ignore it.
            //

            goto WaitForPacketLeader;
        }

    //
    // The packet header is for data packet (not control packet).
    //

    } else if (PacketType == PACKET_TYPE_KD_ACKNOWLEDGE) {

        //
        // if we are waiting for ACK packet ONLY
        // and we receive a data packet header, check if the packet id
        // is what we expected.  If yes, assume the acknowledge is lost (but
        // sent), ask sender to resend and return with PACKET_RECEIVED.
        //

        if (PacketHeader.PacketId == BdPacketIdExpected) {
            BdSendControlPacket(PACKET_TYPE_KD_RESEND, 0L);
            BdNextPacketIdToSend ^= 1;
            return BD_PACKET_RECEIVED;

        } else {
            BdSendControlPacket(PACKET_TYPE_KD_ACKNOWLEDGE,
                                PacketHeader.PacketId);

            goto WaitForPacketLeader;
        }
    }

    //
    // we are waiting for data packet and we received the packet header
    // for data packet. Perform the following checks to make sure
    // it is the packet we are waiting for.
    //
    // Check ByteCount received is valid
    //

    MessageLength = MessageHeader->MaximumLength;
    if ((PacketHeader.ByteCount > (UINT16)PACKET_MAX_SIZE) ||
        (PacketHeader.ByteCount < (UINT16)MessageLength)) {
        goto SendResendPacket;
    }

    *DataLength = PacketHeader.ByteCount - MessageLength;

    //
    // Read the message header.
    //

    ReturnCode = BdReceiveString(MessageHeader->Buffer, MessageLength);
    if (ReturnCode != CP_GET_SUCCESS) {
        goto SendResendPacket;
    }

    MessageHeader->Length = (UINT16)MessageLength;

    //
    // Read the message data.
    //

    ReturnCode = BdReceiveString(MessageData->Buffer, *DataLength);
    if (ReturnCode != CP_GET_SUCCESS) {
        goto SendResendPacket;
    }

    MessageData->Length = (UINT16)*DataLength;

    //
    // Read packet trailing byte
    //

    ReturnCode = BdPortGetByte(&Input);
    if (ReturnCode != CP_GET_SUCCESS || Input != PACKET_TRAILING_BYTE) {
        goto SendResendPacket;
    }

    //
    // Check PacketType is what we are waiting for.
    //

    if (PacketType != PacketHeader.PacketType) {
        BdSendControlPacket(PACKET_TYPE_KD_ACKNOWLEDGE,
                             PacketHeader.PacketId
                             );
        goto WaitForPacketLeader;
    }

    //
    // Check PacketId is valid.
    //

    if (PacketHeader.PacketId == INITIAL_PACKET_ID ||
        PacketHeader.PacketId == (INITIAL_PACKET_ID ^ 1)) {
        if (PacketHeader.PacketId != BdPacketIdExpected) {
            BdSendControlPacket(PACKET_TYPE_KD_ACKNOWLEDGE,
                                 PacketHeader.PacketId
                                 );
            goto WaitForPacketLeader;
        }

    } else {
        goto SendResendPacket;
    }

    //
    // Check checksum is valid.
    //

    Checksum = BdComputeChecksum(MessageHeader->Buffer,
                                 MessageHeader->Length);


    Checksum += BdComputeChecksum(MessageData->Buffer,
                                  MessageData->Length);

    if (Checksum != PacketHeader.Checksum) {
        goto SendResendPacket;
    }

    //
    // Send Acknowledge byte and the Id of the packet received.
    // Then, update the ExpectId for next incoming packet.
    //

    BdSendControlPacket(PACKET_TYPE_KD_ACKNOWLEDGE,
                        PacketHeader.PacketId);

    //
    // We have successfully received the packet so update the
    // packet control variables and return success.
    //

    BdPacketIdExpected ^= 1;
    return BD_PACKET_RECEIVED;

SendResendPacket:
    BdSendControlPacket(PACKET_TYPE_KD_RESEND, 0L);
    goto WaitForPacketLeader;
}

static
void
BdSendPacket(
    IN UINT32 PacketType,
    IN PSTRING MessageHeader,
    IN PSTRING MessageData
    )

/*++

Routine Description:

    This routine sends a packet to the host machine that is running the
    kernel debugger and waits for an ACK.

Arguments:

    PacketType - Supplies the type of packet to send.

    MessageHeader - Supplies a pointer to a string descriptor that describes
        the message information.

    MessageData - Supplies a pointer to a string descriptor that describes
        the optional message data.

Return Value:

    None.

--*/

{

    KD_PACKET PacketHeader;
    UINT32 MessageDataLength;
    UINT32 ReturnCode;
    PDBGKD_DEBUG_IO DebugIo;

    if (MessageData != NULL) {
        MessageDataLength = MessageData->Length;
        PacketHeader.Checksum = BdComputeChecksum(MessageData->Buffer,
                                                  MessageData->Length);

    } else {
        MessageDataLength = 0;
        PacketHeader.Checksum = 0;
    }

    PacketHeader.Checksum += BdComputeChecksum(MessageHeader->Buffer,
                                               MessageHeader->Length);

    //
    // Initialize and send the packet header.
    //

    PacketHeader.PacketLeader = PACKET_LEADER;
    PacketHeader.ByteCount = (UINT16)(MessageHeader->Length + MessageDataLength);
    PacketHeader.PacketType = (UINT16)PacketType;
    BdNumberRetries = BdRetryCount;
    do {
        if (BdNumberRetries == 0) {

            //
            // If the packet is not for reporting exception, we give up
            // and declare debugger not present.
            //

            if (PacketType == PACKET_TYPE_KD_DEBUG_IO) {
                DebugIo = (PDBGKD_DEBUG_IO)MessageHeader->Buffer;
                if (DebugIo->ApiNumber == DbgKdPrintStringApi) {
                    BdDebuggerNotPresent = TRUE;
                    BdNextPacketIdToSend = INITIAL_PACKET_ID | SYNC_PACKET_ID;
                    BdPacketIdExpected = INITIAL_PACKET_ID;
                    return;
                }
            }
        }

        //
        // Setting PacketId has to be in the do loop in case Packet Id was
        // reset.
        //

        PacketHeader.PacketId = BdNextPacketIdToSend;
        BdSendString((PUINT8)&PacketHeader, sizeof(KD_PACKET));

        //
        // Output message header.
        //

        BdSendString(MessageHeader->Buffer, MessageHeader->Length);

        //
        // Output message data.
        //

        if ( MessageDataLength ) {
            BdSendString(MessageData->Buffer, MessageData->Length);
        }

        //
        // Output a packet trailing byte
        //

        BdPortPutByte(PACKET_TRAILING_BYTE);

        //
        // Wait for the Ack Packet
        //

        ReturnCode = BdReceivePacket(PACKET_TYPE_KD_ACKNOWLEDGE,
                                     NULL,
                                     NULL,
                                     NULL);

        if (ReturnCode == BD_PACKET_TIMEOUT) {
            BdNumberRetries--;
        }

    } while (ReturnCode != BD_PACKET_RECEIVED);

    //
    // Reset Sync bit in packet id.  The packet we sent may have Sync bit set
    //

    BdNextPacketIdToSend &= ~SYNC_PACKET_ID;

    //
    // Since we are able to talk to debugger, the retrycount is set to
    // maximum value.
    //

    BdRetryCount = MAXIMUM_RETRIES;
}

static
UINT32
BdReceiveString(
    OUT PUINT8 Destination,
    IN UINT32 Length
    )

/*++

Routine Description:

    This routine reads a string from the kernel debugger port.

Arguments:

    Destination - Supplies a pointer to the input string.

    Length - Supplies the length of the string to be read.

Return Value:

    CP_GET_SUCCESS is returned if string is successfully read from the
        kernel debugger line.
    CP_GET_ERROR is returned if error encountered during reading.
    CP_GET_NODATA is returned if timeout.

--*/

{

    UINT8 Input;
    UINT32 ReturnCode;

    //
    // Read bytes until either an error is encountered or the entire string
    // has been read.
    //
    while (Length > 0) {
        ReturnCode = BdPortGetByte(&Input);
        if (ReturnCode != CP_GET_SUCCESS) {
            return ReturnCode;
        } else {
            *Destination++ = Input;
            Length -= 1;
        }
    }

    return CP_GET_SUCCESS;
}

static
void
BdSendString(
    IN PUINT8 Source,
    IN UINT32 Length
    )

/*++

Routine Description:

    This routine writes a string to the kernel debugger port.

Arguments:

    Source - Supplies a pointer to the output string.

    Length - Supplies the length of the string to be written.

Return Value:

    None.

--*/

{

    UINT8 Output;

    //
    // Write bytes to the kernel debugger port.
    //

    while (Length > 0) {
        Output = *Source++;
        BdPortPutByte(Output);
        Length -= 1;
    }

    return;
}

void BdPrintString(char *Output, UINT Length)

/*++

Routine Description:

    This routine prints a string.

Arguments:

    Output - Supplies a pointer to a string descriptor for the output string.

--*/

{

    STRING MessageData;
    STRING MessageHeader;
    DBGKD_DEBUG_IO DebugIo;

    if (BdDebuggerNotPresent) {
        return;
    }

    // If the total message length is greater than the maximum packet size,
    // then truncate the output string.
    //
    if ((sizeof(DBGKD_DEBUG_IO) + Length) > PACKET_MAX_SIZE) {
        Length = PACKET_MAX_SIZE - sizeof(DBGKD_DEBUG_IO);
    }

    // Construct the print string message and message descriptor.
    //
    DebugIo.ApiNumber = DbgKdPrintStringApi;
    DebugIo.ProcessorLevel = 0;
    DebugIo.Processor = 0;
    DebugIo.LengthOfString = Length;
    MessageHeader.Length = sizeof(DBGKD_DEBUG_IO);
    MessageHeader.Buffer = (PUINT8)&DebugIo;

    // Construct the print string data and data descriptor.
    //
    MessageData.Length = (UINT16)Length;
    MessageData.Buffer = (PUINT8)Output;

    // Send packet to the kernel debugger on the host machine.
    //
    BdSendPacket(PACKET_TYPE_KD_DEBUG_IO, &MessageHeader, &MessageData);
}


bool
BdInitDebugger(UINT16 basePort)
{
    // Attempt to initialize the debug port.
    //
    if (basePort >= 0x100 && BdComInit(basePort)) {
        BdDebuggerNotPresent = FALSE;

        // Initialize the ID for the NEXT packet to send and the Expect
        // ID of next incoming packet.
        //
        BdNextPacketIdToSend = INITIAL_PACKET_ID | SYNC_PACKET_ID;
        BdPacketIdExpected = INITIAL_PACKET_ID;

        // Number of retries and the retry count.
        //
        BdNumberRetries = 5;
        BdRetryCount = 5;

        return TRUE;
    }

    BdDebuggerNotPresent = TRUE;
    return FALSE;
}
