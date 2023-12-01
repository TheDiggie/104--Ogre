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
using Meridian59.Common.Interfaces;
using Meridian59.Common.Enums;
using Meridian59.Common;

namespace Meridian59.Data.Models
{
    /// <summary>
    /// This is an abstract baseclass for UserCommand classes.
    /// UserCommands are some kind of "submessages", often used for user interactions.
    /// </summary>
    [Serializable]
    public abstract class UserCommand : IByteSerializable
    {
        #region Constants
        protected const string ERRORWRONGTYPEBYTE   = "Wrong 1.Byte (type) for this UserCommand";
        #endregion
        
        /// <summary>
        /// Type of this command
        /// </summary>
        public abstract UserCommandType CommandType { get; }
        
        #region IByteSerializable
        public abstract int ByteLength { get; }      
        public abstract int WriteTo(byte[] Buffer, int StartIndex=0);
        public abstract int ReadFrom(byte[] Buffer, int StartIndex=0);
        public byte[] Bytes {
            get
            {
                byte[] returnValue = new byte[ByteLength];
                WriteTo(returnValue);
                return returnValue;
            }

            set
            {
                ReadFrom(value);
            }
        }
        #endregion

        /// <summary>
        /// Parses a typed UserCommand subclass instance from a raw byte buffer.
        /// </summary>
        /// <param name="IsOutgoing">Outgoing (client->server) direction or not. This must be correct for some commands or will create parser errors.</param>
        /// <param name="StringResources">Reference to the string dictionary containing game strings.</param>
        /// <param name="Buffer">Buffer with bytes to read from</param>
        /// <param name="StartIndex">StartIndex for reading</param>
        /// <param name="Length">This is required to parse unknown UserCommandGeneric.</param>
        /// <returns>Subclass instance of UserCommand or UserCommandGeneric for unknown ones</returns>
        public static UserCommand ExtractCommand(
            bool IsOutgoing,
			StringDictionary StringResources, 
            byte[] Buffer, 
            int StartIndex,
            int Length)
        {
            UserCommand returnValue = null;

            // try to parse the command
            switch ((UserCommandType)Buffer[StartIndex])
            {
                case UserCommandType.LookPlayer:                                                        // 2
                    returnValue = new UserCommandLookPlayer(StringResources, Buffer, StartIndex);
                    break;

                case UserCommandType.ChangeURL:                                                         // 3
                    returnValue = new UserCommandChangeURL(Buffer, StartIndex);
                    break;

                case UserCommandType.Rest:                                                              // 5
                    returnValue = new UserCommandRest(Buffer, StartIndex);
                    break;

                case UserCommandType.Stand:                                                             // 6
                    returnValue = new UserCommandStand(Buffer, StartIndex);
                    break;
#if VANILLA
                case UserCommandType.Safety:                                                            // 7
                    returnValue = new UserCommandSafety(Buffer, StartIndex);
                    break;
#else
                case UserCommandType.ReqPreferences:                                                    // 7
                    returnValue = new UserCommandReqPreferences(Buffer, StartIndex);
                    break;
#endif
                case UserCommandType.Suicide:                                                           // 8
                    returnValue = new UserCommandSuicide(Buffer, StartIndex);
                    break;
#if !VANILLA
                case UserCommandType.SendPreferences:                                                   // 9
                    returnValue = new UserCommandSendPreferences(Buffer, StartIndex);
                    break;
#endif
                case UserCommandType.GuildInfo:                                                         // 11
                    returnValue = new UserCommandGuildInfo(Buffer, StartIndex);
                    break;

                case UserCommandType.Invite:                                                            // 12
                    returnValue = new UserCommandGuildInvite(Buffer, StartIndex);
                    break;

                case UserCommandType.Exile:                                                             // 13
                    returnValue = new UserCommandGuildExile(Buffer, StartIndex);
                    break;

                case UserCommandType.Renounce:                                                          // 14
                    returnValue = new UserCommandGuildRenounce(Buffer, StartIndex);
                    break;

                case UserCommandType.Abdicate:                                                          // 15
                    returnValue = new UserCommandGuildAbdicate(Buffer, StartIndex);
                    break;

                case UserCommandType.Vote:                                                              // 16
                    returnValue = new UserCommandGuildVote(Buffer, StartIndex);
                    break;

                case UserCommandType.SetRank:                                                           // 17
                    returnValue = new UserCommandGuildSetRank(Buffer, StartIndex);
                    break;

                case UserCommandType.GuildAsk:                                                          // 18
                    returnValue = new UserCommandGuildAsk(Buffer, StartIndex);
                    break;

                case UserCommandType.Disband:                                                           // 20
                    returnValue = new UserCommandGuildDisband(Buffer, StartIndex);
                    break;

                case UserCommandType.ReqGuildList:                                                      // 21
                    returnValue = new UserCommandGuildGuildListReq(Buffer, StartIndex);
                    break;

                case UserCommandType.GuildList:                                                         // 22
                    returnValue = new UserCommandGuildGuildList(Buffer, StartIndex);
                    break;

                case UserCommandType.MakeAlliance:                                                      // 23
                    returnValue = new UserCommandGuildMakeAlliance(Buffer, StartIndex);
                    break;

                case UserCommandType.EndAlliance:                                                       // 24
                    returnValue = new UserCommandGuildEndAlliance(Buffer, StartIndex);
                    break;

                case UserCommandType.MakeEnemy:                                                         // 25
                    returnValue = new UserCommandGuildMakeEnemy(Buffer, StartIndex);
                    break;

                case UserCommandType.EndEnemy:                                                          // 26
                    returnValue = new UserCommandGuildEndEnemy(Buffer, StartIndex);
                    break;

                case UserCommandType.GuildHalls:                                                        // 27
                    returnValue = new UserCommandGuildHalls(Buffer, StartIndex);
                    break;

                case UserCommandType.AbandonGuildHall:                                                  // 28
                    returnValue = new UserCommandGuildAbandonHall(Buffer, StartIndex);
                    break;

                case UserCommandType.GuildRent:                                                         // 29
                    returnValue = new UserCommandGuildRent(Buffer, StartIndex);
                    break;

                case UserCommandType.GuildSetPassword:                                                  // 30
                    returnValue = new UserCommandGuildSetPassword(Buffer, StartIndex);
                    break;

                case UserCommandType.GuildShield:                                                       // 31
                    if (IsOutgoing) returnValue = new UserCommandGuildShieldInfoReq(Buffer, StartIndex);
                    else returnValue = new UserCommandGuildShieldInfo(Buffer, StartIndex);
                    break;

                case UserCommandType.GuildShields:                                                      // 32
                    if (IsOutgoing) returnValue = new UserCommandGuildShieldListReq(Buffer, StartIndex);
                    else returnValue = new UserCommandGuildShieldList(Buffer, StartIndex);
                    break;

                case UserCommandType.ClaimShield:                                                       // 33
                    returnValue = new UserCommandClaimShield(Buffer, StartIndex);
                    break;
#if !VANILLA
                case UserCommandType.ReceivePreferences:                                                // 34
                    returnValue = new UserCommandReceivePreferences(Buffer, StartIndex);
                    break;
#endif
                case UserCommandType.Deposit:                                                           // 35
                    returnValue = new UserCommandDeposit(Buffer, StartIndex);
                    break;

                case UserCommandType.WithDraw:                                                          // 36
                    returnValue = new UserCommandWithDraw(Buffer, StartIndex);
                    break;

                case UserCommandType.Balance:                                                           // 37
                    returnValue = new UserCommandBalance(Buffer, StartIndex);
                    break;
#if !VANILLA && !OPENMERIDIAN
                case UserCommandType.GuildShieldError:                                                  // 38
                    returnValue = new UserCommandGuildShieldError(StringResources, Buffer, StartIndex);
                    break;
#endif
                case UserCommandType.Appeal:                                                            // 40
                    returnValue = new UserCommandAppeal(Buffer, StartIndex);
                    break;
#if !VANILLA
                case UserCommandType.Time:                                                              // 60
                    returnValue = new UserCommandTime(Buffer, StartIndex);
                    break;
#endif
                default:
                    returnValue = new UserCommandGeneric(Buffer, StartIndex, Length);
                    break;
            }

            return returnValue;
        }
    }
}
