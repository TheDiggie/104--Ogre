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

#if !VANILLA

using System;
using Meridian59.Common.Enums;
using Meridian59.Common.Constants;
using System.Text;

namespace Meridian59.Data.Models
{
   public class UserCommandSendPreferences : UserCommand
   {
      public override UserCommandType CommandType { get { return UserCommandType.SendPreferences; } }

      #region IByteSerializable
      public override int ByteLength
      {
         get
         {
            return TypeSizes.BYTE + TypeSizes.INT;
         }
      }

      public override int WriteTo(byte[] Buffer, int StartIndex = 0)
      {
         int cursor = StartIndex;

         Buffer[cursor] = (byte)CommandType;
         cursor++;

         ClientPreferences.WriteTo(Buffer, cursor);
         cursor += TypeSizes.INT;

         return cursor - StartIndex;
      }

      public override int ReadFrom(byte[] Buffer, int StartIndex = 0)
      {
         int cursor = StartIndex;

         if ((UserCommandType)Buffer[cursor] != CommandType)
            throw new Exception(ERRORWRONGTYPEBYTE);
         else
         {
            cursor++;
            ClientPreferences = new PreferencesFlags(Buffer, cursor);
            cursor += TypeSizes.INT;
         }

         return cursor - StartIndex;
      }
      #endregion

      public PreferencesFlags ClientPreferences;

      public UserCommandSendPreferences(PreferencesFlags ClientPreferences)
      {
         this.ClientPreferences = ClientPreferences;
      }

      public UserCommandSendPreferences(byte[] Buffer, int StartIndex = 0)
      {
         ReadFrom(Buffer, StartIndex);
      }
   }
}
#endif