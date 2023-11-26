/*
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
using System.ComponentModel;
using System.Collections.Generic;
using Meridian59.Common.Enums;
using Meridian59.Common.Constants;

namespace Meridian59.Data.Models
{
    /// <summary>
    /// A model for a trade-object used in NPC offers with price.
    /// </summary>
    [Serializable]
    public class TradeOfferObject : ObjectBase
    {        
        public const string PROPNAME_PLAT = "Platinum";
        public const string PROPNAME_SHILLS = "Shillings";
        public const string PROPNAME_SOULS = "Souls";

        protected uint plat;
        protected uint shills;
        protected uint souls;

        public override int ByteLength
        { 
            get { 
                return base.ByteLength + TypeSizes.INT + TypeSizes.INT + TypeSizes.INT; 
            }
        }
    
        public override int ReadFrom(byte[] Buffer, int StartIndex = 0)
        {
            int cursor = StartIndex;

            cursor += base.ReadFrom(Buffer, cursor);

            shills = BitConverter.ToUInt32(Buffer, cursor);
            cursor += TypeSizes.INT;

            plat = BitConverter.ToUInt32(Buffer, cursor);
            cursor += TypeSizes.INT;

            souls = BitConverter.ToUInt32(Buffer, cursor);
            cursor += TypeSizes.INT;

            return cursor - StartIndex; 
        }

        public override int WriteTo(byte[] Buffer, int StartIndex = 0)
        {
            int cursor = StartIndex;
            
            cursor += base.WriteTo(Buffer, cursor);

            Array.Copy(BitConverter.GetBytes(shills), 0, Buffer, cursor, TypeSizes.INT);
            cursor += TypeSizes.INT;

            Array.Copy(BitConverter.GetBytes(plat), 0, Buffer, cursor, TypeSizes.INT);
            cursor += TypeSizes.INT;

            Array.Copy(BitConverter.GetBytes(souls), 0, Buffer, cursor, TypeSizes.INT);
            cursor += TypeSizes.INT;

            return cursor - StartIndex;
        }

        public override unsafe void ReadFrom(ref byte* Buffer)
        {
            base.ReadFrom(ref Buffer);

            shills = *((uint*)Buffer);
            Buffer += TypeSizes.INT;

            plat = *((uint*)Buffer);
            Buffer += TypeSizes.INT;

            souls = *((uint*)Buffer);
            Buffer += TypeSizes.INT;
        }

        public override unsafe void WriteTo(ref byte* Buffer)
        {
            base.WriteTo(ref Buffer);

            *((uint*)Buffer) = shills;
            Buffer += TypeSizes.INT;

            *((uint*)Buffer) = plat;
            Buffer += TypeSizes.INT;

            *((uint*)Buffer) = souls;
            Buffer += TypeSizes.INT;
        }

        /// <summary>
        /// Per unit price of the object.
        /// </summary>
        public uint Plat
        {
            get { return plat; }
            set
            {
                if (plat != value)
                {
                    plat = value;
                    RaisePropertyChanged(new PropertyChangedEventArgs(PROPNAME_PLAT));
                }
            }
        }

        /// <summary>
        /// Per unit price of the object.
        /// </summary>
        public uint Shills
        {
            get { return shills; }
            set
            {
                if (shills != value)
                {
                    shills = value;
                    RaisePropertyChanged(new PropertyChangedEventArgs(PROPNAME_SHILLS));
                }
            }
        }

        /// <summary>
        /// Per unit price of the object.
        /// </summary>
        public uint Souls
        {
            get { return souls; }
            set
            {
                if (souls != value)
                {
                    souls = value;
                    RaisePropertyChanged(new PropertyChangedEventArgs(PROPNAME_SOULS));
                }
            }
        }

        /// <summary>
        /// Empty constructor
        /// </summary>
        public TradeOfferObject() 
            : base() { }

        /// <summary>
        /// Constructor by values
        /// </summary>
        /// <param name="ID"></param>
        /// <param name="Count"></param>
        /// <param name="OverlayFileRID"></param>
        /// <param name="NameRID"></param>
        /// <param name="Flags"></param>
        /// <param name="LightingInfo"></param>
        /// <param name="FirstAnimationType"></param>
        /// <param name="ColorTranslation"></param>
        /// <param name="Effect"></param>
        /// <param name="Animation"></param>
        /// <param name="SubOverlays"></param>
        /// <param name="Plat"></param>
        /// <param name="Shills"></param>
        /// <param name="Souls"></param>
        public TradeOfferObject(
            uint ID,
            uint Count,
            uint OverlayFileRID,
            uint NameRID,
            uint Flags,
            LightingInfo LightingInfo,
            AnimationType FirstAnimationType,
            byte ColorTranslation,
            byte Effect,
            Animation Animation,
            IEnumerable<SubOverlay> SubOverlays,
            uint Plat = 0,
            uint Shills = 0,
            uint Souls = 0)
            : base(
                ID, Count, OverlayFileRID, NameRID, Flags,
                LightingInfo, FirstAnimationType,
                ColorTranslation, Effect, Animation, SubOverlays)
        {
            this.plat = Plat;
            this.shills = Shills;
            this.souls = Souls;
        }

        /// <summary>
        /// Constructor by managed parser
        /// </summary>
        /// <param name="Buffer"></param>
        /// <param name="StartIndex"></param>
        public TradeOfferObject(byte[] Buffer, int StartIndex = 0)
            : base(true, Buffer, StartIndex) { }

        /// <summary>
        /// Constructor by pointer parser
        /// </summary>
        /// <param name="Buffer"></param>
        public unsafe TradeOfferObject(ref byte* Buffer)
            : base(true, ref Buffer) { }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="RaiseChangedEvent"></param>
        public override void Clear(bool RaiseChangedEvent)
        {
            base.Clear(RaiseChangedEvent);

            if (RaiseChangedEvent)
            {
                Plat = 0;
                Shills = 0;
                Souls = 0;
            }
            else
            {
                plat = 0;
                Shills = 0;
                souls = 0;
            }
        }
    }
}
