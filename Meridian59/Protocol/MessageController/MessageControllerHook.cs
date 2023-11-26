﻿/*
 Copyright (c) 2012-2013 Clint Banzhaf
 This file is part of "Meridian59 .NET".

 "Meridian59 .NET" is free software: 
 You can redistribute it and/or modify it under the terms of the GNU General Public License as published by the Free Software Foundation, 
 either version 3 of the License, or (at your option) any later version.

 "Meridian59 .NET" is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY;
 without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
 See the GNU General Public License for more details.

 You should have received a copy of the GNU General Public License along with "Meridian59 .NET".
 If not, see http://www.gnu.org/licenses/.
*/

using System;
using System.IO;
using Meridian59.Protocol.GameMessages;
using Meridian59.Protocol.Events;
using Meridian59.Protocol.Enums;
using Meridian59.Common;

namespace Meridian59.Protocol
{
    /// <summary>
    /// MessageController for hook environment (read tx/rx)
    /// </summary>
    public class MessageControllerHook : MessageControllerClient
    {        
        /// <summary>
        /// Reader für outgoing traffic
        /// </summary>
        protected MessageParser sendReader;

        /// <summary>
        /// Reader for outgoing UDP traffic
        /// </summary>
        protected MessageParserUDP sendReaderUDP;

        /// <summary>
        /// Whether or not to advance hashtable when processing outgoing message
        /// </summary>
        protected bool advanceHashTable;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="StringResources">Resources</param>
        /// <param name="AdvanceHashTable"></param>
		public MessageControllerHook(StringDictionary StringResources, bool AdvanceHashTable)
            : base(StringResources) 
        {
            this.advanceHashTable = AdvanceHashTable;
        }

        /// <summary>
        /// Reset to initial state
        /// </summary>
        public override void Reset()
        {
            base.Reset();

            // detach events if old instance exists
            if (sendReader != null)
            {
                sendReader.MessageAvailable -= new MessageBufferEventHandler(OnSendReaderProcessMessage);
                sendReader.MismatchMessageLengthFound -= new MismatchMessageLengthFoundEventHandler(OnSendReaderMismatchMessageLengthFound);
                sendReader.SplittedMessageFound -= new SplittedMessageFoundEventHandler(OnSendReaderSplittedMessageFound);
                sendReader.CompletingSplittedMessage -= new CompletingSplittedMessageEventHandler(OnSendReaderCompletingSplittedMessage);
                sendReaderUDP.MessageAvailable -= new MessageBufferEventHandler(OnSendReaderProcessMessage);
            }

            // initialize a reader for outgoing traffic
            sendReader = new MessageParser();
            sendReader.MessageAvailable += new MessageBufferEventHandler(OnSendReaderProcessMessage);
            sendReader.MismatchMessageLengthFound += new MismatchMessageLengthFoundEventHandler(OnSendReaderMismatchMessageLengthFound);           
            sendReader.SplittedMessageFound += new SplittedMessageFoundEventHandler(OnSendReaderSplittedMessageFound);
            sendReader.CompletingSplittedMessage += new CompletingSplittedMessageEventHandler(OnSendReaderCompletingSplittedMessage);

            // for UDP
            sendReaderUDP = new MessageParserUDP();
            sendReaderUDP.MessageAvailable += new MessageBufferEventHandler(OnSendReaderProcessMessage);
        }

        /// <summary>
        /// Reads a received traffic chunk from byte[]
        /// </summary>
        /// <param name="Buffer">The chunk to read</param>
        /// <param name="MemoryAddress">The memory address to attach with this chunk</param>
        /// <param name="Available">Amounts of data available to read</param>
        public void ReadRecv(byte[] Buffer, IntPtr MemoryAddress, int Available)
        {
            recvReader.Read(Buffer, MemoryAddress, Available);
        }

        /// <summary>
        /// Reads a sent traffic chunk from byte[]
        /// </summary>
        /// <param name="Buffer"></param>
        /// <param name="MemoryAddress"></param>
        /// <param name="Available"></param>
        public void ReadSend(byte[] Buffer, IntPtr MemoryAddress, int Available)
        {
            sendReader.Read(Buffer, MemoryAddress, Available);
        }

        /// <summary>
        /// Reads a sent traffic chunk from stream
        /// </summary>
        /// <param name="Stream"></param>
        /// <param name="Available"></param>
        public void ReadSend(Stream Stream, int Available)
        {
            sendReader.Read(Stream, Available);
        }

        /// <summary>
        /// Reads a sent traffic chunk from byte[]
        /// </summary>
        /// <param name="Buffer"></param>
        /// <param name="MemoryAddress"></param>
        /// <param name="Available"></param>
        public void ReadSendUDP(byte[] Buffer, IntPtr MemoryAddress, int Available)
        {
            sendReaderUDP.Read(Buffer, MemoryAddress, Available);
        }

        /// <summary>
        /// Handle a message from the recv messagebuffer
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        protected void OnSendReaderProcessMessage(object sender, MessageBufferEventArgs e)
        {
            // set the direction as outgoing
            e.Direction = MessageDirection.ClientToServer;

            // TCP handling
            if (e.IsTCP)
            {
                if (e.Length > MessageHeader.Tcp.HEADERLENGTH)
                {
                    try
                    {
                        // parse the message
                        GameMessage typedMessage = ExtractMessage(e);
                        typedMessage.EncryptedPI = typedMessage.PI;
                        typedMessage.Header.MemoryStartAddress = e.MemoryAddress;

                        //CheckServerSave(typedMessage);

                        // Advance the hashtable if encryption is enabled (3. TX packet) and if it's not a blacklisted packet
                        if (CRCCreatorEnabled &&
                            advanceHashTable &&
                            ((MessageTypeGameMode)typedMessage.PI != MessageTypeGameMode.Blacklisted))

                            CRCCreator.AdvanceHashTable();

                        OnNewMessageAvailable(new GameMessageEventArgs(typedMessage));
                    }
                    catch (Exception Error)
                    {
                        byte[] dump = new byte[e.Length];
                        Array.Copy(e.MessageBuffer, 0, dump, 0, e.Length);
                        OnHandlerError(new HandlerErrorEventArgs(dump, Error.Message));
                    }
                }
                else
                {
                    OnEmptyPacketFound(new EmptyMessageFoundEventArgs());
                }
            }

            // UDP
            else
            {
                if (e.Length > MessageHeader.Udp.HEADERLENGTH)
                {
                    try
                    {
                        // parse the message
                        GameMessage typedMessage = ExtractMessage(e);
                        typedMessage.EncryptedPI = typedMessage.PI;
                        typedMessage.Header.MemoryStartAddress = e.MemoryAddress;

                        OnNewMessageAvailable(new GameMessageEventArgs(typedMessage));
                    }
                    catch (Exception Error)
                    {
                        byte[] dump = new byte[e.Length];
                        Array.Copy(e.MessageBuffer, 0, dump, 0, e.Length);
                        OnHandlerError(new HandlerErrorEventArgs(dump, Error.Message));
                    }
                }
            }
        }

        #region Event wrappers
        protected void OnSendReaderMismatchMessageLengthFound(object sender, MismatchMessageLengthFoundEventArgs e)
        {
            OnMismatchPacketLENFound(e);
        }
        protected void OnSendReaderCompletingSplittedMessage(object sender, CompletingSplittedMessageEventArgs e)
        {
            OnCompletingSplittedPacket(e);
        }

        protected void OnSendReaderSplittedMessageFound(object sender, SplittedMessageFoundEventArgs e)
        {
            OnSplittedPacketFound(e);
        }
        #endregion
    }
}
