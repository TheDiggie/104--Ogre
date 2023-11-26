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
using System.Collections.Generic;
using Meridian59.Common;
using Meridian59.Common.Enums;
using Meridian59.Data.Models;
using Meridian59.Files.BGF;

// Switch FP precision based on architecture
#if X64
using Real = System.Double;
#else 
using Real = System.Single;
#endif

namespace Meridian59.Drawing2D
{
    /// <summary>
    /// Info for rendering this object to an image
    /// </summary>
    public class RenderInfo
    {
        public const Real DEFAULTQUALITY = 1.0f;
        public const Real QUALITYBASE = 1024.0f;

        protected V2 dimension;
        protected V2 uvstart;
        protected V2 uvend;
        protected V2 origin;
        protected V2 size;
        protected V2 worldsize;

        /// <summary>
        /// Overall size of composed image
        /// </summary>
        public V2 Dimension { get { return dimension; } protected set { dimension = value; } }

        /// <summary>
        /// Size of the whole object in client world-scale
        /// </summary>
        public V2 WorldSize { get { return worldsize; } protected set { worldsize = value; } }

        /// <summary>
        /// UV coordinates of start
        /// </summary>
        public V2 UVStart { get { return uvstart; } protected set { uvstart = value; } }

        /// <summary>
        /// UV coordinates of end
        /// </summary>
        public V2 UVEnd { get { return uvend; } protected set { uvend = value; } }

        /// <summary>
        /// Quality of the image. Using different quality values does not necessarily
        /// affect image quality. More likely this specifies an upper maximum of quality.
        /// By default this is 1.0f which refers to a maximum of 1024x1024 imagesize.
        /// Set this to 0.5f for maximum of 512x512, to 0.25f for 256x256 and 2.0f for 2048x2048.
        /// Many composed objects however can be rendered with much less size without downscaling parts
        /// and therefore will be created at these lower sizes, no matter how you set this.
        /// </summary>
        public Real Quality { get; protected set; }

        /// <summary>
        /// Scale between Dimension property and world size of this object 
        /// World size is at shrink = 1
        /// </summary>
        public Real Scaling { get; protected set; }

        /// <summary>
        /// Maximum appearing Shrink value
        /// </summary>
        public uint MaxShrink { get; protected set; }

        /// <summary>
        /// The origin of main image at current scale
        /// </summary>
        public V2 Origin { get { return origin; } protected set { origin = value; } }

        /// <summary>
        /// The size of main image at current scale
        /// </summary>
        public V2 Size { get { return size; } protected set { size = value; } }

        /// <summary>
        /// The main image
        /// </summary>
        public BgfBitmap Bgf { get; protected set; }

        /// <summary>
        /// The color translation for main image,
        /// may differ from colortranslation of mainoverlay 
        /// if RootHotspotIndex is set.
        /// </summary>
        public byte BgfColor { get; protected set; }

        /// <summary>
        /// Suboverlays
        /// </summary>
        public List<SubOverlay.RenderInfo> SubBgf { get; protected set; }

        /// <summary>
        /// Constructor
        /// </summary>
        public RenderInfo()
        {
            SubBgf = new List<SubOverlay.RenderInfo>();
            Clear();
        }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="Data"></param>
        /// <param name="ApplyYOffset"></param>
        /// <param name="RootHotspotIndex"></param>
        /// <param name="Quality"></param>
        /// <param name="ScalePow2"></param>
        /// <param name="Width"></param>
        /// <param name="Height"></param>
        /// <param name="CenterVertical"></param>
        /// <param name="CenterHorizontal"></param>
        /// <param name="IsCustomShrink"></param>
        /// <param name="CustomShrink"></param>
        public RenderInfo(
            ObjectBase Data,
            bool ApplyYOffset = true,
            byte RootHotspotIndex = 0,
            Real Quality = DEFAULTQUALITY,
            bool ScalePow2 = false,
            uint Width = 0,
            uint Height = 0,
            bool CenterVertical = false,
            bool CenterHorizontal = false,
            bool IsCustomShrink = false,
            Real CustomShrink = 1.0f)
        {
            SubBgf = new List<SubOverlay.RenderInfo>();
            Refresh(Data, ApplyYOffset, RootHotspotIndex, Quality, ScalePow2, Width, Height, CenterVertical, CenterHorizontal, IsCustomShrink, CustomShrink);
        }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="Data"></param>
        /// <param name="UseViewerFrame"></param>
        /// <param name="ApplyYOffset"></param>
        /// <param name="RootHotspotIndex"></param>
        /// <param name="Quality"></param>
        /// <param name="ScalePow2"></param>
        /// <param name="Width"></param>
        /// <param name="Height"></param>
        /// <param name="CenterVertical"></param>
        /// <param name="CenterHorizontal"></param>
        /// <param name="IsCustomShrink"></param>
        /// <param name="CustomShrink"></param>
        public RenderInfo(
            RoomObject Data,
            bool UseViewerFrame = true,
            bool ApplyYOffset = true,
            byte RootHotspotIndex = 0,
            Real Quality = DEFAULTQUALITY,
            bool ScalePow2 = false,
            uint Width = 0,
            uint Height = 0,
            bool CenterVertical = false,
            bool CenterHorizontal = false,
            bool IsCustomShrink = false,
            Real CustomShrink = 1.0f)
        {
            SubBgf = new List<SubOverlay.RenderInfo>();
            Refresh(Data, UseViewerFrame, ApplyYOffset, RootHotspotIndex, Quality, ScalePow2, Width, Height, CenterVertical, CenterHorizontal, IsCustomShrink, CustomShrink);
        }

