///////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   More
//
//  Note:   A text pager contributed by Valerie See (vsee)
//

using System;
using System.Collections;
using System.Diagnostics;
using System.IO;

using Pipe = Microsoft.Singularity.Io.Tty2006;
using FileSystem.Utils;
using Microsoft.Contracts;
using Microsoft.SingSharp.Reflection;

using Microsoft.Singularity.Applications;
using Microsoft.Singularity.Io;
using Microsoft.Singularity.Directory;
using Microsoft.Singularity.Configuration;
using Microsoft.Singularity.Channels;
[assembly: Transform(typeof(ApplicationResourceTransform))]

namespace Microsoft.Singularity.Applications
{
    [ConsoleCategory(HelpMessage="Display a screen full of text", DefaultAction=true)]
    internal class Parameters {
        [InputEndpoint("data")]
        public readonly TRef<UnicodePipeContract.Exp:READY> Stdin;

        [OutputEndpoint("data")]
        public readonly TRef<UnicodePipeContract.Imp:READY> Stdout;

        [Endpoint]
        public readonly TRef<DirectoryServiceContract.Imp:Start> nsRef;

        [Endpoint]
        public readonly TRef<KeyboardDeviceContract.Imp:Start> keyboardRef; 

        [StringArrayParameter( "files", HelpMessage="Files to be listed.")]
        internal string[] fileSet;

        reflective internal Parameters();

        internal int AppMain() {
            return More.AppMain(this);
        }
    }

    class More
    {
        internal static int pageCounter; 
        internal static Terminal! terminal; 
        internal static long fileSize; 
        internal static long bytesRead; 
        
        internal static int GetKey(KeyboardDeviceContract.Imp! keyboard)
        {
            keyboard.SendGetKey();
            switch receive {
                case keyboard.AckKey(ikey):
                    return (int) ikey;
                    break;
                case keyboard.NakKey():
                    return -1;
                    break;
                case keyboard.ChannelClosed():
                    Tracing.Log(Tracing.Debug, "Keyboard channel closed");
                    return -1;
           }
           return -1;
        }
        
        [ Conditional("DEBUG_MORE") ]
        static void DebugPrint(string format, params object [] args)
        {
            DebugStub.Print(String.Format(format, args));
        }

        [ Conditional("DEBUG_MORE") ]
        static void DebugWriteLine(string format, params object [] args)
        {
            DebugPrint(format, args);
            DebugStub.Print("\n");
        }

        [ Conditional("DEBUG_MORE") ]
        static void DebugReadLine()
        {
            Console.ReadLine();
        }

        internal static int AppMain(Parameters! config) {
            // constants

            //int szLineCount = 0;
            int displayfile = 0;
            int consoleWidth = 0;
            int consoleHeight = 0;
            pageCounter = 0; 
            
            terminal = new Terminal(); 
            
            DirectoryServiceContract.Imp dsRoot = (config.nsRef).Acquire(); 
            if (dsRoot == null) { 
                throw new Exception("Unable to acquire handle to the Directory Service root"); 
            } 

            dsRoot.RecvSuccess();

            KeyboardDeviceContract.Imp keyboard = (config.keyboardRef).Acquire(); 
                if (dsRoot == null) { 
                throw new Exception("Unable to acquire handle to the keyboard"); 
            } 
            keyboard.RecvSuccess();

            // FIXFIX need to dynamically get  window size 
            consoleHeight = 47; // allow for "activity" line on top of Singularity console....
            consoleWidth = 79;

            assume config.fileSet != null;
            string szSrcLine;
            bool ok; 
            if (config.fileSet.Length <= 0 ) { 
                //stdin 
                while ( (szSrcLine = Console.ReadLine()) != null ) {
                    ok = PageText(szSrcLine, keyboard, consoleHeight, consoleWidth, true);
                    if (!ok) break; 
                } 
                delete keyboard; 
                delete dsRoot; 
                return 0; 
            } 
            
            for (displayfile = 0; displayfile < config.fileSet.Length; displayfile++) {
                string filename = config.fileSet[displayfile];
                if (filename == null) continue; 

                // check that file exists
                ErrorCode error; 
                NodeType nodeType; 
                ok  = FileUtils.GetAttributes(filename, dsRoot, out fileSize, out nodeType, out  error);
                if (ok) {
                    if ( nodeType == NodeType.IoMemory || nodeType == NodeType.File )  {
                        // Open file RO for now
                        FileStream fsInput = new FileStream(dsRoot, filename, FileMode.Open, FileAccess.Read);
                        StreamReader srInput = new StreamReader(fsInput);
                        while ((szSrcLine = srInput.ReadLine()) != null)
                        {
                            ok = PageText(szSrcLine, keyboard, consoleHeight, consoleWidth, false);
                            if (!ok) break; 
                        }
                        srInput.Close();
                        fsInput.Close();

                        // Display the file by pages
                        // TODO: change PageText to return between pages so can recheck for
                        //       screen dimension changes (?)

                        if (displayfile <= config.fileSet.Length - 2) // offset by 2 due to index offset plus increment hasn't happened yet
                        {
                            Console.WriteLine("");
                            Console.Write("-- <ENTER> for next file --");
                            int key;
                            do {
                                key = Console.Read();
                                if (key == -1) {
                                    delete keyboard;
                                    delete dsRoot; 
                                    return 0;
                                }
                            } while ((char)key != '\n');
                        }
                    }
                    else if (nodeType == NodeType.Directory) {
                        // check that it's not a directory
                        Console.WriteLine("\n{0} - is a directory.", filename);
                    }
                } // ok 
                else { 
                    // not a directory, but file doesn't exist, print error message
                    Console.WriteLine("\n{0} - file not found.", filename);
                }
            }
            delete keyboard;
            delete dsRoot; 
            return 0;
        }

