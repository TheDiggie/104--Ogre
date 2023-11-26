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

#pragma once

#pragma managed(push, off)
#include "OgreTexture.h"
#include "CEGUI/String.h"
#pragma managed(pop)

namespace Meridian59 { namespace Ogre 
{
   using namespace ::Ogre;
   using namespace Meridian59::Drawing2D;
   using namespace Meridian59::Data::Models;
   using namespace System::Drawing;
   using namespace System::Drawing::Drawing2D;
   using namespace System::Drawing::Imaging;

   public ref struct TextureInfoCEGUI
   {
   public:
      ::CEGUI::String* TextureName;

      void Delete();
   };

   /// <summary>
   /// Implements the ImageComposer generic from the core library.
   /// Creates textures and materials for 2D objects for use in CEGUI.
   /// </summary>
   generic <typename T>
   where T : ObjectBase
   public ref class ImageComposerCEGUI : public ImageComposer<T, TextureInfoCEGUI^>
   {
   private:
      static ImageComposerCEGUI();

   protected:
      virtual void PrepareDraw() override;
      virtual void DrawBackground() override;
      virtual void DrawMainOverlay() override;
      virtual void DrawSubOverlay(SubOverlay::RenderInfo^ RenderInfo) override;
      virtual void DrawPostEffects() override;
      virtual void FinishDraw() override;

   public:
      literal unsigned int BYTESPERPIXEL = 4;

      ImageComposerCEGUI(void);
   };
};};