        /// <summary>
        /// Creates RenderInfo for an ObjectBase
        /// </summary>
        /// <param name="Data"></param>
        /// <param name="ApplyYOffset"></param>
        /// <param name="RootHotspotIndex"></param>
        /// <param name="Quality"></param>
        /// <param name="ScalePow2"></param>
        /// <param name="Width"></param>
        /// <param name="Height"></param>
        /// <param name="CenterVertical"></param>
        /// <param name="CenterHorizontal"></param>
        /// <param name="IsCustomShrink"></param>
        /// <param name="CustomShrink"></param>
        public void Refresh(
            ObjectBase Data,
            bool ApplyYOffset = true,
            byte RootHotspotIndex = 0,
            Real Quality = DEFAULTQUALITY,
            bool ScalePow2 = false,
            uint Width = 0,
            uint Height = 0,
            bool CenterVertical = false,
            bool CenterHorizontal = false,
            bool IsCustomShrink = false,
            Real CustomShrink = 1.0f)
        {
            Clear();
            Calculate(Data, Data.ViewerFrame, true, ApplyYOffset, RootHotspotIndex, Quality, ScalePow2, Width, Height, CenterVertical, CenterHorizontal, IsCustomShrink, CustomShrink);
        }

        /// <summary>
        /// Creates RenderInfo for a RoomObject
        /// </summary>
        /// <param name="Data"></param>
        /// <param name="UseViewerFrame"></param>
        /// <param name="ApplyYOffset"></param>
        /// <param name="RootHotspotIndex"></param>
        /// <param name="Quality"></param>
        /// <param name="ScalePow2"></param>
        /// <param name="Width"></param>
        /// <param name="Height"></param>
        /// <param name="CenterVertical"></param>
        /// <param name="CenterHorizontal"></param>
        /// <param name="IsCustomShrink"></param>
        /// <param name="CustomShrink"></param>
        public void Refresh(
            RoomObject Data,
            bool UseViewerFrame = true,
            bool ApplyYOffset = true,
            byte RootHotspotIndex = 0,
            Real Quality = DEFAULTQUALITY,
            bool ScalePow2 = false,
            uint Width = 0,
            uint Height = 0,
            bool CenterVertical = false,
            bool CenterHorizontal = false,
            bool IsCustomShrink = false,
            Real CustomShrink = 1.0f)
        {
            Clear();

            BgfBitmap mainFrame = Data.ViewerFrame;
            if (!UseViewerFrame)
                mainFrame = Data.FrontFrame;

            Calculate(Data, mainFrame, UseViewerFrame, ApplyYOffset, RootHotspotIndex, Quality, ScalePow2, Width, Height, CenterVertical, CenterHorizontal, IsCustomShrink, CustomShrink);
        }