        //////////////////////////////////////////////////////////////
        //  Write out prompt and wait for input
        //  After acquiring the input remove the prompt from the console
        //////////////////////////////////////////////////////////////
        
        static int GetInput( KeyboardDeviceContract.Imp! keyboard, 
                int PAGELENGTH, bool processingStdin)
        {
            double temp = 0; 
            int counter = 0;
            int key; 
            if (processingStdin) {  
                Console.Write("-- <more> -- ");
                key = GetKey(keyboard);
                if ((char)key == '\n' ) Console.Write("\n"); 
            } else {
                temp  =  (double) (bytesRead * 100) / (double)fileSize; 
                Console.Write("-- <more> ({0}%) -- ", (int)temp );
                key = Console.Read();
            } 
            // get rid of the -- <ENTER> line 
            if ((char)key == '\n' ){ 
                terminal.GenerateAndSendEscapeSequence(Pipe.EscapeCodes.UP); 
            }
            Console.Write("\r"); 
            terminal.GenerateAndSendEscapeSequence(Pipe.EscapeCodes.ERASE_FROM_CURSOR); 
            
            if (key == -1) return -1;
            switch (Char.ToLower((char)key )) {
                case 'q':
                    return -1;
                default:
                    if ((char)key == '\n') counter = PAGELENGTH ; // advance by 1
                    break;
            }
            return counter;
        }
        
        // PageText - display text a page at a time
        static bool PageText(string! workString,  KeyboardDeviceContract.Imp! keyboard, 
                int PAGELENGTH, int PAGEWIDTH, bool processingStdin)
        {
            string[] lines; 
            
            int remainder = 0;
            int linesinline = 0;

            // Split the line into the requisite number of lines
            // needed based on page width
            
            if ( workString.Length <= PAGEWIDTH ) {
                lines = new string[1];
                lines[0] = workString; 
            }
            else { 
                int fullLines   = workString.Length / PAGEWIDTH;
                remainder       = workString.Length % PAGEWIDTH;
                linesinline     = fullLines; 
                
                if (remainder != 0) linesinline++;
                lines = new string[linesinline];
                
                int pos = 0; 
                for (int i=0; i < fullLines; i++) {
                    lines[i] = workString.Substring(pos,PAGEWIDTH);
                    pos += PAGEWIDTH; 
                }
                if (remainder != 0) {
                    lines[fullLines] = workString.Substring(pos);
                }
            }

            // write the lines out pausing where necessary
            for (int i=0; i < lines.Length; i++) {
                string s = lines[i]; 
                assert s!= null; 
                Console.WriteLine(s); 
                pageCounter++; 
                bytesRead += s.Length; 
                if (pageCounter >= PAGELENGTH) {
                    pageCounter  = GetInput(keyboard, PAGELENGTH, processingStdin); 
                    if (pageCounter == -1) return false; 
                }
            }
            lines = null; 
            return true; 
        } // end of PageText
    }
}
