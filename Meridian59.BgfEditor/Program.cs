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
using System.Threading;
using System.Windows.Forms;
using System.Diagnostics;
using Meridian59.Drawing2D;
using Meridian59.Files.BGF;
using Meridian59.Common.Constants;
using Meridian59.Data.Models;
using System.Drawing;
using System.Drawing.Drawing2D;
using Meridian59.BgfEditor.Forms;
using Meridian59.Common;

namespace Meridian59.BgfEditor
{
    static class Program
    {
        public static bool IsRunning { get; private set; }
        //public static bool IsPlaying { get; set; }
        public static long Tick { get; private set; }
        public static BgfFile CurrentFile { get; private set; }
        public static bool HasFileChanged { get; set; }
        public static MainForm MainForm { get; private set; }
        public static SettingsForm SettingsForm { get; private set; }
        public static AddFrameSetIndexForm AddFrameSetIndexForm { get; private set; }
        public static RoomObject RoomObject { get; private set; }

        private static Stopwatch stopWatch;
        private static long MSTICKDIVISOR = Stopwatch.Frequency / 1000;

        // Queue for image files to load in worker threads.
        public static LockingQueue<string> BitmapFileQueue = new LockingQueue<string>();
        // Queue for BgfBitmap objects to load into form.
        public static LockingQueue<BgfBitmap> BgfBitmapQueue = new LockingQueue<BgfBitmap>();

        /// <summary>
        /// Main entry
        /// </summary>
        [STAThread]
        static void Main()
        {
            // .net stuff
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // init bgf data model
            CurrentFile = new BgfFile();
            CurrentFile.Frames.AllowEdit = true;

            // init roomobject model for viewer
            RoomObject = new RoomObject();
            RoomObject.Resource = CurrentFile;

            // init mainform
            MainForm = new MainForm();
            MainForm.FormClosed += OnMainFormFormClosed;
            MainForm.Show();

            ImageComposerGDI<RoomObject>.Cache.IsEnabled = false;

            // init shrinkform
            SettingsForm = new SettingsForm();
            SettingsForm.DataBindings.Add("Version", CurrentFile, "Version");
            SettingsForm.DataBindings.Add("ShrinkFactor", CurrentFile, "ShrinkFactor");
            SettingsForm.DataBindings.Add("Name", CurrentFile, "Name");

            // init addframsetindexform
            AddFrameSetIndexForm = new AddFrameSetIndexForm();
            
            // init ticker
            stopWatch = new Stopwatch();
            stopWatch.Start();

            // set running
            IsRunning = true;

            HasFileChanged = false;

            string[] args = Environment.GetCommandLineArgs();

            // load file passed by arguments
            if (args.Length > 1)
                Load(args[1]);
                
            // start mainthread loop
            while (IsRunning)
            {
                long oldTick = Tick;

                // update current tick
                Tick = stopWatch.ElapsedTicks / MSTICKDIVISOR;

                long span = Tick - oldTick;

                // update roomobject
                //if (IsPlaying)
                    RoomObject.Tick(Tick, span);

                // process window messages / events
                Application.DoEvents();

                while (BgfBitmapQueue.TryDequeue(out BgfBitmap bgfBitmap))
                {
                    bgfBitmap.Num = (uint)CurrentFile.Frames.Count + 1;
                    CurrentFile.Frames.Add(bgfBitmap);
                }

                // sleep
                Thread.Sleep(1);
            }
        }

        /// <summary>
        /// Load a BGF from .bgf or .xml
        /// </summary>
        /// <param name="Filename">Full path and filename of BGF or XML</param>
        public static void Load(string Filename)
        {
            if (File.Exists(Filename))
            {
                string extension = Path.GetExtension(Filename).ToLower();

                switch (extension)
                {
                    case FileExtensions.BGF:
                        CurrentFile.Load(Filename);
                        CurrentFile.DecompressAll();
                        break;

                    case FileExtensions.XML:
                        CurrentFile.LoadXml(Filename);
                        break;
                }

                SetLoadedBgfProperties();
                HasFileChanged = false;
            }
        }

        /// <summary>
        /// Sets some form and object properties from the newly loaded file.
        /// </summary>
        public static void SetLoadedBgfProperties()
        {
            // set input controls in 'settings' window to values from file
            SettingsForm.ShrinkFactor = CurrentFile.ShrinkFactor;
            SettingsForm.Version = CurrentFile.Version;
            SettingsForm.BgfName = CurrentFile.Name;

            // set mainoverlay resource to loaded file
            RoomObject.OverlayFile = CurrentFile.Filename + ".bgf";
            RoomObject.Resource = CurrentFile;
        }