        protected void Calculate(
            ObjectBase Data,
            BgfBitmap MainFrame,
            bool UseViewerFrame = true,
            bool ApplyYOffset = true,
            byte RootHotspotIndex = 0,
            Real Quality = DEFAULTQUALITY,
            bool ScalePow2 = false,
            uint Width = 0,
            uint Height = 0,
            bool CenterVertical = false,
            bool CenterHorizontal = false,
            bool IsCustomShrink = false,
            Real CustomShrink = 1.0f)
        {
            BgfBitmap mainFrame = MainFrame;
            BgfFile mainResource = Data.Resource;
            byte mainColor = Data.ColorTranslation;
            BgfBitmap subOvFrame;
            BgfBitmapHotspot subOvHotspot;
            SubOverlay subOvParent;
            BgfBitmap subOvParentFrame;
            BgfBitmapHotspot subOvParentHotspot;
            bool rootSpotFound = false;

            // use custom compose root (suboverlay=mainoverlay)
            // if it's not found, fallback to full compose
            if (RootHotspotIndex > 0)
            {
                SubOverlay subOv = Data.GetSubOverlayByHotspot(RootHotspotIndex);

                if (subOv != null)
                {
                    rootSpotFound = true;

                    if (UseViewerFrame)
                        mainFrame = subOv.ViewerFrame;
                    else
                        mainFrame = subOv.FrontFrame;

                    mainResource = subOv.Resource;
                    mainColor = subOv.ColorTranslation;
                }
            }

            if (mainFrame != null && mainResource != null)
            {
                Bgf = mainFrame;
                BgfColor = mainColor;
                size.X = (Real)mainFrame.Width / (Real)mainResource.ShrinkFactor;
                size.Y = (Real)mainFrame.Height / (Real)mainResource.ShrinkFactor;
                origin.X = (Real)mainFrame.XOffset / (Real)mainResource.ShrinkFactor;
                origin.Y = (Real)mainFrame.YOffset / (Real)mainResource.ShrinkFactor;
                MaxShrink = mainResource.ShrinkFactor;

                // used to calculate the boundingbox
                V2 min = new V2(Origin.X, Origin.Y);
                V2 max = new V2(Size.X, Size.Y);
                if (ApplyYOffset)
                {
                    if (origin.X > 0.0f) max.X += origin.X;
                    if (origin.Y > 0.0f) max.Y += origin.Y;
                }
                else
                  max += origin;

                Real x, y;

                // walk suboverlay structure
                foreach (SubOverlay subOv in Data.CurrentSubOverlays)
                {
                    if (UseViewerFrame)
                    {
                        subOvFrame = subOv.ViewerFrame;
                        subOvHotspot = subOv.ViewerHotspot;
                        subOvParent = subOv.ViewerParent;
                    }
                    else
                    {
                        subOvFrame = subOv.FrontFrame;
                        subOvHotspot = subOv.FrontHotspot;
                        subOvParent = subOv.FrontParent;
                    }

                    bool isSubRoot = (subOvParent != null && subOvParent.HotSpot == RootHotspotIndex);

                    if (subOv.Resource != null &&
                        subOvFrame != null &&
                        subOvHotspot != null &&
                        (RootHotspotIndex <= 0 || !rootSpotFound || isSubRoot))
                    {
                        SubOverlay.RenderInfo subOvInfo = new SubOverlay.RenderInfo();
                        
                        // save subov & bitmap
                        subOvInfo.SubOverlay = subOv;
                        subOvInfo.Bgf = subOvFrame;

                        // calculate the size of this suboverlay
                        subOvInfo.Size.X = (Real)subOvFrame.Width / (Real)subOv.Resource.ShrinkFactor;
                        subOvInfo.Size.Y = (Real)subOvFrame.Height / (Real)subOv.Resource.ShrinkFactor;

                        // update maxshrink if greater
                        if (subOv.Resource.ShrinkFactor > MaxShrink)
                            MaxShrink = subOv.Resource.ShrinkFactor;

                        // CASE 1: SubOverlay on mainoverlay
                        if (subOvParent == null || isSubRoot)
                        {
                            // calculate the origin of this suboverlay on the mainoverlay
                            subOvInfo.Origin.X = mainFrame.XOffset
                                + ((Real)subOvHotspot.X)
                                + ((Real)subOvFrame.XOffset);

                            subOvInfo.Origin.Y = mainFrame.YOffset
                                + ((Real)subOvHotspot.Y)
                                + ((Real)subOvFrame.YOffset);

                            subOvInfo.Origin.X /= mainResource.ShrinkFactor;
                            subOvInfo.Origin.Y /= mainResource.ShrinkFactor;

                            // determine type of hotspot
                            if (subOvHotspot.Index < 0)
                                subOvInfo.HotspotType = HotSpotType.HOTSPOT_UNDER;
                            else
                                subOvInfo.HotspotType = HotSpotType.HOTSPOT_OVER;
                        }

                        // CASE 2: SubOverlay on SubOverlay on MainOverlay
                        else
                        {
                            if (UseViewerFrame)
                            {
                                subOvParentFrame = subOvParent.ViewerFrame;
                                subOvParentHotspot = subOvParent.ViewerHotspot;
                            }
                            else
                            {
                                subOvParentFrame = subOvParent.FrontFrame;
                                subOvParentHotspot = subOvParent.FrontHotspot;
                            }

                            if (subOvParentHotspot != null &&
                                subOvParentFrame != null &&
                                subOvParent.Resource != null)
                            {
                                // calculate the origin of this suboverlay on the suboverlay on the mainoverlay
                                subOvInfo.Origin.X = 
                                    (mainFrame.XOffset +
                                    (Real)subOvParentHotspot.X +
                                    (Real)subOvParentFrame.XOffset) / (Real)mainResource.ShrinkFactor;

                                subOvInfo.Origin.X +=
                                    ((Real)subOvHotspot.X +
                                    (Real)subOvFrame.XOffset) / (Real)subOvParent.Resource.ShrinkFactor;

                                subOvInfo.Origin.Y = 
                                    (mainFrame.YOffset +
                                    (Real)subOvParentHotspot.Y +
                                    (Real)subOvParentFrame.YOffset) / (Real)mainResource.ShrinkFactor;

                                subOvInfo.Origin.Y +=
                                    ((Real)subOvHotspot.Y +
                                    (Real)subOvFrame.YOffset) / (Real)subOvParent.Resource.ShrinkFactor;

                                // determine type of nested hotspot
                                if (subOvParentHotspot.Index > 0 && subOvHotspot.Index > 0)
                                    subOvInfo.HotspotType = HotSpotType.HOTSPOT_OVEROVER;

                                else if (subOvParentHotspot.Index > 0 && subOvHotspot.Index < 0)
                                    subOvInfo.HotspotType = HotSpotType.HOTSPOT_OVERUNDER;

                                else if (subOvParentHotspot.Index < 0 && subOvHotspot.Index > 0)
                                    subOvInfo.HotspotType = HotSpotType.HOTSPOT_UNDEROVER;

                                else if (subOvParentHotspot.Index < 0 && subOvHotspot.Index < 0)
                                    subOvInfo.HotspotType = HotSpotType.HOTSPOT_UNDERUNDER;
                            }
                        }

                        // update max boundingbox
                        if (subOvInfo.Origin.X < min.X)
                            min.X = subOvInfo.Origin.X;

                        if (subOvInfo.Origin.Y < min.Y)
                            min.Y = subOvInfo.Origin.Y;

                        x = subOvInfo.Origin.X + subOvInfo.Size.X;
                        y = subOvInfo.Origin.Y + subOvInfo.Size.Y;

                        if (x > max.X)
                            max.X = x;

                        if (y > max.Y)
                            max.Y = y;

                        // save info for this suboverlay
                        SubBgf.Add(subOvInfo);
                    }
                }

                // get dimension from boundingbox
                dimension.X = Math.Abs(max.X - min.X);
                dimension.Y = Math.Abs(max.Y - min.Y);

                // move all origins so minimum hits 0/0
                // preparation for drawing (pixel origin is 0/0)
                Translate(-min);

                // get the center of the dimension box
                // this is also the center of our image after the translate above
                V2 bbCenter = dimension * 0.5f;

                // get the center of the main overlay
                V2 mainOriginCenter = Origin + (Size * 0.5f);

                // move the x center of the main overlay to the x center of dimensionbox
                V2 centerMove = new V2(bbCenter.X - mainOriginCenter.X, 0.0f);
                Translate(centerMove);

                // since this moves a part outside the dimension box
                // we need to add this size of the move to the dimension, 
                // to the right AND left side, so our centering above stays centered
                // then we remove to the center.
                V2 center = new V2(Math.Abs(centerMove.X), 0.0f);
                dimension.X += center.X * 2.0f;
                Translate(center);

                // scale so highest resolution resource has 1:1 ratio (no downscale)
                // or to user provided shrink value
                Scale(IsCustomShrink ? CustomShrink : (Real)MaxShrink);

                // user defined sizes
                if (Width > 0 && Height > 0)
                {
                    // use user given size
                    V2 userSize = new V2(Width, Height);

                    // scale so we use at least all pixels either from upscaled width or height
                    ScaleToBox(userSize, CenterHorizontal, CenterVertical, !IsCustomShrink);
                }
                else
                {
                    Real maxQuality = Quality * QUALITYBASE;
                    Real ratioX = maxQuality / dimension.X;
                    Real ratioY = maxQuality / dimension.Y;

                    if (ratioX <= ratioY && ratioX < 1.0f)
                        Scale(ratioX);
                    else if (ratioX > ratioY && ratioY < 1.0f)
                        Scale(ratioY);

                    // scale up to pow2 if set
                    if (ScalePow2)
                    {
                        // get next power of 2 size
                        V2 pow2Size = new V2(
                            MathUtil.NextPowerOf2((uint)System.Math.Ceiling(dimension.X)),
                            MathUtil.NextPowerOf2((uint)System.Math.Ceiling(dimension.Y)));

                        // scale so we use at least all pixels either from upscaled width or height
                        ScaleToBox(pow2Size, CenterHorizontal, CenterVertical, !IsCustomShrink);
                    }
                }

                // calculate the world-size (shrink=1) of the composed object from dimension, uv-coordinates and scaling
                worldsize.X = (uvend.X * dimension.X) / Scaling;
                worldsize.Y = (uvend.Y * dimension.Y) / Scaling;
            }
        }

