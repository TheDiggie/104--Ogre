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
using System.ComponentModel;
using Meridian59.Common.Interfaces;
using Meridian59.Files.BGF;
using Meridian59.Files;

namespace Meridian59.Data.Models
{
    /// <summary>
    /// Extends ResourceID with a BgfFile resource property.
    /// </summary>
    [Serializable]
    public class ResourceIDBGF : ResourceID, IUpdatable<ResourceIDBGF>, IResourceResolvable
    {
        #region Constants
        public const string PROPNAME_RESOURCE = "Resource";
        #endregion

        #region Fields
        protected BgfFile resource;
        #endregion

        #region Properties
        public BgfFile Resource
        {
            get { return resource; }
            set
            {
                if (this.resource != value)
                {
                    this.resource = value;
                    RaisePropertyChanged(new PropertyChangedEventArgs(PROPNAME_RESOURCE));
                }
            }
        }

        #endregion

        #region Constructors
        public ResourceIDBGF()
            : base()
        {
            Clear(false);
        }

        public ResourceIDBGF(uint ResourceID)
            : base(ResourceID) { }

        public ResourceIDBGF(byte[] Buffer, int StartIndex = 0)
            : base(Buffer, StartIndex) { }

        public unsafe ResourceIDBGF(ref byte* Buffer)
            : base(ref Buffer) { }
        #endregion
        
        #region IClearable
        public override void Clear(bool RaiseChangedEvent)
        {
            base.Clear(RaiseChangedEvent);

            if (RaiseChangedEvent)
            {
                Resource = null;
            }
            else
            {
                resource = null;
            }
        }
        #endregion

        #region IUpdatable
        public virtual void UpdateFromModel(ResourceIDBGF Model, bool RaiseChangedEvent)
        {
            base.UpdateFromModel(Model, RaiseChangedEvent);

            if (RaiseChangedEvent)
            {
                Resource = Model.Resource;               
            }
            else
            {
                resource = Model.Resource;
            }
        }
        #endregion

        #region IResourceResolvable
        public virtual void ResolveResources(ResourceManager M59ResourceManager, bool RaiseChangedEvent)
        {
            if (name != String.Empty)
            {
                if (RaiseChangedEvent)
                {
                    Resource = M59ResourceManager.GetObject(name);
                }
                else
                {
                    resource = M59ResourceManager.GetObject(name);
                }               
            }
        }
        #endregion
    }
}
