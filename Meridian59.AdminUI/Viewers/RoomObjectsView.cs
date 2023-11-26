﻿/*
 Copyright (c) 2012 Clint Banzhaf
 This file is part of "Meridian59.AdminUI".

 "Meridian59.AdminUI" is free software: 
 You can redistribute it and/or modify it under the terms of the GNU General Public License as published by the Free Software Foundation, 
 either version 3 of the License, or (at your option) any later version.

 "Meridian59.AdminUI" is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY;
 without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
 See the GNU General Public License for more details.

 You should have received a copy of the GNU General Public License along with "Meridian59.AdminUI".
 If not, see http://www.gnu.org/licenses/.
*/

using System;
using System.ComponentModel;
using System.Windows.Forms;
using System.Threading;
using Meridian59.Data.Lists;
using Meridian59.Data.Models;
using Meridian59.Drawing2D;

namespace Meridian59.AdminUI
{
    /// <summary>
    /// View for Data.Lists.RoomObjectList
    /// </summary>
    public partial class RoomObjectsView : UserControl
    {
        /// <summary>
        /// The model to be shown in the View
        /// </summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden), DefaultValue(null), Browsable(true)]
        public RoomObjectList DataSource
        {
            get
            {
                if (gridRoomObjects.DataSource != null)
                    return (RoomObjectList)gridRoomObjects.DataSource;
                else
                    return null;
            }
            set
            {
                gridRoomObjects.DataSource = value;
            }
        }

        /// <summary>
        /// Constructor
        /// </summary>
        public RoomObjectsView()
        {
            InitializeComponent();                  
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        protected void gridRoomObjects_SelectionChanged(object sender, EventArgs e)
        {
            if (gridRoomObjects.SelectedRows.Count > 0 && 
                gridRoomObjects.SelectedRows[0].DataBoundItem != null)
            {
                RoomObject roomObject = (RoomObject)gridRoomObjects.SelectedRows[0].DataBoundItem;
               
                // update imagecomposer
                pictureBox.DataSource = roomObject;
                
                roomObject.SubOverlays.SyncContext = SynchronizationContext.Current;
                roomObject.MotionSubOverlays.SyncContext = SynchronizationContext.Current;

                gridSubOverlays.DataSource = roomObject.SubOverlays;
                gridMotionSubOverlays.DataSource = roomObject.MotionSubOverlays;
                avAnimation.DataSource = roomObject.Animation;
                avMotionAnimation.DataSource = roomObject.MotionAnimation;
            }          
        }

        protected void OnGridSubOverlaysSelectionChanged(object sender, EventArgs e)
        {
            SubOverlay selectedItem = gridSubOverlays.SelectedItem;
            
            avSubOverlayAnimation.DataSource = 
                (selectedItem != null) ? selectedItem.Animation : null;
        }

        protected void OnGridMotionSubOverlaysSelectionChanged(object sender, EventArgs e)
        {
            SubOverlay selectedItem = gridMotionSubOverlays.SelectedItem;

            avMotionSubOverlayAnimation.DataSource = 
                (selectedItem != null) ? selectedItem.Animation : null;
        }
    }
}