        /// <summary>
        /// Resets the data
        /// </summary>
        protected void Clear()
        {
            dimension.X = 0.0f;
            dimension.Y = 0.0f;

            uvstart.X = 0.0f;
            uvstart.Y = 0.0f;

            uvend.X = 1.0f;
            uvend.Y = 1.0f;

            Quality = 1.0f;
            Scaling = 1.0f;
            MaxShrink = 1;

            origin.X = 0.0f;
            origin.Y = 0.0f;

            size.X = 0.0f;
            size.Y = 0.0f;

            worldsize.X = 0.0f;
            worldsize.Y = 0.0f;

            Bgf = null;
            SubBgf.Clear();
        }

        /// <summary>
        /// Scales the renderinfo by Value
        /// </summary>
        /// <param name="Value"></param>
        protected void Scale(Real Value)
        {
            dimension.Scale(Value);
            origin.Scale(Value);
            size.Scale(Value);

            foreach (SubOverlay.RenderInfo subInfo in SubBgf)
                subInfo.Scale(Value);

            Scaling *= Value;
        }

        /// <summary>
        /// Scales the renderinfo with respect to width/height ratio.
        /// Only one side will fit the given Dimension exactly afterwards.
        /// </summary>
        /// <param name="Dimension"></param>
        /// <param name="CenterHorizontal"></param>
        /// <param name="CenterVertical"></param>
        /// <param name="MakeFit"></param>
        protected void ScaleToBox(V2 Dimension, bool CenterHorizontal = false, bool CenterVertical = false, bool MakeFit = true)
        {
            // get scales for both axis
            Real hScale = Dimension.X / this.Dimension.X;
            Real vScale = Dimension.Y / this.Dimension.Y;

            if (MakeFit)
            {
                // use the smaller factor so the other side "still fits"
                // into given dimension
                if (hScale >= vScale)
                {
                    // apply scale from y-side
                    Scale(vScale);
                }
                else
                {
                    // apply scale from x-side
                    Scale(hScale);
                }
            }

            if (CenterHorizontal)
            {
                Real stride = (Dimension.X - this.Dimension.X) / 2.0f;
                Real strideratio = stride / Dimension.X;

                Translate(new V2(stride, 0.0f));

                uvstart.X = 0.0f + strideratio;
                uvend.X = 1.0f - strideratio;
            }
            else
            {
                // adjust UVEnd on x-side
                uvend.X = 1.0f * (this.Dimension.X / Dimension.X);
            }

            if (CenterVertical)
            {
                Real stride = (Dimension.Y - this.Dimension.Y) / 2.0f;
                Real strideratio = stride / Dimension.Y;

                Translate(new V2(0.0f, stride));

                uvstart.Y = 0.0f + strideratio;
                uvend.Y = 1.0f - strideratio;
            }
            else
            {
                if (MakeFit)
                {
                    // adjust UVEnd on y-side
                    uvend.Y = 1.0f * (this.Dimension.Y / Dimension.Y);
                }
                else
                {
                    // move to bottom instead drawing from top
                    Real stride = Dimension.Y - this.Dimension.Y;

                    Translate(new V2(0.0f, stride));

                    // adjust UV coords
                    uvstart.Y = stride / Dimension.Y;
                    uvend.Y = 1.0f;
                }
            }

            this.Dimension = Dimension;
        }

        /// <summary>
        /// Moves the origins by given translation.
        /// </summary>
        /// <param name="Translation"></param>
        protected void Translate(V2 Translation)
        {
            origin.X += Translation.X;
            origin.Y += Translation.Y;

            foreach (SubOverlay.RenderInfo subInfo in SubBgf)
            {
                subInfo.Origin.X += Translation.X;
                subInfo.Origin.Y += Translation.Y;
            }
        }
    }
}