        /// <summary>
        /// Saves the current file as .bgf or .xml/.bmp
        /// </summary>
        /// <param name="Filename"></param>
        public static void Save(string Filename)
        {
            // set values in file from input controls in 'settings' window
            CurrentFile.ShrinkFactor = SettingsForm.ShrinkFactor;
            CurrentFile.Version = SettingsForm.Version;
            CurrentFile.Name = SettingsForm.BgfName;

            string extension = Path.GetExtension(Filename);
            HasFileChanged = false;
            switch (extension)
            {
                case FileExtensions.BGF:
                    if (SettingsForm.IsSaveCompresed)
                        CurrentFile.CompressAll();
                    else
                        CurrentFile.DecompressAll();

                    CurrentFile.Save(Filename);
                    break;

                case FileExtensions.XML:
                    CurrentFile.WriteXmlAndBMPs(Filename);
                    break;
            }
        }

        /// <summary>
        /// Creates a new blank bgf instance to work with
        /// </summary>
        public static void New()
        {
            CurrentFile.Clear(true);

            // set input controls in 'settings' window to values from blank file
            SettingsForm.ShrinkFactor = CurrentFile.ShrinkFactor;
            SettingsForm.Version = CurrentFile.Version;
            SettingsForm.BgfName = CurrentFile.Name;
            HasFileChanged = false;
            // unset current imageboxes
            ShowFrame(true, null, MainForm.picFrameImage);
            //ShowFrame(true, null, MainForm.picAnimation);
        }

        /// <summary>
        /// Show a Bitmap in a picturebox
        /// </summary>
        /// <param name="Scale"></param>
        /// <param name="Bitmap"></param>
        /// <param name="PictureBox"></param>
        public static void ShowFrame(bool Scale, Bitmap Bitmap, PictureBox PictureBox)
        {
            if (Bitmap == null)
                PictureBox.Image = null;

            else
            {
                if (Scale)
                {
                    // scaling calculcations
                    int MainOverlayX = 0;
                    int MainOverlayY = 0;
                    float nPercentW = ((float)PictureBox.Width / (float)Bitmap.Width);
                    float nPercentH = ((float)PictureBox.Height / (float)Bitmap.Height);

                    float MainOverlayScale;
                    if (nPercentH < nPercentW)
                    {
                        MainOverlayScale = nPercentH;
                        MainOverlayX = System.Convert.ToInt16((PictureBox.Width -
                                      (Bitmap.Width * MainOverlayScale)) / 2);
                    }
                    else
                    {
                        MainOverlayScale = nPercentW;
                        MainOverlayY = System.Convert.ToInt16((PictureBox.Height -
                                      (Bitmap.Height * MainOverlayScale)) / 2);
                    }

                    int destWidth = (int)(Bitmap.Width * MainOverlayScale);
                    int destHeight = (int)(Bitmap.Height * MainOverlayScale);

                    Bitmap bmp = new Bitmap(PictureBox.Width, PictureBox.Height);

                    // get graphics for bitmap to draw on
                    Graphics grPhoto = Graphics.FromImage(bmp);
                    grPhoto.Clear(Color.Transparent);
                    grPhoto.InterpolationMode = InterpolationMode.HighQualityBicubic;

                    // draw mainbmp into target bitmap
                    grPhoto.DrawImage(Bitmap,
                        new System.Drawing.Rectangle(MainOverlayX, MainOverlayY, destWidth, destHeight),
                        new System.Drawing.Rectangle(0, 0, Bitmap.Width, Bitmap.Height),
                        GraphicsUnit.Pixel);

                    grPhoto.Dispose();

                    PictureBox.Image = (Image)bmp;
                }
                else
                {
                    PictureBox.Image = Bitmap;
                }
            }
        }

        /// <summary>
        /// Executed when MainForm was closed.
        /// Will exit application.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private static void OnMainFormFormClosed(object sender, FormClosedEventArgs e)
        {
            IsRunning = false;
        }

        /// <summary>
        /// Returns true if images are being loaded into the bgf.
        /// </summary>
        /// <returns></returns>
        public static bool IsLoadingImages()
        {
            return BgfBitmapQueue.Count > 0 || BitmapFileQueue.Count > 0;
        }
    }
}
